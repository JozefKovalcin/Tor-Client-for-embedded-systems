using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Tor.Application;
using Tor.Control;
using Tor.Core;

try
{
    if (args is ["--offline"])
    {
        await Offline.RunAsync();
        return 0;
    }
    if (OperatingSystem.IsWindows() && args is ["--private-profile", var fresh])
    {
        await Offline.PrivateProfileAsync(fresh);
        return 0;
    }
    if (OperatingSystem.IsWindows() && args is ["--private-delivery", var freshDelivery])
    {
        await Offline.PrivateDeliveryAsync(freshDelivery);
        return 0;
    }
    if (OperatingSystem.IsWindows() && args is ["--native-disabled-policy", var offlineRuntime, var offlineState])
    {
        await PublicTor.VerifyDisabledPolicyAsync(offlineRuntime, offlineState);
        return 0;
    }
    if (args is not ["--public-tor", "--approve-public-tor", var runtime, var state])
    {
        Console.WriteLine("NOT RUN: explicit public Tor approval is required.");
        return 2;
    }
    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine("BLOCKED: Windows is required for public Tor qualification.");
        return 2;
    }
    await PublicTor.RunAsync(runtime, state);
    return 0;
}
catch (Exception failure)
{
    Console.Error.WriteLine("FAIL: " + (failure is CheckFailed || args is ["--offline"] ? failure.Message : failure.GetType().Name));
    return 1;
}

internal sealed class CheckFailed(string message) : Exception(message);

internal static class Check
{
    internal static void That(bool ok, string name)
    {
        if (!ok) throw new CheckFailed(name);
    }

    internal static async Task RejectAsync(Func<Task> operation, string name, params Type[] types)
    {
        try { await operation(); }
        catch (Exception error) when (types.Any(t => t.IsInstanceOfType(error))) { return; }
        throw new CheckFailed(name);
    }
}

