using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Tor.Control;

/// <summary>Loopback-only, SAFECOOKIE-only client with one reply reader and serialized typed commands.</summary>
public sealed class TorControlClient : IAsyncDisposable
{
    private readonly TcpClient socket;
    private readonly NetworkStream stream;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim transactions = new(1, 1);
    private readonly SemaphoreSlim authentication = new(1, 1);
    private readonly Channel<ControlReply> replies = Channel.CreateBounded<ControlReply>(1);
    private readonly Channel<BootstrapProgress> events = Channel.CreateBounded<BootstrapProgress>(new BoundedChannelOptions(64)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true, SingleReader = false });
    private readonly Task receive;
    private BootstrapProgress? latest;
    private bool authenticated;
    private bool authenticationAttempted;
    private bool disposed;
    private readonly IPEndPoint endpoint;
    public IPEndPoint Endpoint => new(endpoint.Address, endpoint.Port);
    private TorControlClient(TcpClient socket)
    {
        var remote = (IPEndPoint)socket.Client.RemoteEndPoint!;
        endpoint = new IPEndPoint(remote.Address, remote.Port);
        this.socket = socket; stream = socket.GetStream(); receive = ReceiveAsync();
    }
    public BootstrapProgress? LatestBootstrap => Volatile.Read(ref latest);
    public static async Task<TorControlClient> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default)
    {
        if (!IPAddress.IsLoopback(address) || port is < 1 or > 65535)
            throw new ArgumentException("Only an explicit loopback control endpoint is permitted");
        var socket = new TcpClient(address.AddressFamily);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { await socket.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false); return new TorControlClient(socket); }
        catch { socket.Dispose(); throw; }
    }
    public async Task AuthenticateAsync(ReadOnlyMemory<byte> cookie, CancellationToken cancellationToken = default)
    {
        await authentication.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[] secret = [], nonce = [], message = [], expected = [], proof = [];
        try
        {
            if (authenticationAttempted || cookie.Length != 32) throw new TorControlException("SAFECOOKIE authentication cannot proceed");
            authenticationAttempted = true;
            var info = await CommandAsync("PROTOCOLINFO 1", false, cancellationToken).ConfigureAwait(false);
            var methods = info.Lines.Select(line => Regex.Match(line, @"(?:^| )METHODS=([^ ]+)", RegexOptions.CultureInvariant))
                .Where(match => match.Success).SelectMany(match => match.Groups[1].Value.Split(',')).ToArray();
            if (!methods.Contains("SAFECOOKIE", StringComparer.Ordinal)) throw new TorControlException("SAFECOOKIE is not advertised");
            // Advertised COOKIEFILE is untrusted metadata and is never opened by the client.
            secret = cookie.ToArray(); nonce = RandomNumberGenerator.GetBytes(32);
            var answer = await CommandAsync("AUTHCHALLENGE SAFECOOKIE " + Convert.ToHexString(nonce), false, cancellationToken).ConfigureAwait(false);
            var fields = string.Join(' ', answer.Lines);
            var hash = SingleHex(fields, "SERVERHASH"); var serverNonce = SingleHex(fields, "SERVERNONCE");
            message = secret.Concat(nonce).Concat(serverNonce).ToArray();
            expected = HMACSHA256.HashData(Encoding.ASCII.GetBytes("Tor safe cookie authentication server-to-controller hash"), message);
            if (!CryptographicOperations.FixedTimeEquals(hash, expected)) throw new TorControlException("SAFECOOKIE server authentication rejected");
            proof = HMACSHA256.HashData(Encoding.ASCII.GetBytes("Tor safe cookie authentication controller-to-server hash"), message);
            await CommandAsync("AUTHENTICATE " + Convert.ToHexString(proof), false, cancellationToken).ConfigureAwait(false);
            authenticated = true;
        }
        catch { CloseTransport(); throw; }
        finally
        {
            foreach (var bytes in new[] { secret, nonce, message, expected, proof }) CryptographicOperations.ZeroMemory(bytes);
            authentication.Release();
        }
    }
    private static byte[] SingleHex(string text, string field)
    {
        var matches = Regex.Matches(text, @"(?:^| )" + field + @"=([0-9a-fA-F]{64})(?= |$)", RegexOptions.CultureInvariant);
        if (matches.Count != 1) throw new TorControlException("Malformed SAFECOOKIE challenge");
        return Convert.FromHexString(matches[0].Groups[1].Value);
    }
    public Task SubscribeBootstrapAsync(CancellationToken token = default) => CommandAsync("SETEVENTS STATUS_CLIENT", true, token);
    public Task TakeOwnershipAsync(CancellationToken token = default) => CommandAsync("TAKEOWNERSHIP", true, token);
    public Task ShutdownAsync(CancellationToken token = default) => CommandAsync("SIGNAL SHUTDOWN", true, token);
    public async Task<BootstrapProgress?> GetBootstrapAsync(CancellationToken token = default)
    {
        var reply = await CommandAsync("GETINFO status/bootstrap-phase", true, token).ConfigureAwait(false);
        var progress = ParseBootstrap(reply);
        if (progress is not null) Volatile.Write(ref latest, progress);
        return progress;
    }
    public IAsyncEnumerable<BootstrapProgress> WatchBootstrapAsync(CancellationToken token = default) => events.Reader.ReadAllAsync(token);
    private async Task<ControlReply> CommandAsync(string command, bool requiresAuthentication, CancellationToken token)
    {
        if (requiresAuthentication && !authenticated) throw new TorControlException("Control authentication required");
        ObjectDisposedException.ThrowIf(disposed, this);
        await transactions.WaitAsync(token).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var bytes = Encoding.ASCII.GetBytes(command + "\r\n");
            try { await stream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            var reply = await replies.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (reply.Code != 250) throw new TorControlException("Control command rejected with code " + reply.Code);
            return reply;
        }
        catch { CloseTransport(); throw; } // An abandoned reply must never be mistaken for a later command's result.
        finally { transactions.Release(); }
    }
    private async Task ReceiveAsync()
    {
        var parser = new TorReplyReader(stream);
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var reply = await parser.ReadAsync(lifetime.Token).ConfigureAwait(false);
                if (reply.Code == 650)
                {
                    if (!authenticated) continue; // Unauthenticated peers cannot publish trusted progress.
                    var progress = ParseBootstrap(reply);
                    if (progress is not null) { Volatile.Write(ref latest, progress); events.Writer.TryWrite(progress); }
                }
                else if (!replies.Writer.TryWrite(reply)) throw new TorControlException("Unexpected or excess command reply");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            replies.Writer.TryComplete(new TorControlException("Control connection ended"));
        }
        finally { CloseTransport(); events.Writer.TryComplete(); }
    }
    private static BootstrapProgress? ParseBootstrap(ControlReply reply)
    {
        foreach (var line in reply.Lines)
        {
            if (!line.Contains("BOOTSTRAP", StringComparison.Ordinal)) continue;
            var percent = Regex.Match(line, @"(?:^| )PROGRESS=(\d{1,3})(?= |$)", RegexOptions.CultureInvariant);
            var tag = Regex.Match(line, @"(?:^| )TAG=([A-Za-z0-9_-]{1,64})(?= |$)", RegexOptions.CultureInvariant);
            if (percent.Success && tag.Success && int.TryParse(percent.Groups[1].Value, out var value) && value <= 100)
                return new(value, tag.Groups[1].Value);
        }
        return null;
    }
    private void CloseTransport()
    {
        lifetime.Cancel(); socket.Dispose();
        replies.Writer.TryComplete(new TorControlException("Control connection closed"));
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; CloseTransport();
        await receive.ConfigureAwait(false);
        // Semaphores/lifetime remain valid for concurrent in-flight commands unwinding after transport closure.
    }
}
