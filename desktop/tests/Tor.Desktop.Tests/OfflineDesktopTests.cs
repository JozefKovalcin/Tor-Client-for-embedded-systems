using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Tor.Application;
using Tor.Contracts;
using Tor.Core;
using TorManager.Desktop.Presentation;
using TorManager.Desktop.Services;

internal static class OfflineDesktopTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    public static async Task RunAsync(ITaskFeed demo, SettingsStore settings, Action<bool, string> check)
    {
        await SwitchModesAndCloseAsync(demo, settings, check);
        await ProjectSlowUpdatesAsync(check);
        Console.WriteLine("offline desktop integration assertions PASS");
    }

    private static async Task SwitchModesAndCloseAsync(ITaskFeed demo, SettingsStore settings, Action<bool, string> check)
    {
        var service = new FakeOfflineService();
        var shell = new ShellViewModel(demo, new ImmediateDispatcher(), settings, service);
        await shell.OpenAsync().WaitAsync(Limit);
        check(!shell.IsDemo && shell.Offline is not null && shell.Offline.PackageText == "Verified pinned runtime",
            "Windows DI defaults explicitly to Real Offline");
        var separated = true;
        for (var change = 0; change < 50; ++change)
        {
            shell.ShowDemo();
            separated &= shell.IsDemo && shell.VisibleTasks.All(task => task.TaskId.StartsWith("demo-", StringComparison.Ordinal));
            shell.ShowRealOffline();
            if (shell.IsDemo) throw new InvalidOperationException("Explicit Real Offline selection did not take effect");
        }
        check(separated, "Demo selector shows only simulation tasks");
        check(service.Refreshes == 1 && service.Watchers == 1 && service.Starts == 0,
            "rapid explicit mode switches keep one watcher and never start Tor");
        var model = shell.Offline!;
        model.StartCommand.Execute(null);
        model.StartCommand.Execute(null);
        await service.Started.Task.WaitAsync(Limit);
        shell.ShowDemo();
        check(service.Starts == 1 && !shell.IsDemo, "double-click coalesces and Demo cannot hide active startup");
        var closing = shell.DisposeAsync().AsTask();
        try
        {
            await service.DisposeStarted.Task.WaitAsync(Limit);
            await service.StartCancelled.Task.WaitAsync(Limit);
            check(!closing.IsCompleted && !model.StartCommand.CanExecute(null) && !model.StopCommand.CanExecute(null),
                "window close cancels startup and waits for service disposal");
        }
        finally { service.ReleaseDispose.TrySetResult(); }
        await closing.WaitAsync(Limit);
        await service.WatchEnded.Task.WaitAsync(Limit);
        check(service.Disposals == 1, "closing releases the sole service and live status watcher");
    }

    private static async Task ProjectSlowUpdatesAsync(Action<bool, string> check)
    {
        var service = new FakeOfflineService();
        var dispatcher = new QueuedDispatcher();
        var model = new OfflineTorViewModel(service, dispatcher);
        await model.OpenAsync().WaitAsync(Limit);
        var nativeExit = 0xE0000066U;
        var states = new[] { "queued", "running", "cancelling", "cancelled", "failed", "interrupted", "blocked", "succeeded" };
        var rows = states.Select((state, index) => new TaskProgress(1, "tor-" + state, null, index,
            DateTimeOffset.UtcNow, state == "interrupted" ? "recovery" : "test", state,
            null, null, null, "Task " + state, false, null, state == "failed" ? nativeExit : null,
            state == "cancelled" ? 15 : null, state == "interrupted" ? "app-crash" : null, null)).ToArray();
        var logs = Enumerable.Range(1, 250).Select(i => new TaskLog(DateTimeOffset.UtcNow, "tor-failed", i,
            TaskMessage.Failed, nativeExit, null)).ToArray();
        for (var i = 0; i < 100; ++i)
        {
            service.Publish(service.Snapshot with
            {
                SessionStatus = TorSessionStatus.RunningOffline, ControlStatus = TorControlStatus.Authenticated,
                ProcessId = 1234, BootstrapPercent = i, BootstrapTag = "starting", CanStart = false, CanStop = true
            });
        }
        service.Publish(service.Snapshot with
        {
            SessionStatus = TorSessionStatus.Failed, ControlStatus = TorControlStatus.Disconnected,
            ProcessId = null, BootstrapPercent = null, BootstrapTag = null, CurrentTask = rows[4],
            Tasks = rows, Logs = logs, LastError = "Offline test task failed", CanStart = true, CanStop = false
        });
        using var timeout = new CancellationTokenSource(Limit);
        await dispatcher.DrainAsync(service.FeedConsumed.Task, timeout.Token);
        check(dispatcher.MaximumPending == 1 && model.SessionText == "Failed" &&
            model.ControlText == "Disconnected" && model.BootstrapText == "No bootstrap data received",
            "slow UI applies one snapshot at a time and unknown progress stays unknown");
        check(model.Tasks.Select(task => task.State).ToHashSet().SetEquals(states) &&
            model.Tasks.Single(task => task.State == "cancelled").TerminationSignal == 15 &&
            model.CurrentTask?.RawExitCode == nativeExit && model.Logs.Count == 100 && model.HasError,
            "live projection keeps all W5 states, raw result metadata, sanitized bounded logs and errors");
        service.ReleaseDispose.TrySetResult();
        await model.DisposeAsync().AsTask().WaitAsync(Limit);
    }

    private sealed class FakeOfflineService : IOfflineTorService
    {
        private readonly Channel<OfflineTorSnapshot> inbox = Channel.CreateUnbounded<OfflineTorSnapshot>();
        private OfflineTorSnapshot snapshot = new(RuntimePackageStatus.Available, TorSessionStatus.Stopped,
            TorControlStatus.Disconnected, null, null, null, null, null, [], [], true, false);

        public OfflineTorSnapshot Snapshot => Volatile.Read(ref snapshot);
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource StartCancelled { get; } = Signal();
        public TaskCompletionSource DisposeStarted { get; } = Signal();
        public TaskCompletionSource ReleaseDispose { get; } = Signal();
        public TaskCompletionSource WatchEnded { get; } = Signal();
        public TaskCompletionSource FeedConsumed { get; } = Signal();
        public int Starts { get; private set; }
        public int Watchers { get; private set; }
        public int Refreshes { get; private set; }
        public int Disposals { get; private set; }

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Publish(OfflineTorSnapshot value)
        {
            Volatile.Write(ref snapshot, value);
            inbox.Writer.TryWrite(value);
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            Refreshes++;
            return Task.CompletedTask;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            Starts++;
            Publish(Snapshot with { SessionStatus = TorSessionStatus.Starting, CanStart = false, CanStop = true });
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { if (cancellationToken.IsCancellationRequested) StartCancelled.TrySetResult(); }
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async IAsyncEnumerable<OfflineTorSnapshot> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Watchers++;
            try
            {
                yield return Snapshot;
                await foreach (var value in inbox.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return value;
                    if (value.SessionStatus == TorSessionStatus.Failed) FeedConsumed.TrySetResult();
                }
            }
            finally { WatchEnded.TrySetResult(); }
        }

        public async ValueTask DisposeAsync()
        {
            Disposals++;
            DisposeStarted.TrySetResult();
            await ReleaseDispose.Task;
            inbox.Writer.TryComplete();
        }
    }
}
