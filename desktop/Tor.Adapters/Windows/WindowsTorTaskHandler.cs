using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Tor.Control;
using Tor.Core;

namespace Tor.Adapters.Windows;

public interface IWindowsOnlineTorActions
{
    Task<NewIdentityResult?> RequestNewIdentityAsync(CancellationToken token);
    Task<Socks5ConnectivityResult?> CheckConnectivityAsync(CancellationToken token);
    IOnionPeerTransport? GetPeerTransport();
}

/// <summary>Serial typed session runner. Cancellation stops the owned Tor before returning its raw exit.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTorTaskHandler(PinnedWindowsRuntime runtime, string privateParent,
    Action<TorRuntimeObservation>? observe = null) : ITaskHandler, IWindowsOnlineTorActions
{
    private static readonly TimeSpan BootstrapDeadline = TimeSpan.FromMinutes(3);
    private ActiveOnline? online;

    public CommandKind Kind => CommandKind.WindowsTorSession;
    public async Task<TaskExecutionResult> ExecuteAsync(TaskCommand command, TaskExecutionContext context, CancellationToken token)
    {
        if (command is not WindowsTorSessionCommand tor || tor.Executable != runtime.ExecutablePin ||
            !tor.RuntimeInputs.SequenceEqual(runtime.RuntimePins))
            return new(null, BlockedReason: "unreviewed-runtime");
        if (tor.OnlineSocksPort is < 1 or > 65535) return new(null, BlockedReason: "invalid-socks-port");
        ManagedTorSession session;
        try
        {
            session = tor.OnlineSocksPort is int port
                ? await ManagedTorSession.StartOnlineAsync(runtime, privateParent, port, token, observe).ConfigureAwait(false)
                : await ManagedTorSession.StartOfflineAsync(runtime, privateParent, token, observe).ConfigureAwait(false);
        }
        catch (TorProcessStartException startup)
        {
            return new(startup.RawExitCode, FailureReason: "tor-startup-failed", WindowsFailure: startup.WindowsFailure);
        }
        await using var ownedSession = session;
        using var onlineLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var onlineSession = tor.OnlineSocksPort is int socksPort ? new ActiveOnline(session, socksPort, onlineLifetime.Token) : null;
        var monitor = onlineSession is null ? MonitorAsync(session, context, token) : MonitorOnlineAsync(onlineSession, context);
        try
        {
            await Task.WhenAny(session.Completion, monitor, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
            var expected = token.IsCancellationRequested;
            var onlineFailure = monitor.Exception?.InnerException as OnlineSessionFailure;
            if (monitor.IsCompleted && !expected && !session.Completion.IsCompleted)
                observe?.Invoke(new(session.ProcessId, false));
            Interlocked.Exchange(ref online, null);
            await onlineLifetime.CancelAsync().ConfigureAwait(false);
            if (onlineSession is not null)
            {
                // Before authenticated SHUTDOWN, finish any already-issued bounded command.
                // Cancelling it mid-reply would intentionally close the shared control transport.
                await JoinMonitorAsync(monitor, onlineLifetime.Token).ConfigureAwait(false);
                await onlineSession.IdentityGate.WaitAsync().ConfigureAwait(false);
                onlineSession.IdentityGate.Release();
            }
            var outcome = await session.StopAsync().ConfigureAwait(false);
            observe?.Invoke(new(null, false, TerminationConfirmed: true));
            var failure = monitor.IsFaulted ? "tor-observation-failed" : "unexpected-tor-exit";
            var windowsFailure = onlineFailure?.Reason ?? (monitor.IsFaulted ? (WindowsTorFailure?)null : WindowsTorFailure.UnexpectedExit);
            return new(outcome.RawExitCode, FailureReason: expected ? null :
                failure, WindowsFailure: expected || onlineSession is null ? null : windowsFailure);
        }
        finally
        {
            // Closing control completes the event stream before the monitor is awaited.
            await session.Control.DisposeAsync().ConfigureAwait(false);
            await JoinMonitorAsync(monitor, onlineLifetime.Token).ConfigureAwait(false);
        }
    }

    private static async Task JoinMonitorAsync(Task monitor, CancellationToken stopped)
    {
        try { await monitor.ConfigureAwait(false); }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The observation fault prompted teardown. The caller reports its reason and the process exit.
        }
    }

    public async Task<NewIdentityResult?> RequestNewIdentityAsync(CancellationToken token)
    {
        var current = Volatile.Read(ref online);
        if (current is null || !current.Ready || current.Session.Completion.IsCompleted) return null;
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(token, current.Token);
        if (combined.IsCancellationRequested || !await current.IdentityGate.WaitAsync(0, combined.Token).ConfigureAwait(false))
            return null;
        try
        {
            if (combined.IsCancellationRequested) return null;
            return await current.Session.Control.RequestNewIdentityAsync(combined.Token).ConfigureAwait(false);
        }
        finally { current.IdentityGate.Release(); }
    }

    public async Task<Socks5ConnectivityResult?> CheckConnectivityAsync(CancellationToken token)
    {
        var current = Volatile.Read(ref online);
        if (current is null || !current.Ready || current.Session.Completion.IsCompleted ||
            Interlocked.Exchange(ref current.ProbeRunning, 1) != 0) return null;
        try
        {
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(token, current.Token);
            if (combined.IsCancellationRequested) return null;
            var pid = current.Session.ProcessId;
            return await Socks5ConnectivityProbe.CheckAsync(new(IPAddress.Loopback, current.SocksPort), combined.Token,
                client => !current.Session.Completion.IsCompleted && !combined.IsCancellationRequested &&
                          WindowsTcpOwner.OwnsPeer(pid, current.SocksPort, client)).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref current.ProbeRunning, 0); }
    }

    public IOnionPeerTransport? GetPeerTransport()
    {
        var current = Volatile.Read(ref online);
        return current is not null && current.Ready && !current.Token.IsCancellationRequested &&
               !current.Session.Completion.IsCompleted ? new PeerTransport(current) : null;
    }

    private sealed class PeerTransport(ActiveOnline current) : IPersistentOnionPeerTransport
    {
        public CancellationToken Lifetime => current.Token;

        public Task<OnionAddress> HostAsync(int loopbackPort, CancellationToken token) =>
            RegisterAsync(ct => current.Session.Control.AddOnionAsync(loopbackPort, ct), token);

        public Task<(OnionAddress Address, OnionServiceKey Key)> GenerateHostAsync(int loopbackPort, CancellationToken token) =>
            RegisterAsync(ct => current.Session.Control.GenerateOnionAsync(loopbackPort, ct), token);

        public Task<OnionAddress> RestoreHostAsync(int loopbackPort, OnionAddress address, OnionServiceKey key, CancellationToken token) =>
            RegisterAsync(ct => current.Session.Control.RestoreOnionAsync(loopbackPort, address, key, ct), token);

        private async Task<T> RegisterAsync<T>(Func<CancellationToken, Task<T>> register, CancellationToken token)
        {
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(token, Lifetime);
            await current.IdentityGate.WaitAsync(combined.Token).ConfigureAwait(false);
            try
            {
                await current.Session.Control.SubscribeOnionPublicationAsync().ConfigureAwait(false);
                return await register(combined.Token).ConfigureAwait(false);
            }
            finally { current.IdentityGate.Release(); }
        }

        public async Task RemoveAsync(OnionAddress address)
        {
            if (Lifetime.IsCancellationRequested) return;
            await current.IdentityGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Lifetime.IsCancellationRequested) await current.Session.Control.DeleteOnionAsync(address).ConfigureAwait(false);
            }
            finally { current.IdentityGate.Release(); }
        }

        public Task WaitForPublicationAsync(OnionAddress address, CancellationToken token) =>
            current.Session.Control.WaitForOnionPublicationAsync(address, token);

        public async Task<Stream> ConnectAsync(OnionAddress address, CancellationToken token)
        {
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(token, Lifetime);
            var pid = current.Session.ProcessId;
            return await OnionSocksConnection.ConnectAsync(new(IPAddress.Loopback, current.SocksPort), address,
                peer => !combined.IsCancellationRequested && current.Ready && !current.Session.Completion.IsCompleted &&
                        WindowsTcpOwner.OwnsPeer(pid, current.SocksPort, peer), combined.Token).ConfigureAwait(false);
        }
    }

    private async Task MonitorOnlineAsync(ActiveOnline current, TaskExecutionContext context)
    {
        var control = current.Session.Control;
        var pid = current.Session.ProcessId;
        var clock = Stopwatch.StartNew();
        int? lastPercent = null;
        TorRuntimeObservation? previous = null;
        Interlocked.Exchange(ref online, current);
        try
        {
            while (true)
            {
                current.Token.ThrowIfCancellationRequested();
                var status = await control.GetOnlineStatusAsync().ConfigureAwait(false);
                current.Token.ThrowIfCancellationRequested();
                var listener = new IPEndPoint(IPAddress.Loopback, current.SocksPort);
                if (status.SocksListeners.Count != 1 || !status.SocksListeners[0].Equals(listener) ||
                    !WindowsTcpOwner.OwnsListener(pid, current.SocksPort))
                    throw new OnlineSessionFailure(WindowsTorFailure.UnverifiedSocksListener);

                var bootstrap = control.LatestBootstrap;
                bool wasReady = current.Ready;
                current.Ready = bootstrap?.Percent == 100 && status.CircuitEstablished == true;
                var warning = WarningText(bootstrap?.Warning) ?? (bootstrap?.Percent == 100 && status.CircuitEstablished == false
                    ? "No usable Tor circuit is currently established." : null);
                var measurement = new TorRuntimeObservation(pid, true, BootstrapPercent: bootstrap?.Percent, BootstrapTag: bootstrap?.Tag,
                    Online: new(current.SocksPort, status.TorVersion, status.CircuitEstablished, warning));
                if (measurement != previous) observe?.Invoke(measurement);
                if (previous is null || lastPercent != bootstrap?.Percent)
                {
                    if (bootstrap is not null) context.Report(TaskPhase.Executing, bootstrap.Percent, 100, "%");
                    else context.Report(TaskPhase.Executing);
                    lastPercent = bootstrap?.Percent;
                }
                previous = measurement;
                if (current.Ready) clock.Stop();
                else if (wasReady) clock.Restart();
                if (clock.Elapsed >= BootstrapDeadline) throw new OnlineSessionFailure(WindowsTorFailure.BootstrapTimeout);
                await Task.Delay(current.Ready ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(1), current.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException and not OnlineSessionFailure or SocketException or InvalidOperationException ||
            error is OperationCanceledException && !current.Token.IsCancellationRequested)
        {
            throw new OnlineSessionFailure(WindowsTorFailure.ControlLost);
        }
        finally { current.Ready = false; }
    }

    private static string? WarningText(BootstrapWarning? warning) => warning switch
    {
        BootstrapWarning.ConnectionRefused => "Tor reported a refused bootstrap connection and can retry.",
        BootstrapWarning.ConnectionTimeout => "Tor reported a bootstrap connection timeout and can retry.",
        BootstrapWarning.NoRoute => "Tor reported no route for a bootstrap connection.",
        BootstrapWarning.IoError => "Tor reported an I/O error during bootstrap.",
        BootstrapWarning.Other => "Tor reported a bootstrap warning.",
        _ => null
    };

    private sealed class OnlineSessionFailure(WindowsTorFailure reason) : IOException("Online session monitor stopped")
    {
        public WindowsTorFailure Reason { get; } = reason;
    }

    private sealed class ActiveOnline(ManagedTorSession session, int socksPort, CancellationToken token)
    {
        public ManagedTorSession Session { get; } = session;
        public int SocksPort { get; } = socksPort;
        public CancellationToken Token { get; } = token;
        public SemaphoreSlim IdentityGate { get; } = new(1, 1);
        public volatile bool Ready;
        public int ProbeRunning;
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
