using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Tor.Adapters.Windows;
using Tor.Application;
using Tor.Core;

return await Qualification.RunAsync(args);

internal static class Qualification
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is ["--offline-policy"]) return await VerifyOfflinePolicyAsync();
        var online = args is ["--public-tor", "--approve-public-tor", _, _];
        var local = args is ["--native-collision", _, _];
        if (!online && !local)
        {
            Console.Error.WriteLine("NOT RUN: public Tor qualification requires --public-tor --approve-public-tor <verified-runtime> <new-private-path>.");
            return 2;
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("BLOCKED: this qualification requires Windows.");
            return 2;
        }

        try
        {
            var package = Path.GetFullPath(args[^2]);
            var state = Path.GetFullPath(args[^1]);
            Require(!Directory.Exists(state) && !File.Exists(state), "Choose a new private state path.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await VerifyPortConflictAsync(package, state + "-collision", deadline.Token);
            if (online) await VerifyPublicTorAsync(package, state, deadline.Token);
            else
            {
                await VerifyNativeOfflineStatusAsync(package, state, deadline.Token);
                Console.WriteLine("NOT RUN: public Tor bootstrap and destination check.");
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("FAIL: the qualification deadline expired.");
            return 1;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or SocketException)
        {
            // Native errors can contain paths and other private data.
            Console.Error.WriteLine(error is TestFailure ? "FAIL: " + error.Message : "FAIL: local qualification could not complete.");
            return 1;
        }
    }

    private static async Task<int> VerifyOfflinePolicyAsync()
    {
        var lines = WindowsOnlineTorSettings.Configuration(IOnlineTorService.DefaultSocksPort)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Require(lines.SequenceEqual(new[] {
            "SocksPort 127.0.0.1:9150 IsolateSOCKSAuth ExtendedErrors", "SocksPolicy accept 127.0.0.1/32",
            "SocksPolicy reject *", "SafeSocks 1", "ExitRelay 0", "ExitPolicy reject *:*", "DisableNetwork 1"
        }), "The configured SOCKS and network policy changed.");
        VerifyInvalidPort(0);
        VerifyInvalidPort(65536);
        Console.WriteLine("PASS: online configuration confines SOCKS to IPv4 loopback and starts with networking disabled.");
        if (OperatingSystem.IsWindows()) await VerifyLoopbackOwnershipAsync();
        else Console.WriteLine("SKIPPED: Windows TCP ownership API requires Windows.");
        return 0;
    }

    private static void VerifyInvalidPort(int port)
    {
        try { _ = WindowsOnlineTorSettings.Configuration(port); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new TestFailure("An invalid SOCKS port was accepted.");
    }

    [SupportedOSPlatform("windows")]
    private static async Task VerifyLoopbackOwnershipAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        listener.Start();
        var server = (IPEndPoint)listener.LocalEndpoint;
        var pid = Environment.ProcessId;
        Require(WindowsTcpOwner.OwnsListener(pid, server.Port), "The real IPv4 loopback listener was not attributed to its owner.");
        Require(!WindowsTcpOwner.OwnsListener(pid + 1, server.Port), "The loopback listener was attributed to another PID.");
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(server, deadline.Token);
        using var accepted = await listener.AcceptSocketAsync(deadline.Token);
        var peer = (IPEndPoint)client.LocalEndPoint!;
        Require(WindowsTcpOwner.OwnsPeer(pid, server.Port, peer), "The established loopback socket was not attributed to its owner.");
        Require(WindowsTcpOwner.OwnsPeer(pid, peer.Port, server), "The Tor-side outgoing tuple must also be attributed to its owner.");
        Require(!WindowsTcpOwner.OwnsPeer(pid + 1, server.Port, peer), "The established socket was attributed to another PID.");
        Require(!WindowsTcpOwner.OwnsPeer(pid, server.Port, new IPEndPoint(IPAddress.Any, peer.Port)),
            "An endpoint outside IPv4 loopback passed the ownership check.");
        Require(!WindowsTcpOwner.OwnsPeer(pid, server.Port, new IPEndPoint(peer.Address, peer.Port == 1 ? 2 : 1)),
            "The ownership check ignored the connected client's port.");
        listener.Stop();
        Require(!WindowsTcpOwner.OwnsListener(pid, server.Port), "The stopped listener was still attributed to the application.");
        Console.WriteLine("PASS: real Windows TCP listener and peer ownership match the exact PID and endpoint.");
    }

    [SupportedOSPlatform("windows")]
    private static async Task VerifyPortConflictAsync(string package, string state, CancellationToken token)
    {
        Require(!Directory.Exists(state) && !File.Exists(state), "Choose an unused port-collision evidence path.");
        var occupied = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        occupied.Start();
        try
        {
            var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
            await using IOnlineTorService service = OfflineTorService.CreateWindows(package, state);
            await service.RefreshAsync(token);
            Require(service.Snapshot.PackageStatus == RuntimePackageStatus.Available, "The pinned Windows runtime is unavailable.");
            Require((await service.CheckConnectivityAsync(token)).Status == OnlineActionStatus.Unavailable,
                "Connectivity probing must be unavailable without an authenticated online session.");
            Require((await service.RequestNewIdentityAsync(token)).Status == OnlineActionStatus.Unavailable,
                "NEWNYM must be unavailable without an authenticated online session.");
            using var observation = CancellationTokenSource.CreateLinkedTokenSource(token);
            observation.CancelAfter(TimeSpan.FromSeconds(20));
            await service.StartOnlineAsync(port, observation.Token);
            var refused = await UntilAsync(service, s => !s.CanStop && s.LastError is not null, observation.Token);
            Require(refused.ProcessId is null && refused.ControlStatus == TorControlStatus.Disconnected &&
                refused.BootstrapPercent is null && refused.Online?.SocksPort is null,
                "Port collision must not leave Tor running or report a usable proxy.");
            Require(refused.CurrentTask?.ReasonCode == "socks-port-in-use", "The occupied port must have its own reason code.");
            Require(!Directory.Exists(state) || !Directory.EnumerateDirectories(state, "session-*").Any(),
                "Port conflict was not detected before staging a Tor session.");
            Console.WriteLine("PASS: local port conflict refused before staging or starting Tor; no online action accepted.");
        }
        finally { occupied.Stop(); }
    }

    [SupportedOSPlatform("windows")]
    private static async Task VerifyNativeOfflineStatusAsync(string package, string state, CancellationToken token)
    {
        PrivateStorage.CreateNewDirectory(state);
        await using var session = await ManagedTorSession.StartOfflineAsync(new PinnedWindowsRuntime(package), state, token);
        await session.Control.SubscribeOnlineAsync(token);
        var observed = await session.Control.GetOnlineStatusAsync(token);
        Require(observed.TorVersion == "0.4.9.14" && observed.SocksListeners.Count == 0 && observed.CircuitEstablished == false,
            "Typed status from the pinned offline Tor process is invalid.");
        Require(session.Control.LatestBootstrap?.Percent == 0, "Offline status unexpectedly showed bootstrap progress.");
        var result = await session.StopAsync();
        Require(result.RawExitCode == 0 && !IsTorRunning(session.ProcessId), "The offline status process did not stop cleanly.");
        Console.WriteLine("PASS: actual pinned Tor answered typed GETINFO and SETEVENTS over SAFECOOKIE with no SOCKS listener or circuit.");
    }

    [SupportedOSPlatform("windows")]
    private static async Task VerifyPublicTorAsync(string package, string state, CancellationToken token)
    {
        int? ownedPid = null;
        try
        {
            await using IOnlineTorService service = OfflineTorService.CreateWindows(package, state);
            await service.StartOnlineAsync(IOnlineTorService.DefaultSocksPort, token);
            var authenticated = await UntilAsync(service, s => s.ControlStatus == TorControlStatus.Authenticated ||
                s.SessionStatus == TorSessionStatus.Failed, token);
            Require(authenticated.ProcessId is > 0 && authenticated.ControlStatus == TorControlStatus.Authenticated,
                "Tor did not start and authenticate with SAFECOOKIE.");
            ownedPid = authenticated.ProcessId;
            Console.WriteLine("PASS: verified Windows Tor started and authenticated with SAFECOOKIE.");

            var ready = await UntilAsync(service, s => s.SessionStatus is TorSessionStatus.RunningOnline or TorSessionStatus.Failed, token);
            Require(ready.BootstrapPercent == 100 && ready.SessionStatus == TorSessionStatus.RunningOnline,
                "Tor did not report successful bootstrap.");
            Console.WriteLine("PASS: authenticated Tor control reported 100% bootstrap.");
            Require(ready.Online?.SocksPort == IOnlineTorService.DefaultSocksPort, "The authenticated owned loopback SOCKS port was not confirmed.");
            Console.WriteLine("PASS: the expected loopback SOCKS5 endpoint was verified.");
            Require((await service.CheckConnectivityAsync(token)).Status == OnlineActionStatus.Succeeded,
                "The proxy-only destination check failed.");
            Console.WriteLine("PASS: explicit proxy-only destination check succeeded.");

            await service.StopAsync(token);
            Require(service.Snapshot.ProcessId is null && service.Snapshot.CurrentTask?.RawExitCode == 0,
                "Graceful Stop did not confirm exit code 0.");
            Require(!IsTorRunning(ownedPid), "The first owned Tor process survived Stop.");
            Console.WriteLine("PASS: authenticated shutdown returned exit code 0 and left no owned process.");

            await service.StartOnlineAsync(IOnlineTorService.DefaultSocksPort, token);
            var again = await UntilAsync(service, s => s.ControlStatus == TorControlStatus.Authenticated ||
                s.SessionStatus == TorSessionStatus.Failed, token);
            Require(again.ProcessId is > 0 && again.ControlStatus == TorControlStatus.Authenticated,
                "The second explicit online session did not authenticate.");
            ownedPid = again.ProcessId;
        }
        finally
        {
            Require(!IsTorRunning(ownedPid), "The owned Tor process survived application service shutdown.");
        }
        Console.WriteLine("PASS: repeated explicit online start and disposal left no owned process.");
    }

    private static async Task<OfflineTorSnapshot> UntilAsync(IOnlineTorService service,
        Func<OfflineTorSnapshot, bool> completed, CancellationToken token)
    {
        await foreach (var snapshot in service.WatchAsync(token))
            if (completed(snapshot)) return snapshot;
        throw new TestFailure("Application snapshot stream closed before the required observation.");
    }

    private static bool IsTorRunning(int? pid)
    {
        if (pid is null) return false;
        try
        {
            using var process = Process.GetProcessById(pid.Value);
            return !process.HasExited && string.Equals(process.ProcessName, "tor", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new TestFailure(message);
    }

    private sealed class TestFailure(string message) : InvalidOperationException(message);
}
