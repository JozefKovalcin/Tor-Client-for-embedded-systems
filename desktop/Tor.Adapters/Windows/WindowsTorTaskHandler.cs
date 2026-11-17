using System.Runtime.Versioning;
using Tor.Core;

namespace Tor.Adapters.Windows;

/// <summary>Explicitly registered offline session task. Cancellation stops the owned Tor before returning its raw exit.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTorTaskHandler(PinnedWindowsRuntime runtime, string privateParent,
    Action<TorRuntimeObservation>? observe = null) : ITaskHandler
{
    public CommandKind Kind => CommandKind.WindowsTorSession;
    public async Task<TaskExecutionResult> ExecuteAsync(TaskCommand command, TaskExecutionContext context, CancellationToken token)
    {
        if (command is not WindowsTorSessionCommand tor || tor.Executable != PinnedWindowsRuntime.Executable ||
            !tor.RuntimeInputs.SequenceEqual(new[] { PinnedWindowsRuntime.ThreadLibrary }))
            return new(null, BlockedReason: "unreviewed-runtime");
        ManagedTorSession session;
        try { session = await ManagedTorSession.StartOfflineAsync(runtime, privateParent, token, observe).ConfigureAwait(false); }
        catch (TorProcessStartException startup) { return new(startup.RawExitCode, FailureReason: "tor-startup-failed"); }
        await using var ownedSession = session;
        var monitor = MonitorAsync(session, context, token);
        try
        {
            await Task.WhenAny(session.Completion, monitor, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
            var expected = token.IsCancellationRequested;
            if (monitor.IsCompleted && !expected && !session.Completion.IsCompleted)
                observe?.Invoke(new(session.ProcessId, false));
            var outcome = await session.StopAsync().ConfigureAwait(false);
            observe?.Invoke(new(null, false, TerminationConfirmed: true));
            return new(outcome.RawExitCode, FailureReason: expected ? null :
                monitor.IsFaulted ? "tor-observation-failed" : "unexpected-tor-exit");
        }
        finally
        {
            // Closing control completes the event stream before the monitor is awaited.
            await session.Control.DisposeAsync().ConfigureAwait(false);
            try { await monitor.ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // The observation fault prompted teardown. The result preserves the process exit above.
            }
        }
    }
    private async Task MonitorAsync(ManagedTorSession session, TaskExecutionContext context, CancellationToken token)
    {
        var measured = session.Control.LatestBootstrap;
        observe?.Invoke(new(session.ProcessId, true, BootstrapPercent: measured?.Percent, BootstrapTag: measured?.Tag));
        if (session.Control.LatestBootstrap is { } current) context.Report(TaskPhase.Executing, current.Percent, 100, "%");
        else context.Report(TaskPhase.Executing);
        await foreach (var progress in session.Control.WatchBootstrapAsync(token).ConfigureAwait(false))
        {
            observe?.Invoke(new(session.ProcessId, true, BootstrapPercent: progress.Percent, BootstrapTag: progress.Tag));
            context.Report(TaskPhase.Executing, progress.Percent, 100, "%");
        }
    }
}
