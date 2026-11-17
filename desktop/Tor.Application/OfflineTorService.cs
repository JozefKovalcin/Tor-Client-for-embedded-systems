using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Tor.Adapters.Windows;
using Tor.Core;

namespace Tor.Application;

/// <summary>Owns one explicit offline Tor session and projects the W5 journal and adapter observations.</summary>
public sealed class OfflineTorService : IOfflineTorService
{
    private static readonly ResourceRequest SessionResources = new(1, 0, 0, false);
    private readonly string packageDirectory;
    private readonly string privateDirectory;
    private readonly WindowsTorSessionCommand command;
    private readonly Func<Action<TorRuntimeObservation>, ITaskHandler> handlerFactory;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object gate = new();
    private readonly List<Channel<OfflineTorSnapshot>> subscribers = [];
    private TaskOrchestrator? core;
    private CancellationTokenSource? monitorCancellation;
    private Task? monitor, run, dispose;
    private bool packageBound, storageReady, active, stopRequested, closing, cleanupUnconfirmed;
    private string? taskId, lastError;
    private RuntimePackageStatus packageStatus;
    private TorRuntimeObservation observation = new(null, false);
    private OfflineTorSnapshot snapshot = new(RuntimePackageStatus.Unchecked, TorSessionStatus.Stopped,
        TorControlStatus.Disconnected, null, null, null, null, null, [], [], false, false);

    [SupportedOSPlatform("windows")]
    public static OfflineTorService CreateWindows(string packageDirectory, string privateDirectory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        packageDirectory = Path.GetFullPath(packageDirectory);
        privateDirectory = Path.GetFullPath(privateDirectory);
        return new(packageDirectory, privateDirectory,
            new WindowsTorSessionCommand(PinnedWindowsRuntime.Executable, [PinnedWindowsRuntime.ThreadLibrary]),
            observe => new WindowsTorTaskHandler(new PinnedWindowsRuntime(packageDirectory), privateDirectory, observe));
    }

