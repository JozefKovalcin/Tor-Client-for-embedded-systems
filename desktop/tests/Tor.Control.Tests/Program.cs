using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Tor.Adapters.Windows;
using Tor.Control;
using Tor.Core;

static class Assert
{
    public static void That(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}

sealed class MockServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private TcpClient? client;
    public int Port { get; }
    public Task Serving { get; }
    public byte[] Cookie { get; } = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
    public List<string> Commands { get; } = [];
    public bool ProofReceived { get; private set; }
    public MockServer(string mode = "good")
    {
        listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; Serving = Serve(mode);
    }
    private async Task Serve(string mode)
    {
        client = await listener.AcceptTcpClientAsync();
        using var stream = client.GetStream(); using var input = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        using var output = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        byte[]? proof = null;
        while (await input.ReadLineAsync() is { } command)
        {
            // Retain only command verbs, never cookie/challenge/proof bytes.
            var verb = command.Split(' ')[0]; Commands.Add(verb);
            if (command == "PROTOCOLINFO 1")
            {
                await output.WriteLineAsync("650 STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=100 TAG=untrusted");
                await output.WriteLineAsync("250-PROTOCOLINFO 1");
                await output.WriteLineAsync(mode == "no-safe" ? "250-AUTH METHODS=COOKIE COOKIEFILE=\"untrusted-path\"" : "250-AUTH METHODS=COOKIE,SAFECOOKIE COOKIEFILE=\"untrusted-path\"");
                await output.WriteLineAsync("250 OK");
            }
            else if (command.StartsWith("AUTHCHALLENGE SAFECOOKIE ", StringComparison.Ordinal))
            {
                var nonce = Convert.FromHexString(command.Split(' ')[2]); Assert.That(nonce.Length == 32, "Client nonce length");
                var serverNonce = Enumerable.Repeat((byte)77, 32).ToArray(); var message = Cookie.Concat(nonce).Concat(serverNonce).ToArray();
                var hash = HMACSHA256.HashData(Encoding.ASCII.GetBytes("Tor safe cookie authentication server-to-controller hash"), message);
                proof = HMACSHA256.HashData(Encoding.ASCII.GetBytes("Tor safe cookie authentication controller-to-server hash"), message);
                if (mode == "bad-hash") hash[0] ^= 1;
                var text = "250 AUTHCHALLENGE SERVERHASH=" + Convert.ToHexString(hash) + " SERVERNONCE=" + Convert.ToHexString(serverNonce);
                if (mode == "duplicate-nonce") text += " SERVERNONCE=" + Convert.ToHexString(serverNonce);
                await output.WriteLineAsync(text);
            }
            else if (verb == "AUTHENTICATE")
            {
                ProofReceived = true;
                Assert.That(proof is not null && CryptographicOperations.FixedTimeEquals(proof, Convert.FromHexString(command.Split(' ')[1])), "Controller proof incorrect");
                await output.WriteLineAsync(mode == "deny-proof" ? "515 Authentication failed sensitive-data-must-not-escape" : "250 OK");
            }
            else if (verb == "SETEVENTS")
            {
                await output.WriteLineAsync("650-STATUS_CLIENT NOTICE BOOTSTRAP TAG=conn PROGRESS=10 FUTURE=value");
                await output.WriteLineAsync("650 EXTRA=ignored"); await output.WriteLineAsync("250 OK");
            }
            else if (verb == "GETINFO")
            {
                if (mode == "cancel") { await Task.Delay(500); return; }
                if (mode == "close") return;
                await output.WriteLineAsync("650 STATUS_CLIENT NOTICE BOOTSTRAP PROGRESS=31 TAG=handshake");
                await output.WriteLineAsync("250+status/bootstrap-phase=");
                await output.WriteLineAsync("NOTICE BOOTSTRAP PROGRESS=42 TAG=loading SUMMARY=\"Sensitive summary not exported\"");
                await output.WriteLineAsync("..dot-stuffed"); await output.WriteLineAsync("."); await output.WriteLineAsync("250 OK");
            }
            else if (verb is "SIGNAL" or "TAKEOWNERSHIP") await output.WriteLineAsync("250 OK");
            else throw new Exception("Unexpected command verb");
        }
    }
    public async ValueTask DisposeAsync()
    {
        client?.Dispose(); listener.Stop();
        try { await Serving.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException) { }
        CryptographicOperations.ZeroMemory(Cookie);
    }
}

