using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Tor.Control;

public enum Socks5ConnectivityResult { Reachable, ProxyUnavailable, DestinationUnavailable }

/// <summary>Tests the fixed Tor Project HTTPS endpoint through a numeric local SOCKS5 listener.</summary>
public static class Socks5ConnectivityProbe
{
    private static readonly Uri Destination = new("https://check.torproject.org/api/ip");
    private const int MaxBodyBytes = 4096;

    /// <param name="endpoint">The loopback SOCKS5 listener to use for the probe.</param>
    /// <param name="token">Cancellation for the complete probe.</param>
    /// <param name="verifyPeer">If supplied, confirms ownership using the connected socket's client endpoint before any bytes are sent.</param>
    public static async Task<Socks5ConnectivityResult> CheckAsync(IPEndPoint endpoint,
        CancellationToken token = default, Func<IPEndPoint, bool>? verifyPeer = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var address = endpoint.Address.Equals(IPAddress.Loopback) ? IPAddress.Loopback :
            endpoint.Address.Equals(IPAddress.IPv6Loopback) ? IPAddress.IPv6Loopback : null;
        if (address is null || endpoint.Port == 0) throw new ArgumentException("An explicit loopback SOCKS5 endpoint is required", nameof(endpoint));
        var proxy = new IPEndPoint(address, endpoint.Port);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        bool proxyReady = false;
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            MaxResponseHeadersLength = 8,
            ConnectCallback = async (context, connectToken) =>
            {
                if (!string.Equals(context.DnsEndPoint.Host, Destination.Host, StringComparison.OrdinalIgnoreCase) || context.DnsEndPoint.Port != Destination.Port)
                    throw new IOException("Unexpected connectivity probe destination");

                var socket = new Socket(proxy.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(proxy, connectToken).ConfigureAwait(false);
                    if (verifyPeer is not null && !verifyPeer((IPEndPoint)socket.LocalEndPoint!))
                        throw new IOException("SOCKS listener ownership could not be verified");
                    var stream = new NetworkStream(socket, ownsSocket: true);
                    await NegotiateAsync(stream, connectToken).ConfigureAwait(false);
                    proxyReady = true;
                    await RequestTunnelAsync(stream, Destination.IdnHost, Destination.Port, connectToken).ConfigureAwait(false);
                    return stream;
                }
                catch { socket.Dispose(); throw; }
            }
        };
        // Prevent certificate chain/AIA and revocation fetches outside the already established tunnel.
        handler.SslOptions.CertificateChainPolicy = new()
        {
            DisableCertificateDownloads = true,
            RevocationMode = X509RevocationMode.NoCheck
        };
        using var http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        try
        {
            using var response = await http.GetAsync(Destination, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaxBodyBytes)
                return Socks5ConnectivityResult.DestinationUnavailable;

            await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var body = new byte[MaxBodyBytes + 1];
            var received = await responseStream.ReadAtLeastAsync(body, body.Length, false, timeout.Token).ConfigureAwait(false);
            if (received > MaxBodyBytes) return Socks5ConnectivityResult.DestinationUnavailable;
            using var result = JsonDocument.Parse(body.AsMemory(0, received));
            return result.RootElement.ValueKind == JsonValueKind.Object &&
                result.RootElement.TryGetProperty("IsTor", out var isTor) && isTor.ValueKind == JsonValueKind.True
                ? Socks5ConnectivityResult.Reachable : Socks5ConnectivityResult.DestinationUnavailable;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or SocketException or
            JsonException or AuthenticationException or OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return proxyReady ? Socks5ConnectivityResult.DestinationUnavailable : Socks5ConnectivityResult.ProxyUnavailable;
        }
    }

    internal static async Task NegotiateAsync(Stream stream, CancellationToken token)
    {
        await stream.WriteAsync(new byte[] { 5, 1, 0 }, token).ConfigureAwait(false);
        var reply = new byte[2];
        await stream.ReadExactlyAsync(reply, token).ConfigureAwait(false);
        if (reply[0] != 5 || reply[1] != 0) throw new IOException("SOCKS5 negotiation failed");
    }

    internal static async Task RequestTunnelAsync(Stream stream, string hostname, int port, CancellationToken token)
    {
        // ATYP=3 sends the hostname to Tor. There is no destination-side DNS call in the client.
        var host = Encoding.ASCII.GetBytes(hostname);
        var request = new byte[7 + host.Length];
        request[0] = 5;
        request[1] = 1;
        request[3] = 3;
        request[4] = checked((byte)host.Length);
        host.CopyTo(request.AsSpan(5));
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(^2), checked((ushort)port));
        await stream.WriteAsync(request, token).ConfigureAwait(false);

        var reply = new byte[4];
        await stream.ReadExactlyAsync(reply, token).ConfigureAwait(false);
        if (reply[0] != 5 || reply[1] != 0 || reply[2] != 0) throw new IOException("SOCKS5 CONNECT failed");
        int boundLength = reply[3] switch
        {
            1 => 4,
            3 => await ReadLengthAsync(stream, token).ConfigureAwait(false),
            4 => 16,
            _ => throw new IOException("Malformed SOCKS5 CONNECT reply")
        };
        await stream.ReadExactlyAsync(new byte[boundLength + 2], token).ConfigureAwait(false);
    }

    private static async Task<int> ReadLengthAsync(Stream stream, CancellationToken token)
    {
        var length = new byte[1];
        await stream.ReadExactlyAsync(length, token).ConfigureAwait(false);
        if (length[0] == 0) throw new IOException("Malformed SOCKS5 bound address");
        return length[0];
    }
}
