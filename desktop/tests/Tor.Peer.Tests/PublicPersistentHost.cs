using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Text.Json;
using Tor.Adapters.Windows;
using Tor.Application;
using Tor.Control;
using Tor.Core;

internal static partial class PublicTor
{
    // Both connection profiles and the runtime approval are scoped to this run.
    [SupportedOSPlatform("windows")]
    internal static async Task RunPersistentHostAsync(string input, string state, bool bundled, bool compareFresh)
    {
        var root = Path.GetFullPath(state);
        PrivateStorage.CreateNewDirectory(root);
        var runtime = await SelectPersistentRuntimeAsync(input, root, bundled);

        string profileA = Path.Combine(root, "host A");
        string profileB = Path.Combine(root, "client B");
        var overall = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        using (overall)
        {
            var token = overall.Token;
            int portA = PickPort(), portB;
            do { portB = PickPort(); } while (portA == portB);

            await using var clientB = OfflineTorService.CreateWindows(runtime, profileB);
            using var idB = new PeerIdentity();
            OnionAddress firstAddress;
            string firstFingerprint;
            int firstHost, client;
            await using (var hostA = OfflineTorService.CreateWindows(runtime, profileA))
            {
                var store = new HostedOnionStore(profileA);
                Check.That(store.ReadAddress() is null, "The fresh host already has an Onion Service identity.");
                await Task.WhenAll(hostA.StartOnlineAsync(portA, token), clientB.StartOnlineAsync(portB, token));
                var pids = await Task.WhenAll(ReadyAsync(hostA, "A", token), ReadyAsync(clientB, "B", token));
                firstHost = pids[0];
                client = pids[1];
                Check.That(firstHost != client, "The peers did not launch distinct Tor processes.");
                using var idA = new PeerProfileStore(profileA).CreateIdentity();
                firstFingerprint = idA.Fingerprint;
                await using (var hosted = await StartPersistentAsync(hostA, store, idA, idB,
                    HostingIdentityAction.Create, null, token))
                {
                    firstAddress = hosted.Address;
                    Check.That(store.ReadAddress() == firstAddress, "The generated v3 address was not saved.");
                    await CheckPublicationAsync(hosted, "initial", token);
                    await CheckPublicExchangeAsync(hosted, clientB, firstFingerprint, idB, "initial", token);
                }
                await StopVerifiedAsync(hostA, firstHost, token);
                Console.WriteLine("PASS host A stopped and its first Tor process exited with code 0.");
            }

            // Reopen the host service and the protected identity from disk.
            await using var reopenedA = OfflineTorService.CreateWindows(runtime, profileA);
            var reopenedStore = new HostedOnionStore(profileA);
            using var reopenedId = new PeerProfileStore(profileA).OpenIdentity()
                ?? throw new CheckFailed("The host TLS identity could not be reopened.");
            Check.That(reopenedStore.ReadAddress() == firstAddress && reopenedId.Fingerprint == firstFingerprint,
                "The protected onion or TLS identity changed when the host was reopened.");
            Check.That(ProcessAlive(client), "Client B did not remain running while host A restarted.");
            await reopenedA.StartOnlineAsync(portA, token);
            int secondHost = await ReadyAsync(reopenedA, "A after restart", token);
            Check.That(secondHost != firstHost && secondHost != client, "The restarted host did not receive a fresh PID.");
            await using (var hosted = await StartPersistentAsync(reopenedA, reopenedStore, reopenedId, idB,
                HostingIdentityAction.Restore, firstAddress, token))
            {
                Check.That(hosted.Address == firstAddress, "Tor returned a different v3 address after the restart.");
                Console.WriteLine("PASS native Tor restored the same v3 address and the host reopened the same TLS identity.");
                await CheckPublicationAsync(hosted, "restored", token);
                try { await CheckPublicExchangeAsync(hosted, clientB, firstFingerprint, idB, "restored", token); }
                catch (CheckFailed liveFailure) when (compareFresh && !token.IsCancellationRequested)
                {
                    Console.WriteLine($"FAIL original client: {liveFailure.Message}");
                    int freshPort;
                    do { freshPort = PickPort(); } while (freshPort == portA || freshPort == portB);
                    await using var freshB = OfflineTorService.CreateWindows(runtime, Path.Combine(root, "fresh client B"));
                    await freshB.StartOnlineAsync(freshPort, token);
                    int freshPid = await ReadyAsync(freshB, "fresh B", token);
                    await CheckPublicExchangeAsync(hosted, freshB, firstFingerprint, idB, "fresh-client comparison", token);
                    await StopVerifiedAsync(freshB, freshPid, token);
                    throw new CheckFailed("The fresh-client comparison passed. The original-client reconnect failed.");
                }
            }
            await Task.WhenAll(StopVerifiedAsync(reopenedA, secondHost, token), StopVerifiedAsync(clientB, client, token));
            Console.WriteLine("PASS same v3 address and TLS fingerprint after native host restart. Both peers stopped with exit code 0.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<PinnedWindowsRuntime> SelectPersistentRuntimeAsync(string path, string root, bool bundled)
    {
        if (bundled)
        {
            // ManagedTorSession checks the fixed runtime pins before staging either process.
            Console.WriteLine("Runtime selection: bundled Windows Tor with fixed EXE and DLL pins.");
            return new PinnedWindowsRuntime(Path.GetFullPath(path));
        }
        var input = new FileInfo(path);
        Check.That(input.Length is > 0 and <= 64 * 1024, "The runtime candidate file has an invalid size.");
        await using var stream = input.OpenRead();
        var candidate = await JsonSerializer.DeserializeAsync<RuntimeCandidate>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new CheckFailed("No verified runtime candidate was supplied.");
        Check.That(candidate.Profile == "optimized-static", "An optimized-static runtime candidate is required.");
        var selection = new WindowsRuntimeSelection(Path.Combine(root, "runtime"));
        selection.Activate(candidate);
        var runtime = selection.ReadRuntime(candidate.PackagePath);
        Check.That(runtime.IsCustom, "The isolated run did not select the verified custom runtime.");
        Console.WriteLine("PASS isolated custom runtime verified EXE, DLL and build-report hashes.");
        return runtime;
    }

    private static Task<PeerHost> StartPersistentAsync(IPeerTorService service, HostedOnionStore store, PeerIdentity host,
        PeerIdentity client, HostingIdentityAction action, OnionAddress? expected, CancellationToken token)
    {
        var transport = service.GetPeerTransport() as IPersistentOnionPeerTransport
            ?? throw new CheckFailed("Verified Tor did not offer persistent Onion Service hosting.");
        return PeerHost.StartAsync(transport, host, client.Fingerprint, token,
            register: (port, ct) => store.RegisterAsync(transport, port, action, expected, ct));
    }

    private static async Task CheckPublicationAsync(PeerHost host, string phase, CancellationToken token)
    {
        using var publication = CancellationTokenSource.CreateLinkedTokenSource(token);
        publication.CancelAfter(TimeSpan.FromMinutes(3));
        try { await host.WaitForPublicationAsync(publication.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new CheckFailed($"Timed out waiting for {phase} v3 descriptor publication."); }
        Console.WriteLine($"PASS {phase} v3 descriptor published (authenticated HS_DESC UPLOADED).");
    }

    private static async Task CheckPublicExchangeAsync(PeerHost host, IPeerTorService service, string hostPin,
        PeerIdentity client, string phase, CancellationToken token)
    {
        var transport = service.GetPeerTransport()
            ?? throw new CheckFailed("Client B has no verified SOCKS listener.");
        int maxAttempts = phase == "restored" ? 3 : 2;
        byte[] pin = PeerIdentity.ParseFingerprint(hostPin);
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, transport.Lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(195));
            using var descriptor = transport is IOnionDescriptorDiagnostics diagnostics
                ? await diagnostics.ObserveDescriptorAsync(host.Address, deadline.Token) : null;
            var firstHostTrace = new HandshakeTiming();
            var secondHostTrace = new HandshakeTiming();
            var clientTrace = new HandshakeTiming();
            var inbound = host.AcceptAsync(firstHostTrace.Record, secondHostTrace.Record, deadline.Token);
            string stage = "SOCKS tunnel";
            Exception? exchangeFailure = null, hostFailure = null;
            try
            {
                var tunnel = await transport.ConnectAsync(host.Address, deadline.Token);
                clientTrace.Record(PeerHandshakeStage.ClientSocksEstablished);
                stage = "mutual TLS";
                await using var connection = await PeerConnection.AuthenticateAsync(
                    tunnel, client, pin, false, transport.Lifetime, deadline.Token, trace: clientTrace.Record);
                stage = "host handshake completion";
                var server = await inbound;
                stage = "bidirectional transfer";
                await RoundTripAsync(server, connection, $"{phase} B → A", $"{phase} A → B", deadline.Token);
                stage = "authenticated close";
                await connection.CloseAsync();
                Check.That(await server.ReceiveAsync(deadline.Token) is null, "The peer close was not authenticated.");
                Console.WriteLine($"PASS {phase} mutual TLS and bidirectional text integrity through the owned SOCKS listener.");
                return;
            }
            catch (Exception error) when (IsConnectionFailure(error))
            {
                exchangeFailure = error;
            }
            finally
            {
                bool pendingAccept = !inbound.IsCompleted;
                await deadline.CancelAsync();
                try { await (await inbound).DisposeAsync(); }
                catch (OperationCanceledException) when (pendingAccept && !token.IsCancellationRequested) { }
                catch (Exception error) when (IsConnectionFailure(error)) { hostFailure = error; }
                Console.WriteLine($"TRACE {phase} attempt {attempt}/{maxAttempts}: descriptor={descriptor?.Events}; " +
                    $"host slot 1 [{firstHostTrace}]; host slot 2 [{secondHostTrace}]; client [{clientTrace}].");
            }
            string result = $"{phase} attempt {attempt}/{maxAttempts}: {stage} failed ({FailureCode(exchangeFailure)}).";
            if (hostFailure is not null && !ReferenceEquals(hostFailure, exchangeFailure))
                result += $" Host outcome: {FailureCode(hostFailure)}.";
            if (attempt == maxAttempts || token.IsCancellationRequested || transport.Lifetime.IsCancellationRequested ||
                exchangeFailure is AuthenticationException || hostFailure is AuthenticationException)
                throw new CheckFailed(result);
            Console.WriteLine($"RETRY {result}");
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
    }

    private static bool IsConnectionFailure(Exception error) =>
        error is IOException or SocketException or OperationCanceledException or AuthenticationException;

    private static string FailureCode(Exception? error) => error switch
    {
        Socks5ConnectException rejected => $"SOCKS5 reply 0x{rejected.ReplyCode:X2}",
        OnionSocksTimeoutException timeout => $"{timeout.Phase} deadline elapsed",
        PeerHandshakeTimeoutException timeout => $"{timeout.Phase} deadline elapsed",
        SocketException socket => $"socket error {socket.SocketErrorCode}",
        null => "no fault observed",
        _ => error.GetType().Name
    };

    private sealed class HandshakeTiming
    {
        private static readonly PeerHandshakeStage[] Stages = Enum.GetValues<PeerHandshakeStage>();
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly long[] ticks = new long[Stages.Length];

        internal void Record(PeerHandshakeStage stage) =>
            Interlocked.CompareExchange(ref ticks[(int)stage], Stopwatch.GetTimestamp(), 0);

        public override string ToString() => string.Join(", ", Stages
            .Select(stage => (stage, at: Interlocked.Read(ref ticks[(int)stage])))
            .Where(item => item.at != 0)
            .OrderBy(item => item.at)
            .Select(item => $"{item.stage}@{(long)Stopwatch.GetElapsedTime(started, item.at).TotalMilliseconds}ms"));
    }

    private static async Task StopVerifiedAsync(IPeerTorService service, int pid, CancellationToken token)
    {
        await service.StopAsync(token);
        Check.That(!ProcessAlive(pid) && service.Snapshot.CurrentTask?.RawExitCode == 0,
            "An owned Tor process did not report controlled shutdown with exit code 0.");
    }
}
