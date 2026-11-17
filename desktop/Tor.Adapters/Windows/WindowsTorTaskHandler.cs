using System.Runtime.Versioning;
using Tor.Core;

namespace Tor.Adapters.Windows;

/// <summary>Explicitly registered offline session task. Cancellation stops the owned Tor before returning its raw exit.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTorTaskHandler(PinnedWindowsRuntime runtime, string privateParent) : ITaskHandler
{
    public CommandKind Kind => CommandKind.WindowsTorSession;
    public async Task<TaskExecutionResult> ExecuteAsync(TaskCommand command, TaskExecutionContext context, CancellationToken token)
    {
        if (command is not WindowsTorSessionCommand tor || tor.Executable != PinnedWindowsRuntime.Executable ||
            !tor.RuntimeInputs.SequenceEqual(new[] { PinnedWindowsRuntime.ThreadLibrary }))
            return new(null, BlockedReason: "unreviewed-runtime");
        ManagedTorSession session;
        try { session = await ManagedTorSession.StartOfflineAsync(runtime, privateParent, token).ConfigureAwait(false); }
        catch (TorProcessStartException startup) { return new(startup.RawExitCode, FailureReason: "tor-startup-failed"); }
        await using var ownedSession = session;
        var monitor = MonitorAsync(session, context, token);
        try
        {
            await Task.WhenAny(session.Completion, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
            var expected = token.IsCancellationRequested;
            var outcome = await session.StopAsync().ConfigureAwait(false);
            return new(outcome.RawExitCode, FailureReason: expected ? null : "unexpected-tor-exit");
        }
        finally
        {
            // Closing control completes the event stream before the monitor is awaited.
            await session.Control.DisposeAsync().ConfigureAwait(false);
            try { await monitor.ConfigureAwait(false); } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }
    private static async Task MonitorAsync(ManagedTorSession session, TaskExecutionContext context, CancellationToken token)
    {
        if (session.Control.LatestBootstrap is { } current) context.Report(TaskPhase.Executing, current.Percent, 100, "%");
        await foreach (var progress in session.Control.WatchBootstrapAsync(token).ConfigureAwait(false))
            context.Report(TaskPhase.Executing, progress.Percent, 100, "%");
    }
}