internal static class Offline
{
    internal static async Task PrivateDeliveryAsync(string fresh)
    {
        Check.That(Path.IsPathFullyQualified(fresh) && !Directory.Exists(fresh) && !File.Exists(fresh),
            "An absolute unused test directory is required.");
        PrivateStorage.CreateNewDirectory(fresh);
        var aPath = Path.Combine(fresh, "A");
        var bPath = Path.Combine(fresh, "B");
        PrivateStorage.CreateNewDirectory(aPath);
        PrivateStorage.CreateNewDirectory(bPath);
        var aProfile = new PeerProfileStore(aPath);
        var bProfile = new PeerProfileStore(bPath);
        using var a = aProfile.CreateIdentity();
        using var b = bProfile.CreateIdentity();
        var bContact = aProfile.Pair(a, "Fixture B", b.Fingerprint, null, true);
        var aContact = bProfile.Pair(b, "Fixture A", a.Fingerprint, null, true);
        var aStore = aProfile.OpenDelivery(a, bContact);
        var bStore = bProfile.OpenDelivery(b, aContact);
        const string sentText = "once despite lost ACK";
        var outbound = aStore.Enqueue(sentText);
        var wire = ReliablePeerWire.Encode(outbound);
        await Check.RejectAsync(() => Task.FromResult(ReliablePeerWire.Decode("A invalid")),
            "Malformed ACK was accepted.", typeof(InvalidDataException));
        await Check.RejectAsync(() => Task.FromResult(ReliablePeerWire.Decode(wire + " ")),
            "Noncanonical payload was accepted.", typeof(InvalidDataException));
        Check.That(aProfile.OpenDelivery(a, bContact).Snapshot().Outbox.Single().Id == outbound.Id,
            "Lost pending message on reopen.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var fixture = new LoopbackTransport();
        await using var host = await PeerHost.StartAsync(fixture, b, a.Fingerprint, deadline.Token, PeerProtocol.Reliable);
        await Check.RejectAsync(() => PairAsync(host, fixture, b.Fingerprint, a, deadline.Token),
            "Reliable transport silently downgraded to version 1.", typeof(AuthenticationException), typeof(IOException));
        var first = await PairAsync(host, fixture, b.Fingerprint, a, deadline.Token, PeerProtocol.Reliable);
        await using (first.Client)
        await using (first.Server)
        {
            using var active = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var session = new ReliablePeerSession(first.Client, aStore, state =>
            {
                if (state.Outbox.Any(item => item.Id == outbound.Id && item.Status == PeerDeliveryStatus.Sent))
                    sent.TrySetResult();
            });
            var received = first.Server.ReceiveAsync(deadline.Token);
            var sender = session.RunAsync(active.Token);
            var decoded = ReliablePeerWire.Decode((await received)!);
            await sent.Task.WaitAsync(deadline.Token);
            Check.That(aStore.Snapshot().Outbox.Single().Status == PeerDeliveryStatus.Pending,
                "An unacknowledged send changed the durable queue state.");
            Check.That(bStore.Accept(decoded.Id, decoded.ExpiresUtc, decoded.Text!) == PeerAcceptance.Accepted,
                "New message was not durably accepted.");
            await Check.RejectAsync(() => Task.FromResult(bStore.Accept(decoded.Id, decoded.ExpiresUtc, "changed")),
                "A reused ID with changed contents was accepted.", typeof(InvalidDataException));
            // Deliberately end before an ACK is sent, as if the recipient crashed here.
            await active.CancelAsync();
            try { await sender; }
            catch (OperationCanceledException) when (active.IsCancellationRequested) { }
        }
        Check.That(aStore.Snapshot().Outbox.Single().Status == PeerDeliveryStatus.Pending, "Missing ACK implied delivery.");
        aStore = aProfile.OpenDelivery(a, bContact);
        bStore = bProfile.OpenDelivery(b, aContact);
        aStore.SetHistoryEnabled(true);
        var reply = bStore.Enqueue("reply after restart");
        var second = await PairAsync(host, fixture, b.Fingerprint, a, deadline.Token, PeerProtocol.Reliable);
        await using (second.Client)
        await using (second.Server)
        {
            await Check.RejectAsync(() => Task.FromResult(new ReliablePeerSession(second.Client, bStore, _ => { })),
                "Connection accepted a queue from the wrong local or peer pin.", typeof(ArgumentException));
            var aDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var aSession = new ReliablePeerSession(second.Client, aStore, state =>
            {
                if (state.Outbox.Any(item => item.Id == outbound.Id && item.Status == PeerDeliveryStatus.Delivered) &&
                    state.Inbox.Any(item => item.Id == reply.Id))
                    aDelivered.TrySetResult();
            });
            var bSession = new ReliablePeerSession(second.Server, bStore, state =>
            {
                if (state.Outbox.Any(item => item.Id == reply.Id && item.Status == PeerDeliveryStatus.Delivered))
                    bDelivered.TrySetResult();
            });
            using var active = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var sender = aSession.RunAsync(active.Token);
            var receiver = bSession.RunAsync(active.Token);
            try
            {
                await Task.WhenAll(aDelivered.Task, bDelivered.Task).WaitAsync(deadline.Token);
                var inbox = bStore.Snapshot().Inbox;
                Check.That(inbox.Count == 1 && inbox[0].Id == outbound.Id && inbox[0].Text == sentText,
                    "Lost ACK caused duplicate or changed delivery.");
                Check.That(aStore.Snapshot().Outbox.Single().Text is null, "ACK did not clear outbound payload.");
                var history = aProfile.OpenDelivery(a, bContact).Snapshot().History;
                Check.That(history.Count == 2 && history.Any(row => row.Incoming && row.Text == reply.Text) &&
                    history.Any(row => !row.Incoming && row.Text == sentText), "Opt-in history did not survive reopening.");
                Check.That(bStore.Snapshot().History.Count == 0, "History recorded text without opt-in.");
            }
            finally
            {
                await active.CancelAsync();
                try { await Task.WhenAll(sender, receiver); }
                catch (OperationCanceledException) when (active.IsCancellationRequested) { }
            }
        }
        var clock = new FixtureClock(DateTimeOffset.UtcNow);
        var expiring = new PeerDeliveryStore(Path.Combine(aPath, "peer-security"), a.Fingerprint, b.Fingerprint, clock);
        expiring.SetHistoryEnabled(false);
        var delayed = expiring.Enqueue("expires");
        clock.Utc = clock.Utc.AddMinutes(16);
        var expired = expiring.Snapshot().Outbox.Single(item => item.Id == delayed.Id);
        Check.That(expired.Status == PeerDeliveryStatus.Expired && expired.Text is null, "TTL did not expire the stored payload.");
        Check.That(expiring.Accept(delayed.Id, delayed.ExpiresUtc, "expires") == PeerAcceptance.Expired, "Expired message returned as fresh.");
        Check.That(expiring.Snapshot().History.Count == 2 && !expiring.Snapshot().HistoryEnabled,
            "Opting out did not preserve prior history within its retention window.");
        clock.Utc = clock.Utc.AddDays(8);
        Check.That(expiring.Snapshot().History.Count == 0, "Opt-in history exceeded its retention.");
        for (int index = 0; index < 64; index++) expiring.Enqueue("bounded fixture");
        await Check.RejectAsync(() => Task.FromResult(expiring.Enqueue("one too many")),
            "Queue capacity displaced an unacknowledged message.", typeof(InvalidOperationException));
        Check.That(expiring.Snapshot().Outbox.All(item => item.Status == PeerDeliveryStatus.Pending),
            "A full queue changed pending delivery state.");
        var security = Path.Combine(aPath, "peer-security");
        var queueFile = Path.Combine(security, "delivery-" + b.Fingerprint + ".dpapi");
        var queueLock = Path.ChangeExtension(queueFile, ".lock");
        using (var unavailable = new FileStream(queueLock, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Check.RejectAsync(() => Task.FromResult(expiring.Snapshot()),
                "The delivery store ignored its exclusive writer lease.", typeof(IOException));
        byte[] damaged = [0x27, 0x19, 0x08];
        File.WriteAllBytes(queueFile, damaged);
        await Check.RejectAsync(() => Task.FromResult(expiring.Snapshot()),
            "The delivery store accepted corrupt protected state.", typeof(CryptographicException), typeof(InvalidDataException));
        Check.That(File.ReadAllBytes(queueFile).SequenceEqual(damaged), "Failed open replaced the protected queue.");
        var damagedPair = await PairAsync(host, fixture, b.Fingerprint, a, deadline.Token, PeerProtocol.Reliable);
        await using (damagedPair.Client)
        await using (damagedPair.Server)
        {
            var session = new ReliablePeerSession(damagedPair.Client, expiring, _ => { });
            await Check.RejectAsync(() => Task.FromResult(session.DisplaySnapshot()),
                "Unreadable local state was not classified as a private storage failure.", typeof(PeerDeliveryStorageException));
        }
        Console.WriteLine("PASS durable pre-send, lost ACK, pin-bound queues, restart deduplication, bidirectional ACK, TTL and opt-in history");
        Console.WriteLine("PASS session-local Sent, full queue, writer lease contention and corrupt DPAPI refusal");
    }

    private sealed class FixtureClock(DateTimeOffset utc) : TimeProvider
    {
        internal DateTimeOffset Utc = utc;
        public override DateTimeOffset GetUtcNow() => Utc;
    }

    internal static async Task PrivateProfileAsync(string fresh)
    {
        Check.That(Path.IsPathFullyQualified(fresh) && !Directory.Exists(fresh) && !File.Exists(fresh),
            "An absolute unused test directory is required.");
        PrivateStorage.CreateNewDirectory(fresh);
        var original = new PeerProfileStore(fresh);
        Check.That(original.OpenIdentity() is null && !Directory.Exists(Path.Combine(fresh, "peer-security")),
            "A read created a new private identity.");
        using var remote = new PeerIdentity();
        string fingerprint;
        using (var local = original.CreateIdentity())
        {
            fingerprint = local.Fingerprint;
            await Check.RejectAsync(() => Task.FromResult(original.Pair(local, "Fixture", remote.Fingerprint, null, false)),
                "Unchecked pairing was accepted.", typeof(InvalidOperationException));
            original.Pair(local, "Fixture", remote.Fingerprint.ToLowerInvariant(), null, true);
        }
        var reopened = new PeerProfileStore(fresh);
        using (var local = reopened.OpenIdentity()!)
        {
            Check.That(local.Fingerprint == fingerprint, "Restart replaced the certificate.");
            Check.That(reopened.ReadContacts(local).Single().Fingerprint == remote.Fingerprint, "Pairing did not persist.");
            await Check.RejectAsync(() => Task.FromResult(reopened.Pair(local, "Replacement name", remote.Fingerprint, null, true)),
                "Existing pin was changed without a newly reviewed identity.", typeof(InvalidOperationException));
            reopened.Revoke(local, remote.Fingerprint);
            await Check.RejectAsync(() => Task.FromResult(reopened.Pair(local, "Revoked", remote.Fingerprint, null, true)),
                "Revoked pin was restored implicitly.", typeof(InvalidOperationException));
        }
        using (var local = reopened.OpenIdentity()!)
            Check.That(reopened.ReadContacts(local).Single().Revoked, "Revocation was lost across a reload.");
        Console.WriteLine("PASS first-use consent, identity/pin restart, duplicate trust and persistent revocation");

        var secret = Path.Combine(fresh, "peer-security", "local-identity.dpapi");
        byte[] encrypted = File.ReadAllBytes(secret);
        Check.That(encrypted.Length > 32, "Expected a protected credential.");
        File.WriteAllBytes(secret, [0x11, 0x22, 0x33]);
        await Check.RejectAsync(() => Task.FromResult(reopened.OpenIdentity()), "Corrupt data auto-generated a new key.",
            typeof(CryptographicException));
        await Check.RejectAsync(() => Task.FromResult(reopened.CreateIdentity()), "Existing profile was overwritten.",
            typeof(IOException));
        Check.That(File.ReadAllBytes(secret).SequenceEqual(new byte[] { 0x11, 0x22, 0x33 }), "Failed open changed stored credential.");
        Console.WriteLine("PASS malformed or lost identity fails closed without replacing credential");
    }

    internal static async Task RunAsync()
    {
        VerifyFraming();
        await VerifySocksAsync();
        using var a = new PeerIdentity();
        using var b = new PeerIdentity();
        using var other = new PeerIdentity();
        using var fixture = new LoopbackTransport();
        await using var host = await PeerHost.StartAsync(fixture, a, b.Fingerprint);
        Check.That(fixture.Port > 0, "The host did not register its bound listener.");
        using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = overall.Token;
        await Check.RejectAsync(() => PairAsync(host, fixture, a.Fingerprint, other, token), "A wrong client pin was accepted.",
            typeof(AuthenticationException), typeof(IOException));
        await Check.RejectAsync(() => PairAsync(host, fixture, other.Fingerprint, b, token), "A wrong server pin was accepted.",
            typeof(AuthenticationException), typeof(IOException));
        Console.WriteLine("PASS mutual certificate pins reject either wrong peer");
        await VerifyCorruptedRecordAsync(a, b, token);
        await VerifyMalformedApplicationRecordsAsync(a, b, token);

        var pair = await PairAsync(host, fixture, a.Fingerprint, b, token);
        await using (pair.Server)
        await using (pair.Client)
        {
            await ExchangeAsync(pair.Server, pair.Client, "α from B", "ď from A", token);
            await Check.RejectAsync(() => pair.Client.SendAsync(new string('x', PeerConnection.MaxMessageBytes + 1), token),
                "Oversized outbound message was accepted.", typeof(ArgumentException));
            await ExchangeAsync(pair.Server, pair.Client, new string('x', PeerConnection.MaxMessageBytes), "second", token);
            await pair.Client.CloseAsync();
            Check.That(await pair.Server.ReceiveAsync(token) is null, "Authenticated close frame was not received.");
        }
        Console.WriteLine("PASS concurrent two-way maximum-size transfer, framing and clean close");

        pair = await PairAsync(host, fixture, a.Fingerprint, b, token);
        await using (pair.Server)
        await using (pair.Client)
        {
            await pair.Client.DisposeAsync();
            await Check.RejectAsync(async () => _ = await pair.Server.ReceiveAsync(token), "Abrupt disconnect produced a clean close.",
                typeof(IOException));
        }
        pair = await PairAsync(host, fixture, a.Fingerprint, b, token);
        await using (pair.Server)
        await using (pair.Client)
        {
            await ExchangeAsync(pair.Server, pair.Client, "reconnect request", "reconnect reply", token);
            await ExchangeAsync(pair.Server, pair.Client, "α from B", "ď from A", token);
            Console.WriteLine("PASS identical application text is allowed on a freshly authenticated session");
            using var readDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
            await Check.RejectAsync(async () => _ = await pair.Server.ReceiveAsync(readDeadline.Token), "Read deadline did not cancel.",
                typeof(OperationCanceledException));
            await Check.RejectAsync(() => pair.Server.SendAsync("closed", token), "Cancelled connection remained writable.",
                typeof(ObjectDisposedException));
        }
        Console.WriteLine("PASS abrupt disconnect, authenticated reconnect, read deadline and cleanup");

        using var acceptCancellation = new CancellationTokenSource();
        var accepted = host.AcceptAsync(acceptCancellation.Token);
        await acceptCancellation.CancelAsync();
        await Check.RejectAsync(() => accepted, "Pending accept was not cancelled.", typeof(OperationCanceledException));
        int port = fixture.Port;
        await host.DisposeAsync();
        Check.That(fixture.Removed, "Onion ownership was not removed.");
        using var closedListener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
        closedListener.Start();
        Console.WriteLine("PASS cancelled accept, onion removal and listener release");
        Console.WriteLine("7 offline peer groups PASS");
    }

    private static async Task VerifyMalformedApplicationRecordsAsync(PeerIdentity host, PeerIdentity client, CancellationToken token)
    {
        var fixtures = new (uint Size, byte[] Payload)[] { (0, []), (PeerConnection.MaxMessageBytes + 1, []), (1, [0xff]) };
        foreach (var fixture in fixtures)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            await tcp.ConnectAsync((IPEndPoint)listener.LocalEndpoint, token);
            using var accepted = await listener.AcceptTcpClientAsync(token);
            await using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            var pending = PeerConnection.AuthenticateAsync(accepted.GetStream(), host,
                PeerIdentity.ParseFingerprint(client.Fingerprint), true, token, token);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "tor-peer.invalid", ClientCertificates = [client.Certificate],
                LocalCertificateSelectionCallback = (_, _, _, _, _) => client.Certificate,
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null &&
                    certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256) == host.Fingerprint,
                CertificateChainPolicy = new() { DisableCertificateDownloads = true },
                ApplicationProtocols = [new SslApplicationProtocol("tor-peer/1")], AllowTlsResume = false
            }, token);
            await tls.WriteAsync(new byte[] { 0x54, 0x50, 1, 0x43 }, token);
            await tls.ReadExactlyAsync(new byte[4], token);
            await using var server = await pending;
            var record = new byte[12 + fixture.Payload.Length];
            new byte[] { 0x54, 0x50, 1, 1 }.CopyTo(record, 0);
            BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(8), fixture.Size);
            fixture.Payload.CopyTo(record, 12);
            await tls.WriteAsync(record, token);
            await Check.RejectAsync(async () => _ = await server.ReceiveAsync(token),
                "An invalid frame did not surface as a recoverable channel error.", typeof(IOException));
            await Check.RejectAsync(() => server.SendAsync("closed", token), "Invalid frame left the session open.",
                typeof(ObjectDisposedException));
        }
        Console.WriteLine("PASS authenticated empty, oversized and malformed UTF-8 records fail closed as channel errors");
    }

    private static async Task VerifyCorruptedRecordAsync(PeerIdentity a, PeerIdentity b, CancellationToken token)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        await tcp.ConnectAsync((IPEndPoint)listener.LocalEndpoint, token);
        using var accepted = await listener.AcceptTcpClientAsync(token);
        using var corrupt = new CorruptibleStream(tcp.GetStream());
        var host = PeerConnection.AuthenticateAsync(accepted.GetStream(), a, PeerIdentity.ParseFingerprint(b.Fingerprint),
            true, token, token);
        var peer = PeerConnection.AuthenticateAsync(corrupt, b, PeerIdentity.ParseFingerprint(a.Fingerprint),
            false, token, token);
        await Task.WhenAll(host, peer);
        await using var server = await host;
        await using var client = await peer;
        corrupt.CorruptNextWrite = true;
        await client.SendAsync("corrupt ciphertext", token);
        await Check.RejectAsync(async () => _ = await server.ReceiveAsync(token), "Corrupted TLS data produced a message.",
            typeof(IOException), typeof(AuthenticationException));
        await Check.RejectAsync(() => server.SendAsync("closed", token), "A damaged TLS stream remained writable.",
            typeof(ObjectDisposedException));
        Console.WriteLine("PASS corrupted TLS record is rejected before application delivery");
    }

    private sealed class CorruptibleStream(Stream inner) : Stream
    {
        internal bool CorruptNextWrite { get; set; }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Flush() => inner.Flush();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => inner.ReadAsync(buffer, token);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            if (!CorruptNextWrite || buffer.Length == 0) return inner.WriteAsync(buffer, token);
            CorruptNextWrite = false;
            var changed = buffer.ToArray();
            changed[^1] ^= 1;
            return inner.WriteAsync(changed, token);
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private static void VerifyFraming()
    {
        var bytes = new byte[] { (byte)'T', (byte)'P', 1, 1, 0, 0, 0, 1, 0, 0, 0, 3 };
        Check.That(PeerConnection.DecodeHeader(bytes, 1) == (1, 3), "Version one header did not decode.");
        RejectHeader(bytes, 2);
        bytes[2] = 2;
        RejectHeader(bytes, 1);
        bytes[2] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), uint.MaxValue);
        RejectHeader(bytes, 1);
        RejectHeader(bytes[..11], 1);
        Console.WriteLine("PASS replay, corrupted header, version and overflow are rejected");
    }

    private static void RejectHeader(byte[] bytes, uint sequence)
    {
        try { _ = PeerConnection.DecodeHeader(bytes, sequence); }
        catch (InvalidDataException) { return; }
        throw new CheckFailed("Invalid frame header was accepted.");
    }

    private static async Task VerifySocksAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var socks = new TcpListener(IPAddress.Loopback, 0);
        socks.Start();
        var address = LoopbackTransport.Address;
        var attempt = OnionSocksConnection.ConnectAsync((IPEndPoint)socks.LocalEndpoint, address, _ => true, deadline.Token);
        using var peer = await socks.AcceptTcpClientAsync(deadline.Token);
        using var connection = peer.GetStream();
        var greeting = new byte[3];
        await connection.ReadExactlyAsync(greeting, deadline.Token);
        Check.That(greeting.SequenceEqual(new byte[] { 5, 1, 0 }), "Unexpected SOCKS authentication offer.");
        await connection.WriteAsync(new byte[] { 5, 0 }, deadline.Token);
        var data = new byte[7 + address.Host.Length];
        await connection.ReadExactlyAsync(data, deadline.Token);
        Check.That(data.AsSpan(0, 5).SequenceEqual(new byte[] { 5, 1, 0, 3, (byte)address.Host.Length }) &&
            Encoding.ASCII.GetString(data.AsSpan(5, address.Host.Length)) == address.Host &&
            BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(^2)) == OnionAddress.VirtualPort,
            "The complete onion name must be sent in a domain-form CONNECT to port 80.");
        await connection.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 0 }, deadline.Token);
        await using var tunnel = await attempt;
        Console.WriteLine("PASS SOCKS5 sends the onion name in the domain field without local DNS");
    }

    internal static async Task<(PeerConnection Server, PeerConnection Client)> PairAsync(PeerHost host,
        IOnionPeerTransport transport, string hostPin, PeerIdentity client, CancellationToken token,
        PeerProtocol protocol = PeerProtocol.Transient)
    {
        var inbound = host.AcceptAsync(token);
        var outbound = PeerConnection.ConnectAsync(transport, host.Address, client, hostPin, token, protocol);
        try { await Task.WhenAll(inbound, outbound); }
        catch
        {
            if (inbound.IsCompletedSuccessfully) await inbound.Result.DisposeAsync();
            if (outbound.IsCompletedSuccessfully) await outbound.Result.DisposeAsync();
            throw;
        }
        return (await inbound, await outbound);
    }

    internal static async Task ExchangeAsync(PeerConnection server, PeerConnection client, string request, string response,
        CancellationToken token)
    {
        var receivedByA = server.ReceiveAsync(token);
        var receivedByB = client.ReceiveAsync(token);
        await Task.WhenAll(client.SendAsync(request, token), server.SendAsync(response, token));
        Check.That(string.Equals(await receivedByA, request, StringComparison.Ordinal), "Client A received different content.");
        Check.That(string.Equals(await receivedByB, response, StringComparison.Ordinal), "Client B received different content.");
    }

    private sealed class LoopbackTransport : IOnionPeerTransport, IDisposable
    {
        private readonly CancellationTokenSource lifetime = new();
        internal static OnionAddress Address { get; } = OnionAddress.Parse(new string('a', 56) + ".onion");
        internal int Port { get; private set; }
        internal bool Removed { get; private set; }
        public CancellationToken Lifetime => lifetime.Token;
        public Task<OnionAddress> HostAsync(int loopbackPort, CancellationToken token)
        {
            Port = loopbackPort;
            return Task.FromResult(Address);
        }
        public Task RemoveAsync(OnionAddress address) { Removed = true; return Task.CompletedTask; }
        public Task WaitForPublicationAsync(OnionAddress address, CancellationToken token) => Task.CompletedTask;
        public async Task<Stream> ConnectAsync(OnionAddress address, CancellationToken token)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, Port), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        }
        public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
    }
}

