using System.Net;
using System.Net.Sockets;

namespace Tor.Control;

public sealed record OnionAddress
{
    public const int VirtualPort = 80;
    public string ServiceId { get; }
    public string Host => ServiceId + ".onion";

    private OnionAddress(string id) => ServiceId = id;

    public static OnionAddress Parse(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Length != 62 || !host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Expected a v3 .onion address without a scheme or port.");
        return FromServiceId(host[..56].ToLowerInvariant());
    }

    internal static OnionAddress FromServiceId(string id)
    {
        if (id.Length != 56 || id.Any(c => c is not (>= 'a' and <= 'z' or >= '2' and <= '7')))
            throw new FormatException("Invalid v3 onion service identifier.");
        return new(id);
    }
}

public interface IOnionPeerTransport
{
    CancellationToken Lifetime { get; }
    Task<OnionAddress> HostAsync(int loopbackPort, CancellationToken token);
    Task RemoveAsync(OnionAddress address);
    Task WaitForPublicationAsync(OnionAddress address, CancellationToken token);
    Task<Stream> ConnectAsync(OnionAddress address, CancellationToken token);
}

public interface IPersistentOnionPeerTransport : IOnionPeerTransport
{
    Task<(OnionAddress Address, OnionServiceKey Key)> GenerateHostAsync(int loopbackPort, CancellationToken token);
    Task<OnionAddress> RestoreHostAsync(int loopbackPort, OnionAddress address, OnionServiceKey key, CancellationToken token);
}

public enum OnionSocksPhase { LocalProxy, OnionConnect }

public sealed class OnionSocksTimeoutException(OnionSocksPhase phase) : IOException(
    phase == OnionSocksPhase.LocalProxy ? "The local SOCKS handshake timed out." : "The Onion Service tunnel timed out.")
{
    public OnionSocksPhase Phase { get; } = phase;
}

public static class OnionSocksConnection
{
    internal static readonly TimeSpan LocalHandshakeBudget = TimeSpan.FromSeconds(8);
    // The managed Tor uses its default SocksTimeout of 120 seconds for rendezvous streams.
    internal static readonly TimeSpan OnionConnectBudget = TimeSpan.FromSeconds(130);

    public static Task<Stream> ConnectAsync(IPEndPoint socks, OnionAddress remote,
        Func<IPEndPoint, bool> verifyPeer, CancellationToken token) =>
        ConnectAsync(socks, remote, verifyPeer, LocalHandshakeBudget, OnionConnectBudget, token);

    internal static async Task<Stream> ConnectAsync(IPEndPoint socks, OnionAddress remote,
        Func<IPEndPoint, bool> verifyPeer, TimeSpan localBudget, TimeSpan onionBudget, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(verifyPeer);
        if (!socks.Address.Equals(IPAddress.Loopback) || socks.Port == 0)
            throw new ArgumentException("Expected an owned IPv4 loopback SOCKS endpoint.", nameof(socks));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(localBudget);
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var phase = OnionSocksPhase.LocalProxy;
        try
        {
            await socket.ConnectAsync(socks, deadline.Token).ConfigureAwait(false);
            if (!verifyPeer((IPEndPoint)socket.LocalEndPoint!))
                throw new IOException("The SOCKS connection does not belong to the active Tor process.");
            var stream = new NetworkStream(socket, ownsSocket: true);
            await Socks5ConnectivityProbe.NegotiateAsync(stream, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            phase = OnionSocksPhase.OnionConnect;
            deadline.CancelAfter(onionBudget);
            await Socks5ConnectivityProbe.RequestTunnelAsync(stream, remote.Host, OnionAddress.VirtualPort, deadline.Token).ConfigureAwait(false);
            return stream;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            socket.Dispose();
            throw new OnionSocksTimeoutException(phase);
        }
        catch { socket.Dispose(); throw; }
    }
}
