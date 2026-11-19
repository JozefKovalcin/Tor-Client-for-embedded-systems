using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Tor.Application;
using Tor.Control;

internal static partial class Offline
{
    private static async Task VerifyHandshakeStagesAsync(PeerIdentity host, PeerIdentity client)
    {
        using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = overall.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var hostPin = PeerIdentity.ParseFingerprint(host.Fingerprint);
        var events = new ConcurrentQueue<PeerHandshakeStage>();

        using (var tcp = new TcpClient(AddressFamily.InterNetwork))
        {
            await tcp.ConnectAsync(endpoint, token);
            using var silent = await listener.AcceptTcpClientAsync(token);
            await ExpectHandshakeDeadlineAsync(PeerConnection.AuthenticateAsync(tcp.GetStream(), client, hostPin,
                false, token, token, trace: events.Enqueue, tlsDeadline: TimeSpan.FromMilliseconds(400)),
                PeerHandshakePhase.Tls);
            Check.That(events.Contains(PeerHandshakeStage.ClientHello) && !events.Contains(PeerHandshakeStage.ServerHello),
                "A silent TLS peer did not leave an observable first client record.");
        }

        events.Clear();
        using (var tcp = new TcpClient(AddressFamily.InterNetwork))
        {
            await tcp.ConnectAsync(endpoint, token);
            using var accepted = await listener.AcceptTcpClientAsync(token);
            await using var tls = new SslStream(accepted.GetStream());
            var attempt = PeerConnection.AuthenticateAsync(tcp.GetStream(), client, hostPin,
                false, token, token, trace: events.Enqueue, readyDeadline: TimeSpan.FromSeconds(1));
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = host.Certificate,
                ClientCertificateRequired = true,
                RemoteCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString(
                    System.Security.Cryptography.HashAlgorithmName.SHA256) == client.Fingerprint,
                ApplicationProtocols = [new SslApplicationProtocol("tor-peer/1")],
                CertificateChainPolicy = new() { DisableCertificateDownloads = true }
            }, token);
            await tls.ReadExactlyAsync(new byte[4], token);
            await ExpectHandshakeDeadlineAsync(attempt, PeerHandshakePhase.Ready);
            Check.That(events.Contains(PeerHandshakeStage.ServerHello) && events.Contains(PeerHandshakeStage.RemotePinMatched) &&
                events.Contains(PeerHandshakeStage.AlpnVerified) && events.Contains(PeerHandshakeStage.ReadySent) &&
                !events.Contains(PeerHandshakeStage.ReadyReceived),
                "An authenticated peer that withheld ready was not isolated to the readiness stage.");
        }

        using (var tcp = new TcpClient(AddressFamily.InterNetwork))
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            await tcp.ConnectAsync(endpoint, token);
            using var silent = await listener.AcceptTcpClientAsync(token);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempt = PeerConnection.AuthenticateAsync(tcp.GetStream(), client, hostPin, false, token, cancel.Token,
                trace: stage => { if (stage == PeerHandshakeStage.ClientHello) started.TrySetResult(); });
            await started.Task.WaitAsync(token);
            await cancel.CancelAsync();
            await Check.RejectAsync(() => attempt, "Caller cancellation was reported as a TLS deadline.",
                typeof(OperationCanceledException));
        }
        Console.WriteLine("PASS first TLS records, pin, ALPN, ready deadlines and caller cancellation are distinguishable");
    }

    private static async Task ExpectHandshakeDeadlineAsync(Task<PeerConnection> operation, PeerHandshakePhase phase)
    {
        try { await using var unexpected = await operation; }
        catch (PeerHandshakeTimeoutException error) when (error.Phase == phase) { return; }
        throw new CheckFailed("A stalled peer fell outside the expected handshake deadline.");
    }

    private static async Task VerifyOwnedAcceptAndReconnectAsync(PeerIdentity host, PeerIdentity client)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        using var fixture = new LoopbackTransport { RefuseNextIncoming = true };
        await using var listener = await PeerHost.StartAsync(fixture, host, client.Fingerprint, token);
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Rejected(PeerHandshakeStage stage)
        {
            if (stage == PeerHandshakeStage.HostTcpOwnerRejected) rejected.TrySetResult();
        }
        var first = listener.AcceptAsync(Rejected, Rejected, token);
        using (var unowned = new TcpClient(AddressFamily.InterNetwork))
        {
            await unowned.ConnectAsync(IPAddress.Loopback, fixture.Port, token);
            await rejected.Task.WaitAsync(token);
            Check.That(await unowned.GetStream().ReadAsync(new byte[1], token) == 0,
                "Host wrote TLS data before rejecting an unowned connection.");
        }

        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < 100; i++)
        {
            var pending = i == 0 ? first : listener.AcceptAsync(token);
            await using var outgoing = await PeerConnection.ConnectAsync(fixture, listener.Address, client, host.Fingerprint, token);
            await using var incoming = await pending;
            await ExchangeAsync(incoming, outgoing, "fixture request", "fixture reply", token);
            await outgoing.CloseAsync();
            Check.That(await incoming.ReceiveAsync(token) is null, "A reconnect cycle lost its authenticated close.");
        }
        Console.WriteLine($"PASS unowned ingress rejected before TLS; 100 authenticated local reconnects in {Stopwatch.GetElapsedTime(started).TotalSeconds:F2}s");

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromSeconds(5));
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Accepted(PeerHandshakeStage stage)
        {
            if (stage == PeerHandshakeStage.HostTcpAccepted) accepted.TrySetResult();
        }
        var competing = listener.AcceptAsync(Accepted, Accepted, bounded.Token);
        using var stalled = new TcpClient(AddressFamily.InterNetwork);
        await stalled.ConnectAsync(IPAddress.Loopback, fixture.Port, bounded.Token);
        await accepted.Task.WaitAsync(bounded.Token);
        await using var live = await PeerConnection.ConnectAsync(fixture, listener.Address, client, host.Fingerprint, bounded.Token);
        await using var hosted = await competing;
        await ExchangeAsync(hosted, live, "second owned stream", "first stream stalled", bounded.Token);
        Check.That(await stalled.GetStream().ReadAsync(new byte[1], bounded.Token) == 0,
            "The stalled competing socket survived a completed peer handshake.");
        Console.WriteLine("PASS an owned TCP stream stalled before TLS does not block the next authenticated peer");

        var bothAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int acceptedCount = 0;
        void Remember(PeerHandshakeStage stage)
        {
            if (stage == PeerHandshakeStage.HostTcpAccepted && Interlocked.Increment(ref acceptedCount) == 2)
                bothAccepted.TrySetResult();
        }
        var survivor = listener.AcceptAsync(Remember, Remember, bounded.Token);
        using var closing = new TcpClient(AddressFamily.InterNetwork);
        await closing.ConnectAsync(IPAddress.Loopback, fixture.Port, bounded.Token);
        using var second = new TcpClient(AddressFamily.InterNetwork);
        await second.ConnectAsync(IPAddress.Loopback, fixture.Port, bounded.Token);
        await bothAccepted.Task.WaitAsync(bounded.Token);
        closing.Dispose();
        Check.That(await Task.WhenAny(survivor, Task.Delay(150, bounded.Token)) != survivor,
            "A failed owned stream aborted a second accepted handshake.");
        await using var survivingClient = await PeerConnection.AuthenticateAsync(second.GetStream(), client,
            PeerIdentity.ParseFingerprint(host.Fingerprint), false, token, bounded.Token);
        await using var survivingHost = await survivor;
        await ExchangeAsync(survivingHost, survivingClient, "surviving request", "surviving reply", bounded.Token);
        Console.WriteLine("PASS an early close does not cancel the other accepted TLS handshake");
    }
}
