using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Tor.Control;

static partial class Program
{
    static async Task OnlineProtocolTests()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("online-control-requires-authentication", OnlineControlRequiresAuthentication),
            ("typed-status-and-sanitized-bootstrap-warnings", OnlineStatus),
            ("optional-circuit-status-keeps-control-connection", OptionalCircuitStatus),
            ("nonloopback-socks-listener-cannot-pass", UnsafeListener),
            ("newnym-distinguishes-event-ack-and-rate-limit", NewIdentity),
            ("cancelled-newnym-drains-reply-and-allows-shutdown", NewIdentityCancellation),
            ("onion-key-restores-the-same-address-on-a-new-control-session", OnionKeyRestore),
            ("invalid-onion-registration-is-cleaned-up-and-sanitized", InvalidOnionRegistration),
            ("socks5-domain-resolution-happens-through-proxy", SocksDestinationRejected),
            ("socks5-tls-starts-inside-established-tunnel", SocksTlsInTunnel),
            ("socks5-wrong-owner-rejected-before-protocol-bytes", SocksWrongOwner),
            ("socks5-no-auth-policy-and-cancellation", SocksAuthenticationAndCancellation)
        };
        foreach (var (name, run) in tests)
        {
            await run();
            Console.WriteLine("PASS " + name);
        }
        Console.WriteLine($"{tests.Length} online protocol groups PASS (loopback only)");
    }

    static async Task OnlineControlRequiresAuthentication()
    {
        await using var server = new MockServer();
        await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await Assert.Throws<TorControlException>(() => client.EnableNetworkAsync());
        await Assert.Throws<TorControlException>(() => client.SubscribeOnlineAsync());
        await Assert.Throws<TorControlException>(() => client.GetOnlineStatusAsync());
        await Assert.Throws<TorControlException>(() => client.RequestNewIdentityAsync());
        await Assert.Throws<TorControlException>(() => client.GenerateOnionAsync(8181));
        using var key = new OnionServiceKey(new byte[OnionServiceKey.ByteLength]);
        await Assert.Throws<TorControlException>(() =>
            client.RestoreOnionAsync(8181, OnionAddress.Parse(MockServer.FixtureServiceId + ".onion"), key));
        Assert.That(server.Commands.Count == 0, "Unauthenticated online command reached the server");
    }

    static async Task OnionKeyRestore()
    {
        OnionAddress address;
        OnionServiceKey key;
        await using (var firstTor = new MockServer())
        await using (var first = await TorControlClient.ConnectAsync(IPAddress.Loopback, firstTor.Port))
        {
            await first.AuthenticateAsync(firstTor.Cookie);
            (address, key) = await first.GenerateOnionAsync(8181);
            await first.DeleteOnionAsync(address);
        }
        using (key)
        await using (var secondTor = new MockServer())
        await using (var second = await TorControlClient.ConnectAsync(IPAddress.Loopback, secondTor.Port))
        {
            await second.AuthenticateAsync(secondTor.Cookie);
            Assert.That(await second.RestoreOnionAsync(8181, address, key) == address, "The restored onion address changed");
            await second.DeleteOnionAsync(address);
            Assert.That(await second.AddOnionAsync(8181) == address, "Transient hosting no longer works");
            await second.DeleteOnionAsync(address);
        }
    }

    static async Task InvalidOnionRegistration()
    {
        foreach (var mode in new[] { "bad-onion-key", "wrong-onion-id" })
        {
            await using var server = new MockServer(mode);
            await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
            await client.AuthenticateAsync(server.Cookie);
            try
            {
                if (mode == "bad-onion-key") await client.GenerateOnionAsync(8181);
                else
                {
                    using var key = new OnionServiceKey(Convert.FromBase64String(MockServer.FixtureOnionKey));
                    await client.RestoreOnionAsync(8181, OnionAddress.Parse(MockServer.FixtureServiceId + ".onion"), key);
                }
                throw new Exception("Invalid onion registration was accepted");
            }
            catch (TorControlException error)
            {
                Assert.That(!error.ToString().Contains("redaction-test-secret", StringComparison.Ordinal) &&
                    !error.ToString().Contains(MockServer.FixtureOnionKey, StringComparison.Ordinal)
                    && server.Commands.Contains("DEL_ONION"), "Invalid registration leaked a secret or left a service behind");
            }
        }
    }

    static async Task OnlineStatus()
    {
        await using var server = new MockServer();
        await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await client.AuthenticateAsync(server.Cookie);
        await client.SubscribeOnlineAsync();
        await client.EnableNetworkAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var bootstrap = client.WatchBootstrapAsync(timeout.Token).GetAsyncEnumerator();
        Assert.That(await bootstrap.MoveNextAsync() && bootstrap.Current == new BootstrapProgress(17, "conn", BootstrapWarning.ConnectionTimeout),
            "Control accepted quoted text as status or did not sanitize the WAN warning");
        var status = await client.GetOnlineStatusAsync();
        Assert.That(status.TorVersion == "0.4.9.14" && status.CircuitEstablished is true && status.SocksListeners.SequenceEqual(
            new[] { new IPEndPoint(IPAddress.Loopback, 9150), new IPEndPoint(IPAddress.IPv6Loopback, 9151) }), "Typed online status fields differ from the observed reply");
    }

    static async Task OptionalCircuitStatus()
    {
        await using var server = new MockServer("no-circuit-key");
        await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await client.AuthenticateAsync(server.Cookie);
        Assert.That((await client.GetOnlineStatusAsync()).CircuitEstablished is null, "Unsupported circuit status was fabricated");
        Assert.That(await client.GetBootstrapAsync() == new BootstrapProgress(42, "loading"), "Optional query rejection closed authenticated control");
    }

    static async Task UnsafeListener()
    {
        await using var server = new MockServer("foreign-listener");
        await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await client.AuthenticateAsync(server.Cookie);
        await Assert.Throws<TorControlException>(() => client.GetOnlineStatusAsync());
        await Assert.Throws<ArgumentException>(() => Socks5ConnectivityProbe.CheckAsync(new(IPAddress.Any, 9150)));
    }

    static async Task NewIdentity()
    {
        foreach (var (mode, expected) in new[] { ("identity-observed", NewIdentityResult.Applied), ("good", NewIdentityResult.Submitted) })
        {
            await using var server = new MockServer(mode);
            await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
            await client.AuthenticateAsync(server.Cookie);
            await client.SubscribeOnlineAsync();
            var results = await Task.WhenAll(client.RequestNewIdentityAsync(), client.RequestNewIdentityAsync());
            Assert.That(results.SequenceEqual(new[] { expected, NewIdentityResult.RateLimited }) && server.Commands.Count(v => v == "SIGNAL") == 1,
                "NEWNYM acknowledgement, event or local rate limiting was misclassified");
        }
    }

    static async Task NewIdentityCancellation()
    {
        await using var server = new MockServer("identity-cancel");
        await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await client.AuthenticateAsync(server.Cookie);
        await client.SubscribeOnlineAsync();
        using var cancellation = new CancellationTokenSource();
        var request = client.RequestNewIdentityAsync(cancellation.Token);
        await server.IdentityReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        var shutdown = client.ShutdownAsync();
        server.ReleaseIdentityReply.TrySetResult();
        await Assert.Throws<OperationCanceledException>(() => request);
        await shutdown;
        Assert.That(server.Commands.Count(v => v == "SIGNAL") == 2, "Cancelling NEWNYM prevented authenticated shutdown");
    }

    static async Task SocksDestinationRejected()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var verifiedPeer = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = Socks5ConnectivityProbe.CheckAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token, peer => verifiedPeer.TrySetResult(peer));
        using var proxy = await listener.AcceptTcpClientAsync(timeout.Token);
        Assert.That(Equals(proxy.Client.RemoteEndPoint, await verifiedPeer.Task.WaitAsync(timeout.Token)), "Ownership callback did not receive the connected client endpoint");
        using var stream = proxy.GetStream();
        await AssertDomainTunnelRequest(stream, timeout.Token);
        await stream.WriteAsync(new byte[] { 5, 4, 0, 1, 0, 0, 0, 0, 0, 0 }, timeout.Token);
        Assert.That(await check == Socks5ConnectivityResult.DestinationUnavailable && !listener.Pending(),
            "A correctly negotiated proxy with a rejected destination was misclassified or retried");
    }

    static async Task SocksTlsInTunnel()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var check = Socks5ConnectivityProbe.CheckAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        using (var proxy = await listener.AcceptTcpClientAsync(timeout.Token))
        {
            using var stream = proxy.GetStream();
            await AssertDomainTunnelRequest(stream, timeout.Token);
            await stream.WriteAsync(new byte[] { 5, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0 }, timeout.Token);
            var tls = new byte[5];
            await stream.ReadExactlyAsync(tls, timeout.Token);
            Assert.That(tls[0] == 22 && tls[1] == 3 && BinaryPrimitives.ReadUInt16BigEndian(tls.AsSpan(3)) > 0,
                "TLS handshake did not travel inside the negotiated SOCKS tunnel");
        }
        Assert.That(await check == Socks5ConnectivityResult.DestinationUnavailable && !listener.Pending(),
            "Early EOF after SOCKS CONNECT was reported as destination reachability or retried");
    }

    static async Task SocksWrongOwner()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var check = Socks5ConnectivityProbe.CheckAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token, _ => false);
        using var proxy = await listener.AcceptTcpClientAsync(timeout.Token);
        using var stream = proxy.GetStream();
        Assert.That(await stream.ReadAsync(new byte[1], timeout.Token) == 0 && await check == Socks5ConnectivityResult.ProxyUnavailable,
            "An unverified listener received SOCKS data or was reported ready");
    }

    static async Task SocksAuthenticationAndCancellation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var check = Socks5ConnectivityProbe.CheckAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        using (var proxy = await listener.AcceptTcpClientAsync(timeout.Token))
        {
            using var stream = proxy.GetStream();
            var greeting = new byte[3];
            await stream.ReadExactlyAsync(greeting, timeout.Token);
            Assert.That(greeting.SequenceEqual(new byte[] { 5, 1, 0 }), "The client offered unexpected authentication mechanisms");
            await stream.WriteAsync(new byte[] { 5, 2 }, timeout.Token);
            Assert.That(await check == Socks5ConnectivityResult.ProxyUnavailable, "Unexpected SOCKS authentication did not block the probe");
            Assert.That(await stream.ReadAsync(new byte[1], timeout.Token) == 0, "The client transmitted unexpected credentials");
        }
        using var cancellation = new CancellationTokenSource();
        check = Socks5ConnectivityProbe.CheckAsync((IPEndPoint)listener.LocalEndpoint, cancellation.Token);
        using var hanging = await listener.AcceptTcpClientAsync(timeout.Token);
        using var stalled = hanging.GetStream();
        await stalled.ReadExactlyAsync(new byte[3], timeout.Token);
        cancellation.Cancel();
        await Assert.Throws<OperationCanceledException>(() => check.WaitAsync(timeout.Token));
    }

    static async Task AssertDomainTunnelRequest(Stream stream, CancellationToken token)
    {
        var greeting = new byte[3];
        await stream.ReadExactlyAsync(greeting, token);
        Assert.That(greeting.SequenceEqual(new byte[] { 5, 1, 0 }), "Wrong SOCKS5 loopback greeting");
        await stream.WriteAsync(new byte[] { 5, 0 }, token);
        var frame = new byte[5];
        await stream.ReadExactlyAsync(frame, token);
        Assert.That(frame.AsSpan(0, 4).SequenceEqual(new byte[] { 5, 1, 0, 3 }), "CONNECT carried an IP instead of a proxy-resolved hostname");
        var address = new byte[frame[4] + 2];
        await stream.ReadExactlyAsync(address, token);
        Assert.That(Encoding.ASCII.GetString(address.AsSpan(0, address.Length - 2)) == "check.torproject.org" &&
            BinaryPrimitives.ReadUInt16BigEndian(address.AsSpan(^2)) == 443, "CONNECT differed from the fixed HTTPS destination");
    }
}