[SupportedOSPlatform("windows")]
internal static class PublicTor
{
    internal static async Task VerifyDisabledPolicyAsync(string package, string state)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Tor.Core.PrivateStorage.CreateNewDirectory(state);
        using var listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        listener.Start();
        await using var session = await Tor.Adapters.Windows.ManagedTorSession.StartOfflineAsync(
            new Tor.Adapters.Windows.PinnedWindowsRuntime(package), state, deadline.Token);
        await session.Control.SubscribeOnionPublicationAsync();
        var address = await session.Control.AddOnionAsync(((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
        Check.That(address.ServiceId.Length == 56, "The pinned native Tor did not return a v3 onion ID.");
        await session.Control.DeleteOnionAsync(address);
        var observed = await session.Control.GetOnlineStatusAsync(deadline.Token);
        Check.That(observed.SocksListeners.Count == 0 && session.Control.LatestBootstrap?.Percent == 0,
            "The offline policy test left networking enabled.");
        Check.That((await session.StopAsync()).RawExitCode == 0, "The native offline process did not stop cleanly.");
        Console.WriteLine("PASS pinned Tor accepted typed v3 ADD_ONION and DEL_ONION with networking disabled.");
    }

    internal static async Task RunAsync(string package, string state)
    {
        var root = Path.GetFullPath(state);
        Check.That(!Directory.Exists(root) && !File.Exists(root), "The qualification needs a fresh private parent.");
        Tor.Core.PrivateStorage.CreateNewDirectory(root);
        using var overall = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        var token = overall.Token;
        await using IPeerTorService a = OfflineTorService.CreateWindows(package, Path.Combine(root, "client A"));
        await using IPeerTorService b = OfflineTorService.CreateWindows(package, Path.Combine(root, "client B"));
        using var idA = new PeerIdentity();
        using var idB = new PeerIdentity();
        int portA = PickPort(), portB;
        do { portB = PickPort(); } while (portA == portB);
        Console.WriteLine("START two independent Windows Tor clients with separate private state.");
        await Task.WhenAll(a.StartOnlineAsync(portA, token), b.StartOnlineAsync(portB, token));
        var boot = await Task.WhenAll(ReadyAsync(a, "A", token), ReadyAsync(b, "B", token));
        Check.That(boot[0] != boot[1], "The two clients did not launch distinct processes.");
        await using var host = await PeerHost.StartAsync(a.GetPeerTransport()!, idA, idB.Fingerprint, token);
        using (var publication = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            publication.CancelAfter(TimeSpan.FromMinutes(3));
            await host.WaitForPublicationAsync(publication.Token);
        }
        Console.WriteLine("PASS client A published a v3 onion descriptor (authenticated HS_DESC UPLOADED).");
        var pair = await Offline.PairAsync(host, b.GetPeerTransport()!, idA.Fingerprint, idB, token);
        await using (pair.Server)
        await using (pair.Client)
        {
            Console.WriteLine("PASS client B connected through its verified SOCKS listener and mutually pinned TLS.");
            await RoundTripAsync(pair.Server, pair.Client, "B → A | TASK 10 integrity", "A → B | verified response", token);
            Console.WriteLine("PASS both test messages arrived unchanged.");
            await b.StopAsync(token);
            Check.That(!ProcessAlive(boot[1]), "Client B Tor survived controlled interruption.");
            await Check.RejectAsync(async () => _ = await pair.Server.ReceiveAsync(token), "Controlled interruption looked like a clean close.",
                typeof(IOException));
        }
        Console.WriteLine("PASS controlled interruption stopped B and broke the peer connection.");
        await b.StartOnlineAsync(portB, token);
        var reconnectPid = await ReadyAsync(b, "B after interruption", token);
        pair = await Offline.PairAsync(host, b.GetPeerTransport()!, idA.Fingerprint, idB, token);
        await using (pair.Server)
        await using (pair.Client)
        {
            await RoundTripAsync(pair.Server, pair.Client, "B reconnect", "A reconnect", token);
            await pair.Client.CloseAsync();
            Check.That(await pair.Server.ReceiveAsync(token) is null, "Authenticated peer close was missing on reconnect.");
        }
        Console.WriteLine("PASS reconnection to the same hosted onion, mutual authentication and two-way integrity.");
        await host.DisposeAsync();
        await Task.WhenAll(a.StopAsync(token), b.StopAsync(token));
        Check.That(!ProcessAlive(boot[0]) && !ProcessAlive(reconnectPid), "An owned Tor process survived teardown.");
        Check.That(a.Snapshot.CurrentTask?.RawExitCode == 0 && b.Snapshot.CurrentTask?.RawExitCode == 0,
            "Final Tor shutdown did not return exit code zero.");
        Console.WriteLine("PASS authenticated close, onion removal and both Tor processes terminated with raw exit code 0.");
    }

    private static async Task RoundTripAsync(PeerConnection server, PeerConnection client, string request, string response,
        CancellationToken token)
    {
        await client.SendAsync(request, token);
        Check.That(await server.ReceiveAsync(token) == request, "Client A received different content.");
        await server.SendAsync(response, token);
        Check.That(await client.ReceiveAsync(token) == response, "Client B received different content.");
    }

    private static async Task<int> ReadyAsync(IPeerTorService service, string role, CancellationToken token)
    {
        await foreach (var s in service.WatchAsync(token))
        {
            if (s.SessionStatus == TorSessionStatus.Failed || !s.CanStop && s.LastError is not null)
                throw new CheckFailed("Client " + role + " failed before completing bootstrap.");
            if (s is { BootstrapPercent: 100, ControlStatus: TorControlStatus.Authenticated, ProcessId: int pid } &&
                service.GetPeerTransport() is not null)
            {
                Console.WriteLine("PASS client " + role + " reported SAFECOOKIE, 100% bootstrap and an owned SOCKS listener.");
                return pid;
            }
        }
        throw new CheckFailed("Client snapshot ended before bootstrap completed.");
    }

    private static int PickPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool ProcessAlive(int pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited && p.ProcessName == "tor"; }
        catch (ArgumentException) { return false; }
    }
}
