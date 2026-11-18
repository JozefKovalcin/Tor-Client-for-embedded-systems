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

public static class OnionSocksConnection
{
    public static async Task<Stream> ConnectAsync(IPEndPoint socks, OnionAddress remote,
        Func<IPEndPoint, bool> verifyPeer, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(verifyPeer);
        if (!socks.Address.Equals(IPAddress.Loopback) || socks.Port == 0)
            throw new ArgumentException("Expected an owned IPv4 loopback SOCKS endpoint.", nameof(socks));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(socks, deadline.Token).ConfigureAwait(false);
            if (!verifyPeer((IPEndPoint)socket.LocalEndPoint!))
                throw new IOException("The SOCKS connection does not belong to the active Tor process.");
            var stream = new NetworkStream(socket, ownsSocket: true);
            await Socks5ConnectivityProbe.NegotiateAsync(stream, deadline.Token).ConfigureAwait(false);
            await Socks5ConnectivityProbe.RequestTunnelAsync(stream, remote.Host, OnionAddress.VirtualPort, deadline.Token).ConfigureAwait(false);
            return stream;
        }
        catch { socket.Dispose(); throw; }
    }
}
