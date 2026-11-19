using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Tor.Control;

public enum NewIdentityResult { Applied, Submitted, RateLimited }
public sealed record TorOnlineStatus(string? TorVersion, bool? CircuitEstablished, IReadOnlyList<IPEndPoint> SocksListeners);

/// <summary>Loopback-only, SAFECOOKIE-only client with one reply reader and serialized typed commands.</summary>
public sealed class TorControlClient : IAsyncDisposable
{
    private const string BootstrapKey = "status/bootstrap-phase=";
    private const string ClientStatusPrefix = "STATUS_CLIENT ";
    private static readonly TimeSpan NewIdentityInterval = TimeSpan.FromSeconds(10);
    private static readonly Regex BootstrapField = new("\\G *(?<key>[A-Za-z0-9_-]+)=(?:\"(?:[^\"\\\\]|\\\\.)*\"|(?<value>[^\\s\"]+))(?= |$)", RegexOptions.CultureInvariant);
    private static readonly Regex Version = new(@"\A(?:Tor )?(?<version>[0-9]{1,3}(?:\.[0-9]{1,3}){2,3}(?:-[A-Za-z0-9.-]{1,24})?)\z", RegexOptions.CultureInvariant);
    private readonly TcpClient socket;
    private readonly NetworkStream stream;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim transactions = new(1, 1);
    private readonly SemaphoreSlim authentication = new(1, 1);
    private readonly SemaphoreSlim identityRequests = new(1, 1);
    private readonly SemaphoreSlim descriptorRequests = new(1, 1);
    private readonly Channel<ControlReply> replies = Channel.CreateBounded<ControlReply>(1);
    private readonly Channel<BootstrapProgress> events = Channel.CreateBounded<BootstrapProgress>(new BoundedChannelOptions(64)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true, SingleReader = false });
    private readonly Channel<OnionAddress> onionUploads = Channel.CreateBounded<OnionAddress>(new BoundedChannelOptions(16)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true, SingleReader = true });
    private readonly Task receive;
    private BootstrapProgress? latest;
    private long? lastIdentityRequest;
    private TaskCompletionSource? pendingIdentitySignal;
    private DescriptorRefresh? pendingDescriptor;
    private OnionDescriptorObservation? descriptorObservation;
    private volatile bool authenticated;
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
    public Task SubscribeOnlineAsync(CancellationToken token = default) => CommandAsync("SETEVENTS STATUS_CLIENT SIGNAL", true, token);
    public Task SubscribeOnionPublicationAsync(CancellationToken token = default) =>
        CommandAsync("SETEVENTS STATUS_CLIENT SIGNAL HS_DESC", true, token, drainAfterAdmission: true);

    private sealed record DescriptorRefresh(OnionAddress Address, TaskCompletionSource Received);

    public async Task<OnionDescriptorObservation> ObserveDescriptorAsync(OnionAddress address, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        var observation = new OnionDescriptorObservation(address,
            current => Interlocked.CompareExchange(ref descriptorObservation, null, current));
        if (Interlocked.CompareExchange(ref descriptorObservation, observation, null) is not null)
            throw new InvalidOperationException("A descriptor observation is already active.");
        try
        {
            await SubscribeOnionPublicationAsync(token).ConfigureAwait(false);
            return observation;
        }
        catch
        {
            observation.Dispose();
            throw;
        }
    }

    public async Task<bool> RefreshOnionDescriptorAsync(OnionAddress address, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await descriptorRequests.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            await SubscribeOnionPublicationAsync(operation.Token).ConfigureAwait(false);
            var pending = new DescriptorRefresh(address, new(TaskCreationOptions.RunContinuationsAsynchronously));
            Volatile.Write(ref pendingDescriptor, pending);
            await CommandAsync("HSFETCH " + address.ServiceId, true, operation.Token, drainAfterAdmission: true)
                .ConfigureAwait(false);
            try
            {
                await pending.Received.Task.WaitAsync(TimeSpan.FromSeconds(15), operation.Token).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException) { return false; }
        }
        finally
        {
            Volatile.Write(ref pendingDescriptor, null);
            descriptorRequests.Release();
        }
    }

    public async Task<OnionAddress> AddOnionAsync(int loopbackPort, CancellationToken token = default) =>
        (await RegisterOnionAsync(loopbackPort, "NEW:ED25519-V3 Flags=DiscardPK", false, null, token)
            .ConfigureAwait(false)).Address;

    public async Task<(OnionAddress Address, OnionServiceKey Key)> GenerateOnionAsync(int loopbackPort, CancellationToken token = default)
    {
        var result = await RegisterOnionAsync(loopbackPort, "NEW:ED25519-V3", true, null, token).ConfigureAwait(false);
        return (result.Address, result.Key!);
    }

    public async Task<OnionAddress> RestoreOnionAsync(int loopbackPort, OnionAddress address,
        OnionServiceKey key, CancellationToken token = default)
    {
        if (!authenticated) throw new TorControlException("Control authentication required");
        return (await RegisterOnionAsync(loopbackPort, key.ControlSpec(), false, address, token)
            .ConfigureAwait(false)).Address;
    }

    private async Task<(OnionAddress Address, OnionServiceKey? Key)> RegisterOnionAsync(int loopbackPort,
        string keySpec, bool capture, OnionAddress? expected, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(loopbackPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(loopbackPort, 65535);
        token.ThrowIfCancellationRequested();
        // Drain once admitted. If the owner cancelled during creation, remove the service before returning.
        var command = FormattableString.Invariant($"ADD_ONION {keySpec} MaxStreams=4 Port=80,127.0.0.1:{loopbackPort}");
        var reply = await CommandAsync(command, true, token, drainAfterAdmission: true).ConfigureAwait(false);
        OnionAddress address;
        try { address = OnionAddress.FromServiceId(ReadValue(reply, "ServiceID")); }
        catch (Exception error) when (error is FormatException or TorControlException)
        {
            CloseTransport(); // A service with an unknown identifier must die with its control connection.
            throw new TorControlException("Tor returned an invalid onion service identifier.");
        }
        if (token.IsCancellationRequested || (expected is not null && address != expected))
        {
            await DeleteOnionAsync(address).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            throw new TorControlException("Restored Onion Service address differs from the saved address.");
        }
        if (!capture) return (address, null);
        try { return (address, OnionServiceKey.Parse(ReadValue(reply, "PrivateKey"))); }
        catch (Exception error) when (error is InvalidDataException or TorControlException)
        {
            await DeleteOnionAsync(address).ConfigureAwait(false);
            throw new TorControlException("Tor returned an invalid Onion Service key.");
        }
    }

    public Task DeleteOnionAsync(OnionAddress address) => CommandAsync("DEL_ONION " + address.ServiceId, true, CancellationToken.None);

    public async Task WaitForOnionPublicationAsync(OnionAddress address, CancellationToken token)
    {
        await foreach (var uploaded in onionUploads.Reader.ReadAllAsync(token).ConfigureAwait(false))
            if (uploaded == address) return;
        throw new TorControlException("Control disconnected before onion publication was observed.");
    }
    public Task TakeOwnershipAsync(CancellationToken token = default) => CommandAsync("TAKEOWNERSHIP", true, token);
    public Task ShutdownAsync(CancellationToken token = default) => CommandAsync("SIGNAL SHUTDOWN", true, token);
    public Task EnableNetworkAsync(CancellationToken token = default) => CommandAsync("SETCONF DisableNetwork=0", true, token);

    public async Task<BootstrapProgress?> GetBootstrapAsync(CancellationToken token = default)
    {
        var reply = await CommandAsync("GETINFO status/bootstrap-phase", true, token).ConfigureAwait(false);
        return ParseBootstrap(reply);
    }
    public IAsyncEnumerable<BootstrapProgress> WatchBootstrapAsync(CancellationToken token = default) => events.Reader.ReadAllAsync(token);

    public async Task<TorOnlineStatus> GetOnlineStatusAsync(CancellationToken token = default)
    {
        var status = await CommandAsync("GETINFO version net/listeners/socks", true, token).ConfigureAwait(false);
        var version = Version.Match(ReadValue(status, "version"));
        var listeners = ReadSocksListeners(ReadValue(status, "net/listeners/socks"));
        var circuit = await CommandAsync("GETINFO status/circuit-established", true, token, allowUnknownKey: true).ConfigureAwait(false);
        bool? established = circuit.Code == 552 ? null : ReadValue(circuit, "status/circuit-established") switch
        {
            "0" => false,
            "1" => true,
            _ => throw new TorControlException("Malformed circuit status")
        };
        return new(version.Success ? version.Groups["version"].Value : null, established, listeners);
    }

    /// <summary>Reports protocol acknowledgement separately from an observed NEWNYM event.</summary>
    public async Task<NewIdentityResult> RequestNewIdentityAsync(CancellationToken token = default)
    {
        await identityRequests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!authenticated) throw new TorControlException("Control authentication required");
            ObjectDisposedException.ThrowIf(disposed, this);
            if (lifetime.IsCancellationRequested) throw new TorControlException("Control connection closed");
            if (lastIdentityRequest is { } last && Stopwatch.GetElapsedTime(last) < NewIdentityInterval)
                return NewIdentityResult.RateLimited;

            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref pendingIdentitySignal, observed);
            try
            {
                await CommandAsync("SIGNAL NEWNYM", true, token, drainAfterAdmission: true).ConfigureAwait(false);
                lastIdentityRequest = Stopwatch.GetTimestamp();
                token.ThrowIfCancellationRequested();
                try
                {
                    await observed.Task.WaitAsync(TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
                    return NewIdentityResult.Applied;
                }
                catch (TimeoutException) { return NewIdentityResult.Submitted; }
            }
            finally { Volatile.Write(ref pendingIdentitySignal, null); }
        }
        finally { identityRequests.Release(); }
    }

    private static string ReadValue(ControlReply reply, string key)
    {
        var prefix = key + "=";
        var matches = reply.Lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new TorControlException("Missing or repeated online status field");
        return matches[0][prefix.Length..];
    }

    private static IReadOnlyList<IPEndPoint> ReadSocksListeners(string value)
    {
        var listeners = new List<IPEndPoint>();
        foreach (var item in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (item.Length < 3 || item[0] != '"' || item[^1] != '"' ||
                !IPEndPoint.TryParse(item.AsSpan(1, item.Length - 2), out var listener) ||
                !(listener.Address.Equals(IPAddress.Loopback) || listener.Address.Equals(IPAddress.IPv6Loopback)) ||
                listener.Port == 0 || listeners.Contains(listener))
                throw new TorControlException("Unsafe or malformed SOCKS listener reported by Tor");
            listeners.Add(listener);
        }
        return listeners.AsReadOnly();
    }

    private async Task<ControlReply> CommandAsync(string command, bool requiresAuthentication, CancellationToken token,
        bool allowUnknownKey = false, bool drainAfterAdmission = false)
    {
        if (requiresAuthentication && !authenticated) throw new TorControlException("Control authentication required");
        ObjectDisposedException.ThrowIf(disposed, this);
        await transactions.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Once NEWNYM is admitted, drain its bounded reply so canceling the UI action cannot break shutdown.
            if (drainAfterAdmission) token.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                drainAfterAdmission ? CancellationToken.None : token, lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var bytes = Encoding.ASCII.GetBytes(command + "\r\n");
                try { await stream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
                var reply = await replies.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
                if (reply.Code != 250 && !(allowUnknownKey && reply.Code == 552))
                    throw new TorControlException("Control command rejected with code " + reply.Code);
                return reply;
            }
            catch { CloseTransport(); throw; } // An abandoned reply must never be mistaken for a later command's result.
        }
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
                    if (reply.Lines[0] == "SIGNAL NEWNYM" || reply.Lines[0].StartsWith("SIGNAL NEWNYM ", StringComparison.Ordinal))
                        Volatile.Read(ref pendingIdentitySignal)?.TrySetResult();
                    const string uploaded = "HS_DESC UPLOADED ";
                    if (reply.Lines[0].StartsWith(uploaded, StringComparison.Ordinal))
                    {
                        var id = reply.Lines[0][uploaded.Length..].Split(' ', 2)[0];
                        try { onionUploads.Writer.TryWrite(OnionAddress.FromServiceId(id)); }
                        catch (FormatException) { }
                    }
                    const string received = "HS_DESC RECEIVED ";
                    if (reply.Lines[0].StartsWith(received, StringComparison.Ordinal) &&
                        Volatile.Read(ref pendingDescriptor) is { } pending &&
                        reply.Lines[0][received.Length..].Split(' ', 2)[0] == pending.Address.ServiceId)
                        pending.Received.TrySetResult();
                    if (Volatile.Read(ref descriptorObservation) is { } observation)
                        ObserveDescriptorEvent(reply.Lines[0], observation);
                }
                else
                {
                    // Apply query results in wire order relative to asynchronously received progress.
                    if (authenticated && ParseBootstrap(reply) is { } progress) Volatile.Write(ref latest, progress);
                    if (!replies.Writer.TryWrite(reply)) throw new TorControlException("Unexpected or excess command reply");
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            replies.Writer.TryComplete(new TorControlException("Control connection ended"));
        }
        finally { CloseTransport(); events.Writer.TryComplete(); onionUploads.Writer.TryComplete(); }
    }
    private static void ObserveDescriptorEvent(string line, OnionDescriptorObservation observation)
    {
        const string prefix = "HS_DESC ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return;
        var fields = line[prefix.Length..].Split(' ', 3);
        if (fields.Length != 3 || fields[1] != observation.Address.ServiceId) return;
        observation.Record(fields[0] switch
        {
            "REQUESTED" => OnionDescriptorEvents.Requested,
            "RECEIVED" => OnionDescriptorEvents.Received,
            "FAILED" => OnionDescriptorEvents.Failed,
            "IGNORE" => OnionDescriptorEvents.Ignored,
            _ => OnionDescriptorEvents.None
        });
    }

    private static BootstrapProgress? ParseBootstrap(ControlReply reply)
    {
        string text = reply.Lines[0];
        if (reply.Code == 650 && text.StartsWith(ClientStatusPrefix, StringComparison.Ordinal))
            text = text[ClientStatusPrefix.Length..];
        else if (reply.Code == 250 && text.StartsWith(BootstrapKey, StringComparison.Ordinal))
            text = text.Length == BootstrapKey.Length && reply.Lines.Count > 1 ? reply.Lines[1] : text[BootstrapKey.Length..];
        else return null;

        bool warning = text.StartsWith("WARN BOOTSTRAP ", StringComparison.Ordinal);
        if (!warning && !text.StartsWith("NOTICE BOOTSTRAP ", StringComparison.Ordinal)) return null;
        var fields = text[(warning ? "WARN BOOTSTRAP ".Length : "NOTICE BOOTSTRAP ".Length)..];
        int? percent = null;
        string? tag = null, reason = null;
        foreach (Match field in BootstrapField.Matches(fields))
        {
            var value = field.Groups["value"].Value;
            switch (field.Groups["key"].Value)
            {
                case "PROGRESS":
                    if (percent is not null || value.Length is < 1 or > 3 || !value.All(char.IsAsciiDigit) ||
                        !int.TryParse(value, out var parsed) || parsed > 100) return null;
                    percent = parsed;
                    break;
                case "TAG":
                    if (tag is not null || value.Length is < 1 or > 64 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return null;
                    tag = value;
                    break;
                case "REASON":
                    reason = value;
                    break;
            }
        }
        if (percent is null || tag is null) return null;
        BootstrapWarning? problem = warning ? reason switch
        {
            "CONNECTREFUSED" => BootstrapWarning.ConnectionRefused,
            "TIMEOUT" => BootstrapWarning.ConnectionTimeout,
            "NOROUTE" => BootstrapWarning.NoRoute,
            "IOERROR" => BootstrapWarning.IoError,
            _ => BootstrapWarning.Other
        } : null;
        return new(percent.Value, tag, problem);
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