static class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--owned-child")
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            await using var childSession = await ManagedTorSession.StartOfflineAsync(new PinnedWindowsRuntime(args[1]), args[2]);
            await File.WriteAllTextAsync(args[3], childSession.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await Task.Delay(Timeout.Infinite); return;
        }
        foreach (var (name, test) in new (string, Func<Task>)[] {
            ("bounded-multiline-and-dot-framing", Frames), ("safecookie-events-and-serialized-commands", Good),
            ("bad-server-hash-no-proof", () => Reject("bad-hash")), ("duplicate-challenge-no-proof", () => Reject("duplicate-nonce")),
            ("no-safecookie-no-cookie-fallback", () => Reject("no-safe")), ("bad-controller-auth-reply-sanitized", Denied),
            ("unauthenticated-and-nonloopback-rejected", Unauthorized), ("cancelled-command-fails-closed", Cancel),
            ("unexpected-control-eof", UnexpectedClose) })
        { await test(); Console.WriteLine("PASS " + name); }
        Console.WriteLine("9 control behavior groups PASS");
        if (args.Length == 3 && args[0] == "--windows-smoke")
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Windows smoke");
            await Smoke(args[1], args[2]);
        }
        else Console.WriteLine("NOT RUN native Windows Tor lifecycle smoke (explicit --windows-smoke package private-parent required)");
    }
    static async Task Frames()
    {
        var text = "250+value=\r\nfirst\r\n..second\r\n.\r\n250 OK\r\n";
        var reply = await new TorReplyReader(new MemoryStream(Encoding.ASCII.GetBytes(text))).ReadAsync();
        Assert.That(reply.Code == 250 && reply.Lines.SequenceEqual(new[] { "value=", "first", ".second", "OK" }), "Multiline framing lost data");
        foreach (var invalid in new[] { "250 OK\n", "250-hello\r\n550 wrong\r\n", "250+value=\r\nmissing terminator\r\n", "oops\r\n", new string('x', 8193) + "\r\n" })
            await Assert.Throws<TorControlException>(() => new TorReplyReader(new MemoryStream(Encoding.ASCII.GetBytes(invalid))).ReadAsync());
    }
    static async Task Good()
    {
        await using var server = new MockServer(); await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await client.AuthenticateAsync(server.Cookie); Assert.That(client.LatestBootstrap is null, "Unauthenticated progress was trusted");
        await client.SubscribeBootstrapAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = client.WatchBootstrapAsync(timeout.Token).GetAsyncEnumerator();
        Assert.That(await events.MoveNextAsync() && events.Current == new BootstrapProgress(10, "conn"), "Async multiline event consumed command reply");
        var values = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetBootstrapAsync()));
        Assert.That(values.All(x => x == new BootstrapProgress(42, "loading")), "Concurrent command replies misassociated");
        Assert.That(client.LatestBootstrap == new BootstrapProgress(42, "loading"), "Latest bootstrap lost");
        await client.TakeOwnershipAsync(); await client.ShutdownAsync();
    }
    static async Task Reject(string mode)
    {
        await using var server = new MockServer(mode); await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await Assert.Throws<TorControlException>(() => client.AuthenticateAsync(server.Cookie));
        await server.Serving.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(!server.ProofReceived && client.LatestBootstrap is null, "Unverified server received proof or published trusted events");
    }
    static async Task Denied()
    {
        await using var server = new MockServer("deny-proof"); await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        try { await client.AuthenticateAsync(server.Cookie); throw new Exception("Expected auth rejection"); }
        catch (TorControlException e) { Assert.That(!e.Message.Contains("sensitive", StringComparison.OrdinalIgnoreCase), "Server auth text leaked"); }
    }
    static async Task Unauthorized()
    {
        await Assert.Throws<ArgumentException>(() => TorControlClient.ConnectAsync(IPAddress.Parse("192.0.2.1"), 9051));
        await using var server = new MockServer(); await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await Assert.Throws<TorControlException>(() => client.GetBootstrapAsync());
        Assert.That(server.Commands.Count == 0, "Unauthenticated command reached the server");
    }
    static async Task Cancel()
    {
        await using var server = new MockServer("cancel"); await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await client.AuthenticateAsync(server.Cookie); using var timeout = new CancellationTokenSource(30);
        await Assert.Throws<OperationCanceledException>(() => client.GetBootstrapAsync(timeout.Token));
        try { await client.GetBootstrapAsync(); throw new Exception("Cancelled connection reused"); }
        catch (Exception e) when (e is TorControlException or OperationCanceledException or ObjectDisposedException or System.Threading.Channels.ChannelClosedException) { }
    }
    static async Task UnexpectedClose()
    {
        await using var server = new MockServer("close"); await using var client = await TorControlClient.ConnectAsync(IPAddress.Loopback, server.Port);
        await client.AuthenticateAsync(server.Cookie);
        try { await client.GetBootstrapAsync(); throw new Exception("EOF accepted as response"); }
        catch (Exception e) when (e is TorControlException or OperationCanceledException or System.Threading.Channels.ChannelClosedException) { }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static async Task Smoke(string package, string parent)
    {
        if (!Directory.Exists(parent)) PrivateStorage.CreateNewDirectory(parent);
        var runtime = new PinnedWindowsRuntime(package);
        int pid;
        await using (var session = await ManagedTorSession.StartOfflineAsync(runtime, parent))
        {
            pid = session.ProcessId; Assert.That(!session.Completion.IsCompleted, "Tor exited immediately");
            Assert.That(session.Control.LatestBootstrap is { Percent: 0 }, "Offline bootstrap status not observed");
            using (var unauthorized = new TcpClient())
            {
                await unauthorized.ConnectAsync(session.Control.Endpoint.Address, session.Control.Endpoint.Port);
                await unauthorized.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GETINFO status/bootstrap-phase\r\n"));
                using var deniedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var denied = await new TorReplyReader(unauthorized.GetStream()).ReadAsync(deniedTimeout.Token);
                Assert.That(denied.Code == 514, "Actual Tor accepted unauthenticated control access");
            }
            Console.WriteLine("PASS actual Tor rejects raw unauthenticated GETINFO with514");
            var result = await session.StopAsync(); Assert.That(result.RawExitCode == 0, "Authenticated clean shutdown failed");
        }
        Assert.That(!Alive(pid), "Managed Tor survived stop"); Console.WriteLine("PASS native Windows offline SAFECOOKIE startup/bootstrap0/shutdown; raw exit0; no surviving process");
        await using (var session = await ManagedTorSession.StartOfflineAsync(runtime, parent))
        {
            pid = session.ProcessId; Process.GetProcessById(pid).Kill();
            var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(!result.StopRequested, "Unexpected process termination mislabeled as managed stop");
        }
        Assert.That(!Alive(pid), "Unexpectedly terminated Tor still alive"); Console.WriteLine("PASS native Windows unexpected exit detection and cleanup");
        var files = new VerifiedFileStore(package); var journalRoot = Path.Combine(parent, "core-journal-" + Guid.NewGuid().ToString("N")); PrivateStorage.CreateNewDirectory(journalRoot);
        var handler = new WindowsTorTaskHandler(runtime, parent);
        await using (var core = new TaskOrchestrator([handler], files, new TaskJournal(journalRoot), new(1, 1024 * 1024 * 1024, 1024 * 1024 * 1024, false)))
        {
            core.Submit([new("offline-tor", new WindowsTorSessionCommand(PinnedWindowsRuntime.Executable, [PinnedWindowsRuntime.ThreadLibrary]), [], new(1, 1, 1, false))]);
            var running = core.RunUntilIdleAsync();
            var limit = Stopwatch.StartNew();
            while (core.Snapshot().Single().Completed is null && !running.IsCompleted && limit.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(40);
            Assert.That(core.Snapshot().Single().Completed == 0, "Typed Tor handler did not emit observed bootstrap");
            core.Cancel("offline-tor"); await running.WaitAsync(TimeSpan.FromSeconds(10));
            var result = core.Snapshot().Single(); Assert.That(result.State == "cancelled" && result.RawExitCode == 0, "Cancellation lost actual Tor exit");
        }
        Console.WriteLine("PASS native Windows core adapter cancellation awaits Tor teardown and preserves raw exit0");
        var marker = Path.Combine(parent, "crash-marker-" + Guid.NewGuid().ToString("N") + ".txt");
        var childStart = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            childStart.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in new[] { "--owned-child", package, parent, marker }) childStart.ArgumentList.Add(argument);
        using (var owner = Process.Start(childStart) ?? throw new Exception("Owned crash helper did not start"))
        {
            var outDrain = owner.StandardOutput.ReadToEndAsync(); var errDrain = owner.StandardError.ReadToEndAsync();
            int? ownedPid = null;
            try
            {
                var wait = Stopwatch.StartNew();
                while (!File.Exists(marker) && !owner.HasExited && wait.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(40);
                Assert.That(File.Exists(marker), "Owned crash helper did not authenticate Tor");
                ownedPid = int.Parse(await File.ReadAllTextAsync(marker), System.Globalization.CultureInfo.InvariantCulture);
                owner.Kill(entireProcessTree: false); // Simulate application death, not an explicit child-tree termination.
                await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                wait.Restart(); while (Alive(ownedPid.Value) && wait.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(40);
                Assert.That(!Alive(ownedPid.Value), "Tor survived unexpected owning-application death");
            }
            finally
            {
                if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); }
                if (ownedPid is { } childPid && Alive(childPid))
                { using var orphan = Process.GetProcessById(childPid); orphan.Kill(); await orphan.WaitForExitAsync(); }
                await Task.WhenAll(outDrain, errDrain); // Never print child raw diagnostics or private configuration paths.
            }
        }
        Console.WriteLine("PASS native Windows owning-application crash leaves no Tor orphan (kill-on-close job/owned control)");
        var unverified = Path.Combine(parent, "unverified-package-" + Guid.NewGuid().ToString("N"));
        PrivateStorage.CreateNewDirectory(unverified);
        await File.WriteAllTextAsync(Path.Combine(unverified, "tor.exe"), "synthetic non-executable hash mismatch fixture");
        int before = Directory.GetDirectories(parent, "session-*").Length;
        await Assert.Throws<InvalidDataException>(() => ManagedTorSession.StartOfflineAsync(new PinnedWindowsRuntime(unverified), parent));
        Assert.That(Directory.GetDirectories(parent, "session-*").Length == before, "Unverified runtime created managed state");
        Console.WriteLine("PASS unverified runtime hash rejected before process/state creation");
        var neighbors = Path.Combine(parent, "package-neighbors-" + Guid.NewGuid().ToString("N"));
        PrivateStorage.CreateNewDirectory(neighbors);
        foreach (var file in new[] { PinnedWindowsRuntime.Executable.Ref, PinnedWindowsRuntime.ThreadLibrary.Ref })
            File.Copy(Path.Combine(package, file), Path.Combine(neighbors, file));
        await File.WriteAllTextAsync(Path.Combine(neighbors, "version.dll"), "harmless synthetic non-PE DLL fixture; never executed");
        await File.WriteAllTextAsync(Path.Combine(neighbors, "tor.exe.manifest"), "harmless synthetic ignored external manifest fixture");
        Directory.CreateDirectory(Path.Combine(neighbors, "tor.exe.local"));
        await File.WriteAllTextAsync(Path.Combine(neighbors, "tor.exe.local/unlisted.dll"), "harmless synthetic redirection fixture; never executed");
        await using (var isolated = await ManagedTorSession.StartOfflineAsync(new PinnedWindowsRuntime(neighbors), parent))
        {
            using var observed = Process.GetProcessById(isolated.ProcessId);
            var executable = observed.MainModule?.FileName ?? throw new Exception("Managed executable location unavailable");
            var executionDirectory = Path.GetDirectoryName(executable)!;
            Assert.That(!executable.StartsWith(neighbors + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Original package directory remained executable search scope");
            Assert.That(Directory.GetFileSystemEntries(executionDirectory).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(new[] { "libwinpthread-1.dll", "tor.exe" }, StringComparer.OrdinalIgnoreCase), "Unverified package neighbor/redirection reached private execution directory");
            foreach (ProcessModule module in observed.Modules)
                Assert.That(!module.FileName.StartsWith(neighbors + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "A module loaded from unverified package directory");
            Assert.That((await isolated.StopAsync()).RawExitCode == 0, "Isolated staged Tor shutdown failed");
        }
        Assert.That(File.Exists(Path.Combine(neighbors, "version.dll")) && Directory.Exists(Path.Combine(neighbors, "tor.exe.local")), "Source package neighbors were unexpectedly modified");
        Console.WriteLine("PASS unlisted DLL/external manifest/.local neighbors excluded from private verified execution and loaded modules");
        var contents = Directory.GetFiles(parent, "*.json", SearchOption.AllDirectories)
            .Select(File.ReadAllText).ToArray();
        Assert.That(contents.All(x => !x.Contains("AUTHENTICATE ", StringComparison.Ordinal) && !x.Contains("SERVERNONCE=", StringComparison.Ordinal)), "Control authentication material was persisted");
    }
    static bool Alive(int pid)
    { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }
}
