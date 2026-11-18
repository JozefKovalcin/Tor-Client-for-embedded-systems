using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Tor.Adapters.Windows;
using Tor.Control;
using Tor.Core;

namespace Tor.Application;

/// <summary>Owns one explicitly started Tor session and projects the journal and adapter observations.</summary>
public sealed class OfflineTorService : IPeerTorService
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
    private IWindowsOnlineTorActions? onlineActions;
    private bool packageBound, storageReady, active, stopRequested, closing, cleanupUnconfirmed;
    private string? taskId, lastError;
    private int? requestedSocksPort;
    private RuntimePackageStatus packageStatus;
    private TorRuntimeObservation observation = new(null, false);
    private OfflineTorSnapshot snapshot = new(RuntimePackageStatus.Unchecked, TorSessionStatus.Stopped,
        TorControlStatus.Disconnected, null, null, null, null, null, [], [], false, false);

    [SupportedOSPlatform("windows")]
    public static OfflineTorService CreateWindows(string packageDirectory, string privateDirectory) =>
        CreateWindows(new PinnedWindowsRuntime(Path.GetFullPath(packageDirectory)), privateDirectory);

    [SupportedOSPlatform("windows")]
    public static OfflineTorService CreateWindows(PinnedWindowsRuntime runtime, string privateDirectory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        privateDirectory = Path.GetFullPath(privateDirectory);
        return new(runtime.PackageDirectory, privateDirectory,
            new WindowsTorSessionCommand(runtime.ExecutablePin, runtime.RuntimePins),
            observe => new WindowsTorTaskHandler(runtime, privateDirectory, observe));
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

    public Task StartAsync(CancellationToken cancellationToken = default) => StartModeAsync(null, cancellationToken);

    public Task StartOnlineAsync(int socksPort, CancellationToken cancellationToken = default)
    {
        ValidateSocksPort(socksPort);
        return StartModeAsync(socksPort, cancellationToken);
    }

    // Stop and admission share the same operations gate, so another mode cannot enter between them.
    public async Task RestartOnlineAsync(int socksPort, CancellationToken cancellationToken = default)
    {
        ValidateSocksPort(socksPort);
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate) if (closing || cleanupUnconfirmed || active && requestedSocksPort is null) return;
            var pending = await Task.Run<Task?>(RequestStop, cancellationToken).ConfigureAwait(false);
            if (pending is not null) await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            await StartUnderOperationsGateAsync(socksPort, cancellationToken).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }

    private static void ValidateSocksPort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
    }

    private async Task StartModeAsync(int? socksPort, CancellationToken cancellationToken)
    {
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StartUnderOperationsGateAsync(socksPort, cancellationToken).ConfigureAwait(false); }
        finally { operations.Release(); }
    }

    private async Task StartUnderOperationsGateAsync(int? socksPort, CancellationToken cancellationToken)
    {
        lock (gate) if (closing || active || cleanupUnconfirmed) return;
        if (!await PreflightAsync(createState: true, cancellationToken).ConfigureAwait(false)) return;
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => SubmitStart(socksPort), cancellationToken).ConfigureAwait(false);
    }

    private void SubmitStart(int? socksPort)
    {
        lock (gate)
        {
            if (closing) return;
            var orchestrator = core!;
            var id = "tor-" + Guid.NewGuid().ToString("N");
            var previous = snapshot.CurrentTask;
            var retryOf = requestedSocksPort.HasValue == socksPort.HasValue &&
                previous?.State is "failed" or "interrupted" or "blocked" or "cancelled" ? previous.TaskId : null;
            var sessionCommand = new WindowsTorSessionCommand(command.Executable, command.RuntimeInputs, socksPort);
            try { orchestrator.Submit([new TaskSubmission(id, sessionCommand, [], SessionResources, retryOf)]); }
            catch (Exception error) when (IsJournalError(error))
            {
                lastError = "Could not record the start request in the private task journal.";
                PublishLocked();
                return;
            }
            taskId = id;
            requestedSocksPort = socksPort;
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

    public async Task<OnlineActionResult> RequestNewIdentityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunOnlineActionAsync((actions, token) => actions.RequestNewIdentityAsync(token), cancellationToken)
                .ConfigureAwait(false);
            return result switch
            {
                NewIdentityResult.Applied => new(OnlineActionStatus.Succeeded, "Tor confirmed the new identity signal for future requests."),
                NewIdentityResult.Submitted => new(OnlineActionStatus.Submitted, "Tor accepted the request. Its effect has not been confirmed."),
                NewIdentityResult.RateLimited => new(OnlineActionStatus.RateLimited, "Wait before requesting another identity."),
                _ => OnlineUnavailable()
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return OnlineUnavailable(); }
        catch (Exception error) when (IsOnlineTransportError(error))
        { return new(OnlineActionStatus.Failed, "The authenticated Tor control connection could not complete the request."); }
    }

    public async Task<OnlineActionResult> CheckConnectivityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunOnlineActionAsync((actions, token) => actions.CheckConnectivityAsync(token), cancellationToken)
                .ConfigureAwait(false);
            return result switch
            {
                Socks5ConnectivityResult.Reachable => new(OnlineActionStatus.Succeeded, "The Tor Project HTTPS check succeeded through the owned SOCKS5 listener."),
                Socks5ConnectivityResult.ProxyUnavailable => new(OnlineActionStatus.Unavailable, "The verified local SOCKS5 listener did not accept the test connection."),
                Socks5ConnectivityResult.DestinationUnavailable => new(OnlineActionStatus.Failed, "The Tor Project HTTPS check could not be verified through SOCKS5."),
                _ => OnlineUnavailable()
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return OnlineUnavailable(); }
        catch (Exception error) when (IsOnlineTransportError(error))
        { return new(OnlineActionStatus.Failed, "The online Tor session did not complete the connectivity check."); }
    }

    public IOnionPeerTransport? GetPeerTransport()
    {
        lock (gate) return snapshot.Online?.CanUseOnlineActions == true ? onlineActions?.GetPeerTransport() : null;
    }

    private async Task<T?> RunOnlineActionAsync<T>(Func<IWindowsOnlineTorActions, CancellationToken, Task<T?>> start,
        CancellationToken cancellationToken) where T : struct
    {
        Task<T?> pending;
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (snapshot.Online?.CanUseOnlineActions != true || onlineActions is null) return null;
                pending = start(onlineActions, cancellationToken);
            }
        }
        finally { operations.Release(); }
        return await pending.ConfigureAwait(false);
    }

    private static OnlineActionResult OnlineUnavailable() => new(OnlineActionStatus.Unavailable,
        "Start an online session and wait for Tor bootstrap and its verified SOCKS5 listener.");

    private static bool IsOnlineTransportError(Exception error) => error is IOException or InvalidOperationException or
        System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException or
        System.Threading.Channels.ChannelClosedException;

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
            lock (gate)
            {
                core = opened;
                onlineActions = handlers.SingleOrDefault() as IWindowsOnlineTorActions;
            }
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
        var online = requestedSocksPort is not null;
        var onlineReady = authenticated && observation.BootstrapPercent == 100 &&
            observation.Online?.SocksPort is not null && observation.Online.CircuitEstablished == true;
        var session = ObserveSessionStatus(current?.State, authenticated, online, onlineReady);
        var detail = lastError ?? (authenticated ? observation.Online?.LastError : null) ?? (session switch
        {
            TorSessionStatus.Interrupted => "A previous application session was interrupted. Start a new session explicitly.",
            TorSessionStatus.Failed => online ? OnlineFailure(current?.ReasonCode) :
                "The offline task failed or was blocked. Review its phase, reason and exit code.",
            _ => null
        });
        snapshot = new(packageStatus, session, control, observation.ProcessId,
            authenticated ? observation.BootstrapPercent : null,
            authenticated ? observation.BootstrapTag : null, detail, current, tasks, core?.Logs() ?? [],
            !closing && !active && !cleanupUnconfirmed && storageReady && packageStatus == RuntimePackageStatus.Available,
            !closing && active && !stopRequested,
            requestedSocksPort is int port ? new(port, authenticated && !stopRequested ? observation.Online?.SocksPort : null,
                authenticated ? observation.Online?.TorVersion : null, authenticated ? observation.Online?.CircuitEstablished : null,
                !closing && !stopRequested && onlineReady && onlineActions is not null) : null,
            packageStatus == RuntimePackageStatus.Available ? command.Executable.Sha256 : null);
        foreach (var channel in subscribers) channel.Writer.TryWrite(snapshot);
    }

    private TorSessionStatus ObserveSessionStatus(string? taskState, bool authenticated, bool online, bool onlineReady)
    {
        if (!active)
        {
            if (cleanupUnconfirmed || taskState is "failed" or "blocked") return TorSessionStatus.Failed;
            return taskState == "interrupted" ? TorSessionStatus.Interrupted : TorSessionStatus.Stopped;
        }
        if (online && taskState is ("failed" or "blocked")) return TorSessionStatus.Failed;
        if (stopRequested || observation.TerminationConfirmed || taskState == "cancelling") return TorSessionStatus.Stopping;
        if (authenticated)
        {
            if (!online) return TorSessionStatus.RunningOffline;
            return onlineReady ? TorSessionStatus.RunningOnline : TorSessionStatus.BootstrappingOnline;
        }
        return observation.ProcessId is not null && !observation.Authenticating ? TorSessionStatus.Failed : TorSessionStatus.Starting;
    }

    private static string OnlineFailure(string? reason) => reason switch
    {
        "socks-port-in-use" => "The selected loopback SOCKS5 port is already in use. Choose another port and retry.",
        "socks-port-unavailable" => "Windows refused the selected loopback SOCKS5 port. Choose another port and retry.",
        "unverified-socks-listener" => "Tor did not provide the expected owned loopback SOCKS5 listener. The session stopped.",
        "tor-bootstrap-timeout" => "Tor could not establish a circuit before the bootstrap deadline. Check the network and retry.",
        "tor-control-lost" => "The authenticated Tor control connection became unavailable. The session stopped.",
        "unexpected-tor-exit" => "The owned Tor process exited unexpectedly. Review the exit code and restart explicitly.",
        _ => "The online Tor task failed or was blocked. Review its phase, reason and exit code."
    };

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
        finally { lock (gate) { core = null; onlineActions = null; } }
    }

    private bool IsClosing() { lock (gate) return closing; }
    private bool IsActive() { lock (gate) return active; }
    private static bool IsJournalError(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or
            AggregateException or OverflowException or System.Text.Json.JsonException;
}
