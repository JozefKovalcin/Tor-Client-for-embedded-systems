using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading.Channels;
using Tor.Adapters.Windows;
using Tor.Application;
using Tor.Contracts;
using Tor.Core;

internal static class Program
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    private static async Task Main(string[] args)
    {
        if (args is ["--owned-child", var childPackage, var childState, var marker])
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            await using var child = OfflineTorService.CreateWindows(childPackage, childState);
            await child.StartAsync();
            await Until(child, s => s.ControlStatus == TorControlStatus.Authenticated && s.ProcessId is not null);
            var pending = marker + ".pending";
            await File.WriteAllTextAsync(pending, child.Snapshot.ProcessId!.Value.ToString(CultureInfo.InvariantCulture));
            File.Move(pending, marker);
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return;
        }
        if (args is ["--windows-smoke", var runtime, var nativeRoot])
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            Assert(Path.IsPathFullyQualified(runtime) && Path.IsPathFullyQualified(nativeRoot),
                "Native smoke requires explicit absolute input and test-state directories");
            Directory.CreateDirectory(nativeRoot);
            await NativeSmoke(runtime, nativeRoot);
            return;
        }
        if (args is ["--windows-developer-cancel", var workspace])
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            await DeveloperCancellation(workspace).WaitAsync(TimeSpan.FromSeconds(50));
            Console.WriteLine("PASS native-developer-cancellation-kills-worker-and-child");
            return;
        }
        if (args is not ["--workspace", var root] || !Path.IsPathFullyQualified(root))
            throw new ArgumentException("Use --workspace <absolute temporary test directory> or --windows-smoke <runtime> <test directory>");
        Directory.CreateDirectory(root);
        await Group("service-repeated-start-stop-and-double-start", () => Repeat(Fresh(root)));
        await Group("service-stop-during-startup-and-retry", () => StopDuringStartup(Fresh(root)));
        await Group("service-close-during-startup", () => CloseWhileActive(Fresh(root), false));
        await Group("service-close-during-shutdown", () => CloseWhileActive(Fresh(root), true));
        await Group("service-unconfirmed-stop-retains-owned-pid", () => UnconfirmedStop(Fresh(root)));
        await Group("service-unexpected-exit", () => Unexpected(Fresh(root)));
        await Group("service-missing-invalid-and-recovered-package", () => InvalidPackages(Fresh(root)));
        await Group("service-invalid-private-directory", () => InvalidStorage(Fresh(root)));
        await Group("service-journal-publication-error", () => JournalFailure(Fresh(root)));
        await Group("service-crash-recovery-does-not-launch", () => Recovery(Fresh(root)));
        await Group("service-slow-subscriber-keeps-current-snapshot", () => SlowSubscriber(Fresh(root)));
        Console.WriteLine("11 offline application behavior groups PASS");
    }

    private static async Task Group(string name, Func<Task> test)
    {
        await test().WaitAsync(TimeSpan.FromSeconds(50));
        Console.WriteLine("PASS " + name);
    }

    [SupportedOSPlatform("windows")]
    private static async Task NativeSmoke(string runtime, string root)
    {
        await Group("native-service-repeat-authenticated-stop", () => NativeRepeat(runtime, Fresh(root)));
        await Group("native-service-unexpected-exit", () => NativeUnexpected(runtime, Fresh(root)));
        await Group("native-service-owner-death-and-recovery", () => NativeCrash(runtime, Fresh(root)));
        await Group("native-runtime-activation-lease-and-rollback", () => RuntimeSelection(runtime, Fresh(root)));
    }

    [SupportedOSPlatform("windows")]
    private static async Task DeveloperCancellation(string workspace)
    {
        // The test output hosts only a synthetic worker. It leaves the reviewed
        // desktop payload, native recipe and existing artifacts untouched.
        var launcher = Path.Combine(AppContext.BaseDirectory, "developer", "scripts", "windows_developer", "launch.py");
        Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
        using (var file = new FileStream(launcher, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(file))
            writer.Write("""
                import os, subprocess, sys, time
                assert input() == "owned-windows-job"
                child = subprocess.Popen([sys.executable, "-I", "-c", "import time; time.sleep(120)"])
                print(str(os.getpid()) + " " + str(child.pid), flush=True)
                time.sleep(120)
                """);
        try
        {
            var pids = new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancel = new CancellationTokenSource();
            var spec = new WindowsDeveloperCommand(WindowsDeveloperAction.Build, "optimized-static", workspace, workspace);
            var run = WindowsDeveloperRunner.RunAsync(spec, line =>
            {
                var fields = line.Split(' ');
                if (fields.Length == 2 && fields.All(field => int.TryParse(field, out _)))
                    pids.TrySetResult(fields.Select(int.Parse).ToArray());
            }, cancel.Token);
            try
            {
                var first = await Task.WhenAny(pids.Task, run).WaitAsync(TimeSpan.FromSeconds(10));
                if (first == run) throw new InvalidOperationException("Developer test worker exited before starting a child.");
                var owned = await pids.Task;
                cancel.Cancel();
                try { await run.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Cancellation did not reach the job."); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                foreach (var pid in owned)
                {
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (ArgumentException) { } // An exited process may already have no PID mapping.
                }
            }
            finally { cancel.Cancel(); }
        }
        finally { File.Delete(launcher); }
    }

    [SupportedOSPlatform("windows")]
    private static async Task RuntimeSelection(string bundled, string root)
    {
        var selectionRoot = Path.Combine(root, "selection");
        var selection = new WindowsRuntimeSelection(selectionRoot);
        var original = selection.ReadRuntime(bundled);
        var package = Path.Combine(root, "builds", "run-fixture", "pipeline", "dist");
        Directory.CreateDirectory(package);
        foreach (var name in new[] { "tor.exe", "libwinpthread-1.dll", "source-manifest.json", "package-report.json" })
            File.Copy(Path.Combine(bundled, name), Path.Combine(package, name));
        string Hash(string name) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(package, name))));
        var candidate = new RuntimeCandidate("run-fixture", "0.4.9.14", "optimized-static", package,
            Hash("package-report.json"), Hash("source-manifest.json"),
            [new("tor.exe", Hash("tor.exe")), new("libwinpthread-1.dll", Hash("libwinpthread-1.dll"))]);

        await using (var app = OfflineTorService.CreateWindows(original, Path.Combine(root, "state")))
        {
            await app.StartAsync();
            await Until(app, state => state.ControlStatus == TorControlStatus.Authenticated);
            try { selection.Activate(candidate); throw new InvalidOperationException("Activation bypassed an active session."); }
            catch (IOException) { }
            await app.StopAsync();
            await Until(app, state => state.SessionStatus == TorSessionStatus.Stopped);
        }
        selection.Activate(candidate);
        var approved = selection.ReadRuntime(bundled);
        Assert(approved.PackageDirectory != bundled && approved.RuntimePins.Count == 1, "Approved package was not independently staged.");
        await using (var app = OfflineTorService.CreateWindows(approved, Path.Combine(root, "approved-state")))
        {
            await app.StartAsync();
            await Until(app, state => state.ControlStatus == TorControlStatus.Authenticated);
            await app.StopAsync();
            await Until(app, state => state.SessionStatus == TorSessionStatus.Stopped);
        }
        File.AppendAllText(Path.Combine(approved.PackageDirectory, "libwinpthread-1.dll"), "altered");
        try { selection.ReadRuntime(bundled); throw new InvalidOperationException("Tampered DLL was accepted."); }
        catch (InvalidDataException) { }
        selection.Rollback(bundled);
        Assert(selection.ReadRuntime(bundled).PackageDirectory == bundled, "Rollback did not restore the bundled pin.");
        File.AppendAllText(Path.Combine(package, "tor.exe"), "altered");
        try { selection.Activate(candidate); throw new InvalidOperationException("Tampered Tor executable was accepted."); }
        catch (InvalidDataException) { }
        Assert(selection.ReadRuntime(bundled).PackageDirectory == bundled, "A failed activation changed the current selection.");
    }

    private static string Fresh(string parent)
    {
        var path = Path.Combine(parent, "case-" + Guid.NewGuid().ToString("N"));
        PrivateStorage.CreateNewDirectory(path);
        return path;
    }

    private static void Assert(bool test, string why)
    {
        if (!test) throw new InvalidOperationException(why);
    }

    private static async Task Until(IOfflineTorService service, Func<OfflineTorSnapshot, bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate(service.Snapshot))
        {
            if (timer.Elapsed > Limit)
            {
                var snapshot = service.Snapshot;
                throw new TimeoutException($"Expected state not reached. State={snapshot.SessionStatus}; " +
                    $"package={snapshot.PackageStatus}; task={snapshot.CurrentTask?.State}; error={snapshot.LastError}");
            }
            await Task.Delay(10);
        }
    }

    private static async Task Repeat(string root)
    {
        var fixture = new Fixture(root);
        await using var service = fixture.Create();
        await service.RefreshAsync();
        Assert(service.Snapshot.PackageStatus == RuntimePackageStatus.Available, "Pinned fixture not recognized");
        for (var run = 1; run <= 3; ++run)
        {
            await Task.WhenAll(service.StartAsync(), service.StartAsync());
            var attempt = await fixture.Next();
            attempt.Authenticate();
            await Until(service, s => s is { SessionStatus: TorSessionStatus.RunningOffline,
                ControlStatus: TorControlStatus.Authenticated, BootstrapPercent: 0 });
            Assert(fixture.Calls == run && fixture.Active == 1, "Overlapping Start created duplicate work");
            var stop = service.StopAsync();
            var secondStop = service.StopAsync();
            await attempt.CancellationObserved.Task.WaitAsync(Limit);
            Assert(!stop.IsCompleted && !secondStop.IsCompleted && fixture.Active == 1,
                "Stop acknowledged before the adapter finished");
            attempt.CompleteStop();
            await Task.WhenAll(stop, secondStop).WaitAsync(Limit);
            await Until(service, s => s is { SessionStatus: TorSessionStatus.Stopped,
                ProcessId: null, ControlStatus: TorControlStatus.Disconnected });
            Assert(service.Snapshot.CurrentTask is { State: "cancelled", RawExitCode: 0 } && fixture.Active == 0,
                "An actual clean exit code must survive cancellation");
        }
        Assert(service.Snapshot.Tasks.Select(t => t.TaskId).Distinct().Count() == 3,
            "Repeated explicit starts did not produce distinct task IDs");
        foreach (var task in service.Snapshot.Tasks) task.Validate();
    }

    private static async Task StopDuringStartup(string root)
    {
        var fixture = new Fixture(root);
        await using var service = fixture.Create();
        await service.StartAsync();
        var first = await fixture.Next();
        var firstId = service.Snapshot.CurrentTask?.TaskId;
        Assert(service.Snapshot.SessionStatus == TorSessionStatus.Starting &&
            service.Snapshot.ControlStatus != TorControlStatus.Authenticated, "Startup should not announce authentication");
        var stop = service.StopAsync();
        await first.CancellationObserved.Task.WaitAsync(Limit);
        first.CompleteStop();
        await stop.WaitAsync(Limit);
        Assert(fixture.Active == 0 && service.Snapshot.SessionStatus == TorSessionStatus.Stopped,
            "Stopping before authentication must release the task");
        await service.StartAsync();
        var retry = await fixture.Next();
        retry.Authenticate();
        await Until(service, s => s.SessionStatus == TorSessionStatus.RunningOffline);
        Assert(firstId is not null && service.Snapshot.CurrentTask is { } restarted && restarted.TaskId != firstId &&
            restarted.RetryOfTaskId == firstId && fixture.Calls == 2,
            "Explicit retry reused an old task ID, lost lineage or failed to launch");
        retry.CompleteStop();
        await service.StopAsync().WaitAsync(Limit);
    }

    private static async Task CloseWhileActive(string root, bool alreadyStopping)
    {
        var fixture = new Fixture(root);
        var service = fixture.Create();
        await service.StartAsync();
        var attempt = await fixture.Next();
        Task? stop = null;
        if (alreadyStopping)
        {
            attempt.Authenticate();
            await Until(service, s => s.SessionStatus == TorSessionStatus.RunningOffline);
            stop = service.StopAsync();
            await attempt.CancellationObserved.Task.WaitAsync(Limit);
        }
        var close = service.DisposeAsync().AsTask();
        try
        {
            await attempt.CancellationObserved.Task.WaitAsync(Limit);
            Assert(!close.IsCompleted && fixture.Active == 1, "Close released ownership before adapter cleanup");
        }
        finally { attempt.CompleteStop(); }
        await close.WaitAsync(Limit);
        if (stop is not null) await stop.WaitAsync(Limit);
        Assert(fixture.Active == 0, "Adapter still active after close");
        await using var reopened = fixture.Create();
        await reopened.RefreshAsync();
        Assert(fixture.Calls == 1 && reopened.Snapshot.CurrentTask?.State is "cancelled",
            "Closing did not persist cancellation or reopening silently relaunched");
    }

    private static async Task Unexpected(string root)
    {
        var fixture = new Fixture(root);
        await using var service = fixture.Create();
        await service.StartAsync();
        var attempt = await fixture.Next();
        attempt.Authenticate();
        await Until(service, s => s.SessionStatus == TorSessionStatus.RunningOffline);
        const uint unsignedExit = 0xE0000055;
        attempt.Unexpected.SetResult(new(unsignedExit, FailureReason: "unexpected-tor-exit"));
        await Until(service, s => s.SessionStatus == TorSessionStatus.Failed);
        Assert(service.Snapshot is { ProcessId: null, ControlStatus: TorControlStatus.Disconnected,
            CurrentTask.RawExitCode: unsignedExit } && fixture.Active == 0,
            "Unexpected death was not reflected or lost the unsigned exit code");
    }

    private static async Task UnconfirmedStop(string root)
    {
        var fixture = new Fixture(root);
        await using var service = fixture.Create();
        await service.StartAsync();
        var attempt = await fixture.Next();
        attempt.Authenticate();
        await Until(service, s => s.SessionStatus == TorSessionStatus.RunningOffline);
        var pid = service.Snapshot.ProcessId;
        attempt.ConfirmTermination = false;
        attempt.CompleteStop();
        await service.StopAsync().WaitAsync(Limit);
        Assert(service.Snapshot is { SessionStatus: TorSessionStatus.Failed, CanStart: false, LastError: not null }
            && service.Snapshot.ProcessId == pid && pid is not null,
            "An unconfirmed adapter exit cleared the known PID or presented Stop as successful");
        await service.StartAsync();
        Assert(fixture.Calls == 1, "Unconfirmed termination allowed another Start");
    }

    private static async Task InvalidPackages(string root)
    {
        var fixture = new Fixture(root);
        File.Move(fixture.ExecutablePath, fixture.ExecutablePath + ".reviewed");
        await using var service = fixture.Create();
        await service.RefreshAsync();
        Assert(service.Snapshot.PackageStatus == RuntimePackageStatus.Missing && !service.Snapshot.CanStart,
            "A missing executable must block Start");
        await service.StartAsync();
        Assert(fixture.Calls == 0, "A missing executable reached the handler");
        File.WriteAllText(fixture.ExecutablePath, "synthetic hash mismatch");
        await service.RefreshAsync();
        Assert(service.Snapshot.PackageStatus == RuntimePackageStatus.Invalid && !service.Snapshot.CanStart,
            "A mismatched executable must block Start");
        await service.StartAsync();
        Assert(fixture.Calls == 0, "An unverified executable reached the handler");
        File.Move(fixture.ExecutablePath + ".reviewed", fixture.ExecutablePath, overwrite: true);
        await service.RefreshAsync();
        Assert(service.Snapshot.PackageStatus == RuntimePackageStatus.Available && service.Snapshot.CanStart,
            "Correcting the package did not restore explicit start");
    }

    private static async Task InvalidStorage(string root)
    {
        var fixture = new Fixture(root);
        File.WriteAllText(fixture.State, "owner test fixture, not a directory");
        await using var service = fixture.Create();
        await service.StartAsync();
        Assert(fixture.Calls == 0 && File.ReadAllText(fixture.State) == "owner test fixture, not a directory",
            "Invalid private storage reached the handler or was changed");
        Assert(!service.Snapshot.CanStop && service.Snapshot.SessionStatus != TorSessionStatus.RunningOffline &&
            service.Snapshot.LastError is not null,
            "Invalid private storage was displayed as live");
    }

    private static async Task JournalFailure(string root)
    {
        var fixture = new Fixture(root);
        await using var service = fixture.Create();
        await service.StartAsync();
        var first = await fixture.Next();
        first.CompleteStop();
        await service.StopAsync().WaitAsync(Limit);
        var journalPath = Directory.GetFiles(fixture.State, "tasks.json", SearchOption.AllDirectories).Single();
        var saved = journalPath + ".reviewed";
        File.Move(journalPath, saved);
        Directory.CreateDirectory(journalPath);
        try
        {
            await service.StartAsync();
            Assert(fixture.Calls == 1 && fixture.Active == 0 && service.Snapshot.LastError is not null &&
                service.Snapshot.SessionStatus != TorSessionStatus.RunningOffline,
                "A journal publication failure launched Tor or lacked a visible failure");
        }
        finally
        {
            Directory.Delete(journalPath);
            File.Move(saved, journalPath);
        }
        await service.StartAsync();
        var replacement = await fixture.Next();
        replacement.CompleteStop();
        await service.StopAsync().WaitAsync(Limit);
        Assert(fixture.Calls == 2 && fixture.Active == 0, "Journal recovery did not permit an explicit restart");
    }

    private static async Task Recovery(string root)
    {
        var fixture = new Fixture(root);
        TaskProgress previous;
        await using (var service = fixture.Create())
        {
            await service.StartAsync();
            var attempt = await fixture.Next();
            attempt.Authenticate();
            await Until(service, s => s.SessionStatus == TorSessionStatus.RunningOffline);
            previous = service.Snapshot.CurrentTask!;
            attempt.CompleteStop();
            await service.StopAsync().WaitAsync(Limit);
        }
        var path = Directory.GetFiles(fixture.State, "tasks.json", SearchOption.AllDirectories).Single();
        using (var journal = new TaskJournal(Path.GetDirectoryName(path)!)) journal.Write([previous]);
        await using var reopened = fixture.Create();
        await reopened.RefreshAsync();
        await Until(reopened, s => s.SessionStatus == TorSessionStatus.Interrupted);
        Assert(reopened.Snapshot.CurrentTask is { State: "interrupted", ReasonCode: "app-crash", RawExitCode: null }
            && fixture.Calls == 1 && fixture.Active == 0, "Recovered running work was lost, synthesized an exit or relaunched");
    }

    private static async Task SlowSubscriber(string root)
    {
        var fixture = new Fixture(root);
        await using var service = fixture.Create();
        using var readerTimeout = new CancellationTokenSource(Limit);
        await using var reader = service.WatchAsync(readerTimeout.Token).GetAsyncEnumerator();
        Assert(await reader.MoveNextAsync(), "A new reader did not get the current state");
        await service.StartAsync();
        var attempt = await fixture.Next();
        attempt.Authenticate();
        await Until(service, s => s.SessionStatus == TorSessionStatus.RunningOffline);
        for (var i = 0; i < 1000; ++i) attempt.ReportBootstrap(i % 100);
        attempt.CompleteStop();
        await service.StopAsync().WaitAsync(Limit);
        Assert(await reader.MoveNextAsync() && reader.Current is { SessionStatus: TorSessionStatus.Stopped,
            CurrentTask.State: "cancelled", ProcessId: null },
            "A slow subscriber retained a stale backlog instead of the latest full state");
    }

    [SupportedOSPlatform("windows")]
    private static async Task NativeRepeat(string package, string root)
    {
        await using var service = OfflineTorService.CreateWindows(package, Path.Combine(root, "state"));
        for (var run = 0; run < 3; ++run)
        {
            await Task.WhenAll(service.StartAsync(), service.StartAsync());
            using var owned = await AuthenticatedProcess(service);
            await service.StopAsync().WaitAsync(Limit);
            await owned.WaitForExitAsync().WaitAsync(Limit);
            Assert(service.Snapshot is { SessionStatus: TorSessionStatus.Stopped, ProcessId: null,
                ControlStatus: TorControlStatus.Disconnected, CurrentTask.RawExitCode: 0 },
                "Managed Windows Tor did not report authenticated start and completed stop");
        }
        Assert(service.Snapshot.Tasks.Count == 3, "Double Start submitted another native process");
    }

    [SupportedOSPlatform("windows")]
    private static async Task NativeUnexpected(string package, string root)
    {
        await using var service = OfflineTorService.CreateWindows(package, Path.Combine(root, "state"));
        await service.StartAsync();
        using var owned = await AuthenticatedProcess(service);
        owned.Kill();
        await owned.WaitForExitAsync().WaitAsync(Limit);
        await Until(service, s => s.SessionStatus == TorSessionStatus.Failed);
        Assert(service.Snapshot.CurrentTask?.RawExitCode == unchecked((uint)owned.ExitCode) &&
            service.Snapshot.ProcessId is null && service.Snapshot.ControlStatus == TorControlStatus.Disconnected,
            "Service did not expose the verified Tor process's observed raw exit");
    }

    [SupportedOSPlatform("windows")]
    private static async Task NativeCrash(string package, string root)
    {
        var marker = Path.Combine(root, "owned-pid.txt");
        var state = Path.Combine(root, "state");
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in new[] { "--owned-child", package, state, marker }) start.ArgumentList.Add(arg);
        using var owner = Process.Start(start) ?? throw new IOException("Could not start lifecycle test child");
        var stdout = owner.StandardOutput.ReadToEndAsync();
        var stderr = owner.StandardError.ReadToEndAsync();
        Process? owned = null;
        try
        {
            var timer = Stopwatch.StartNew();
            do
            {
                if (owner.HasExited || timer.Elapsed > Limit) throw new TimeoutException("Child did not authenticate offline Tor");
                if (File.Exists(marker) && int.TryParse(await File.ReadAllTextAsync(marker), CultureInfo.InvariantCulture, out var id))
                    owned = Process.GetProcessById(id);
                else await Task.Delay(25);
            } while (owned is null);
            _ = owned.Handle; // Keep the handle so PID reuse cannot change the process under test.
            owner.Kill(entireProcessTree: false);
            await owner.WaitForExitAsync().WaitAsync(Limit);
            await owned.WaitForExitAsync().WaitAsync(Limit);
            await using var reopened = OfflineTorService.CreateWindows(package, state);
            await reopened.RefreshAsync();
            await Until(reopened, s => s.SessionStatus == TorSessionStatus.Interrupted);
            Assert(reopened.Snapshot is { ProcessId: null, CurrentTask.State: "interrupted", CurrentTask.ReasonCode: "app-crash" },
                "Crash recovery relaunched Tor or failed to show interrupted task");
        }
        finally
        {
            if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync().WaitAsync(Limit); }
            if (owned is not null)
            {
                if (!owned.HasExited) { owned.Kill(); await owned.WaitForExitAsync().WaitAsync(Limit); }
                owned.Dispose();
            }
            await Task.WhenAll(stdout, stderr).WaitAsync(Limit);
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<Process> AuthenticatedProcess(IOfflineTorService service)
    {
        await Until(service, s => s is { SessionStatus: TorSessionStatus.RunningOffline,
            ControlStatus: TorControlStatus.Authenticated, ProcessId: not null, BootstrapPercent: 0 });
        var process = Process.GetProcessById(service.Snapshot.ProcessId!.Value);
        _ = process.Handle;
        Assert(!process.HasExited, "Reported Windows Tor process had exited");
        return process;
    }

    private sealed class Fixture
    {
        private readonly string package;
        private readonly WindowsTorSessionCommand command;
        private readonly Channel<Attempt> attempts = Channel.CreateUnbounded<Attempt>();
        private int active;
        private int calls;
        public string State { get; }
        public string ExecutablePath => Path.Combine(package, command.Executable.Ref);
        public int Active => Volatile.Read(ref active);
        public int Calls => Volatile.Read(ref calls);

        public Fixture(string root)
        {
            package = Fresh(root);
            State = Path.Combine(root, "state");
            command = new(Pin("tor.exe", "verified synthetic application fixture"),
                [Pin("libwinpthread-1.dll", "verified synthetic dependency fixture")]);
        }

        private VerifiedFile Pin(string name, string contents)
        {
            var path = Path.Combine(package, name);
            File.WriteAllText(path, contents);
            return new(name, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }

        public IOfflineTorService Create() => new OfflineTorService(package, State, command,
            observer => new Handler(this, observer));

        public Task<Attempt> Next() => attempts.Reader.ReadAsync().AsTask().WaitAsync(Limit);

        private sealed class Handler(Fixture parent, Action<TorRuntimeObservation> observe) : ITaskHandler
        {
            public CommandKind Kind => CommandKind.WindowsTorSession;

            public async Task<TaskExecutionResult> ExecuteAsync(TaskCommand command, TaskExecutionContext context,
                CancellationToken cancellationToken)
            {
                Assert(command is WindowsTorSessionCommand, "Application dispatched an untyped or wrong command");
                var call = Interlocked.Increment(ref parent.calls);
                Interlocked.Increment(ref parent.active);
                var attempt = new Attempt(observe, context, 10000 + call);
                using var registration = cancellationToken.Register(() => attempt.CancellationObserved.TrySetResult());
                parent.attempts.Writer.TryWrite(attempt);
                try
                {
                    var first = await Task.WhenAny(attempt.Unexpected.Task, attempt.CancellationObserved.Task);
                    if (first == attempt.Unexpected.Task) return await attempt.Unexpected.Task;
                    await attempt.Stopped.Task.WaitAsync(Limit);
                    return TaskExecutionResult.Success;
                }
                finally
                {
                    Interlocked.Decrement(ref parent.active);
                    observe(new(null, false, TerminationConfirmed: attempt.ConfirmTermination));
                }
            }
        }
    }

    private sealed class Attempt(Action<TorRuntimeObservation> observe, TaskExecutionContext context, int processId)
    {
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TaskExecutionResult> Unexpected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ConfirmTermination { get; set; } = true;
        public void Authenticate()
        {
            ReportBootstrap(0);
            context.Report(TaskPhase.Executing, 0, 100, "%");
        }
        public void ReportBootstrap(int progress) => observe(new(processId, true, BootstrapPercent: progress, BootstrapTag: "starting"));
        public void CompleteStop() => Stopped.TrySetResult();
    }
}