    public OfflineTorService(string packageDirectory, string privateDirectory, WindowsTorSessionCommand command,
        Func<Action<TorRuntimeObservation>, ITaskHandler> createHandler)
    {
        this.packageDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory));
        this.privateDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(privateDirectory));
        this.command = command;
        handlerFactory = createHandler;
    }

    public OfflineTorSnapshot Snapshot { get { lock (gate) return snapshot; } }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsClosing()) return;
            await PreflightAsync(createState: false, cancellationToken).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate) if (closing || active || cleanupUnconfirmed) return;
            if (!await PreflightAsync(createState: true, cancellationToken).ConfigureAwait(false)) return;
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(SubmitStart, cancellationToken).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }

    private void SubmitStart()
    {
        lock (gate)
        {
            if (closing) return;
            var orchestrator = core!;
            var id = "tor-" + Guid.NewGuid().ToString("N");
            var previous = snapshot.CurrentTask;
            var retryOf = previous?.State is "failed" or "interrupted" or "blocked" or "cancelled" ? previous.TaskId : null;
            try { orchestrator.Submit([new TaskSubmission(id, command, [], SessionResources, retryOf)]); }
            catch (Exception error) when (IsJournalError(error))
            {
                lastError = "Could not record the start request in the private task journal.";
                PublishLocked();
                return;
            }
            taskId = id;
            active = true;
            stopRequested = false;
            lastError = null;
            observation = new(null, false);
            PublishLocked();
            run = Task.Run(() => RunAsync(orchestrator));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? pending;
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { pending = await Task.Run<Task?>(RequestStop, cancellationToken).ConfigureAwait(false); }
        finally { operations.Release(); }
        if (pending is not null) await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task? RequestStop()
    {
        lock (gate)
        {
            if (!active) return run;
            if (stopRequested) return run;
            stopRequested = true;
            try { core!.Cancel(taskId!); }
            catch (Exception error) when (IsJournalError(error))
            {
                lastError = "The task journal could not record the stop request. Cleanup is continuing.";
            }
            PublishLocked();
            return run;
        }
    }

    private async Task RunAsync(TaskOrchestrator orchestrator)
    {
        try { await orchestrator.RunUntilIdleAsync().ConfigureAwait(false); }
        catch (Exception error) when (IsJournalError(error))
        {
            lock (gate) lastError = "The private task journal could not record a task update. Review its permissions and free space.";
        }
        finally
        {
            lock (gate)
            {
                active = false;
                stopRequested = false;
                cleanupUnconfirmed = observation.ProcessId is not null;
                if (cleanupUnconfirmed)
                    lastError = "Tor termination could not be confirmed. Close the application and review the owned process before restarting.";
                PublishLocked();
            }
        }
    }

    private async Task<bool> PreflightAsync(bool createState, CancellationToken token)
    {
        var (status, store) = await Task.Run(ProbePackage, token).ConfigureAwait(false);
        bool available = status == RuntimePackageStatus.Available;
        bool ready;
        try
        {
            ready = await Task.Run(() => CheckPrivateDirectory(createState && available), token).ConfigureAwait(false);
            bool openJournal = createState && available || File.Exists(Path.Combine(privateDirectory, "tasks.json"));
            if (ready && openJournal && Directory.Exists(privateDirectory) && !IsActive() &&
                (core is null || !packageBound && store is not null))
            {
                await CloseCoreAsync().ConfigureAwait(false);
                await Task.Run(() => OpenCore(store), token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (IsJournalError(error) || error is ArgumentException)
        {
            ready = false;
        }
        lock (gate)
        {
            packageStatus = status;
            storageReady = ready;
            if (!active && !cleanupUnconfirmed)
                lastError = !ready ? "The private data directory or journal is unavailable. Check ownership, permissions and another running instance." : status switch
                {
                    RuntimePackageStatus.Missing => "The reviewed Tor executable or its companion DLL is missing.",
                    RuntimePackageStatus.Invalid => "The selected Tor package failed path or hash verification.",
                    RuntimePackageStatus.Blocked => "Could not read the reviewed Tor package from a local drive.",
                    _ => null
                };
            PublishLocked();
        }
        return available && ready && core is not null;
    }

    private (RuntimePackageStatus, VerifiedFileStore?) ProbePackage()
    {
        VerifiedFileStore? store = null;
        try
        {
            RequireLocalPath(packageDirectory);
            store = new VerifiedFileStore(packageDirectory);
            foreach (var input in command.Inputs) store.Verify(input);
            return (RuntimePackageStatus.Available, store);
        }
        catch (Exception error) when (error is DirectoryNotFoundException or FileNotFoundException)
        { return (RuntimePackageStatus.Missing, store); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { return (RuntimePackageStatus.Invalid, store); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return (RuntimePackageStatus.Blocked, store); }
    }

    private bool CheckPrivateDirectory(bool create)
    {
        RequireLocalPath(privateDirectory);
        var relative = Path.GetRelativePath(packageDirectory, privateDirectory);
        var reverse = Path.GetRelativePath(privateDirectory, packageDirectory);
        if (Within(relative) || Within(reverse))
            throw new InvalidDataException("Private data directory must be separate from the runtime package.");
        if (Directory.Exists(privateDirectory))
        {
            PrivateStorage.RequireOwnerOnly(privateDirectory);
            return true;
        }
        if (File.Exists(privateDirectory)) return false;
        var parent = Path.GetDirectoryName(privateDirectory);
        if (string.IsNullOrEmpty(parent)) return false;
        _ = new VerifiedFileStore(parent);
        if (create) PrivateStorage.CreateNewDirectory(privateDirectory);
        return true;
    }

    private static bool Within(string relative) => !Path.IsPathRooted(relative) && relative != ".." &&
        !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static void RequireLocalPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.GetPathRoot(path);
        if (root is null || root.StartsWith("\\\\", StringComparison.Ordinal) || new DriveInfo(root).DriveType is DriveType.Network)
            throw new IOException("Use a local drive.");
    }

    private void OpenCore(VerifiedFileStore? store)
    {
        var journal = new TaskJournal(privateDirectory);
        try
        {
            var handlers = store is null ? Array.Empty<ITaskHandler>() : new[] { handlerFactory(Observe) };
            var opened = new TaskOrchestrator(handlers, store ?? new VerifiedFileStore(privateDirectory), journal,
                SessionResources, maximumTasks: 1024, maximumTaskDuration: TimeSpan.FromHours(24));
            var cancellation = new CancellationTokenSource();
            lock (gate) core = opened;
            packageBound = store is not null;
            monitorCancellation = cancellation;
            monitor = MonitorCoreAsync(opened, cancellation.Token);
        }
        catch { journal.Dispose(); throw; }
    }

    private async Task MonitorCoreAsync(TaskOrchestrator orchestrator, CancellationToken token)
    {
        try
        {
            await foreach (var _ in orchestrator.WatchProgressAsync(token).ConfigureAwait(false))
                lock (gate) PublishLocked();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void Observe(TorRuntimeObservation value)
    {
        lock (gate)
        {
            if (!active) return;
            // Never erase a known live PID without confirmation from the owning adapter.
            observation = value with { ProcessId = value.ProcessId ?? (value.TerminationConfirmed ? null : observation.ProcessId) };
            PublishLocked();
        }
    }

    private void PublishLocked()
    {
        var tasks = core?.Snapshot() ?? [];
        var current = tasks.LastOrDefault(row => row.TaskId == taskId) ?? tasks.LastOrDefault();
        bool authenticated = active && observation.Authenticated;
        var control = authenticated ? TorControlStatus.Authenticated : active && observation.Authenticating
            ? TorControlStatus.Authenticating : TorControlStatus.Disconnected;
        var session = active ? stopRequested || observation.TerminationConfirmed || current?.State == "cancelling"
            ? TorSessionStatus.Stopping : authenticated ? TorSessionStatus.RunningOffline
            : observation.ProcessId is not null && !observation.Authenticating ? TorSessionStatus.Failed
            : TorSessionStatus.Starting
            : cleanupUnconfirmed ? TorSessionStatus.Failed : current?.State switch
            {
                "failed" or "blocked" => TorSessionStatus.Failed,
                "interrupted" => TorSessionStatus.Interrupted,
                _ => TorSessionStatus.Stopped
            };
        var detail = lastError ?? (session switch
        {
            TorSessionStatus.Interrupted => "A previous application session was interrupted. Start a new session explicitly.",
            TorSessionStatus.Failed => "The offline task failed or was blocked. Review its phase, reason and exit code.",
            _ => null
        });
        snapshot = new(packageStatus, session, control, observation.ProcessId,
            authenticated ? observation.BootstrapPercent : null,
            authenticated ? observation.BootstrapTag : null, detail, current, tasks, core?.Logs() ?? [],
            !closing && !active && !cleanupUnconfirmed && storageReady && packageStatus == RuntimePackageStatus.Available,
            !closing && active && !stopRequested);
        foreach (var channel in subscribers) channel.Writer.TryWrite(snapshot);
    }

    public async IAsyncEnumerable<OfflineTorSnapshot> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<OfflineTorSnapshot>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
        lock (gate)
        {
            if (closing) yield break;
            if (subscribers.Count >= 8) throw new InvalidOperationException("Too many view subscriptions.");
            subscribers.Add(channel);
            channel.Writer.TryWrite(snapshot);
        }
        try { await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return item; }
        finally { lock (gate) subscribers.Remove(channel); }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (dispose is not null) return new(dispose);
            closing = true;
            PublishLocked();
            dispose = Task.Run(CloseAsync);
            return new(dispose);
        }
    }

    private async Task CloseAsync()
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            try { if (RequestStop() is { } pending) await pending.ConfigureAwait(false); }
            finally { await CloseCoreAsync().ConfigureAwait(false); }
        }
        finally
        {
            lock (gate) foreach (var subscriber in subscribers) subscriber.Writer.TryComplete();
            operations.Release();
        }
    }

    private async Task CloseCoreAsync()
    {
        var existing = core;
        monitorCancellation?.Cancel();
        if (monitor is not null) await monitor.ConfigureAwait(false);
        monitorCancellation?.Dispose();
        monitorCancellation = null;
        monitor = null;
        if (existing is null) return;
        try { await existing.DisposeAsync().ConfigureAwait(false); }
        finally { lock (gate) core = null; }
    }

    private bool IsClosing() { lock (gate) return closing; }
    private bool IsActive() { lock (gate) return active; }
    private static bool IsJournalError(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or
            AggregateException or OverflowException or System.Text.Json.JsonException;
}
