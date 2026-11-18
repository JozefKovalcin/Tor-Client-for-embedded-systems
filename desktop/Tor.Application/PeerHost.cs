using System.Net;
using System.Net.Sockets;
using Tor.Control;

namespace Tor.Application;

public sealed class PeerHost : IAsyncDisposable
{
    private readonly IOnionPeerTransport tor;
    private readonly TcpListener listener;
    private readonly CancellationTokenRegistration owner;
    private readonly byte[] remotePin;
    private readonly PeerIdentity identity;
    private readonly CancellationTokenSource lifetime;
    private readonly CancellationToken hostToken;
    private readonly PeerProtocol protocol;
    private int accepting, disposed;
    public OnionAddress Address { get; }

    private PeerHost(IOnionPeerTransport tor, TcpListener listener, PeerIdentity identity, byte[] remotePin, OnionAddress address,
        PeerProtocol protocol)
    {
        this.tor = tor; this.listener = listener; this.identity = identity; this.remotePin = remotePin; Address = address;
        this.protocol = protocol;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(tor.Lifetime);
        hostToken = lifetime.Token;
        owner = hostToken.Register(static s => ((TcpListener)s!).Stop(), listener);
    }

    public static async Task<PeerHost> StartAsync(IOnionPeerTransport tor, PeerIdentity local, string remoteFingerprint,
        CancellationToken token = default, PeerProtocol protocol = PeerProtocol.Transient)
    {
        var pin = PeerIdentity.ParseFingerprint(remoteFingerprint);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, tor.Lifetime);
        var listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        try
        {
            listener.Start(2);
            var address = await tor.HostAsync(((IPEndPoint)listener.LocalEndpoint).Port, operation.Token).ConfigureAwait(false);
            return new(tor, listener, local, pin, address, protocol);
        }
        catch { listener.Stop(); throw; }
    }

    public Task WaitForPublicationAsync(CancellationToken token) => tor.WaitForPublicationAsync(Address, token);

    public async Task<PeerConnection> AcceptAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref accepting, 1) != 0) throw new InvalidOperationException("A peer accept is already pending.");
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, hostToken);
            var accepted = await listener.AcceptSocketAsync(operation.Token).ConfigureAwait(false);
            return await PeerConnection.AuthenticateAsync(new NetworkStream(accepted, ownsSocket: true), identity,
                remotePin, true, hostToken, operation.Token, protocol).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref accepting, 0); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await owner.DisposeAsync().ConfigureAwait(false);
        lifetime.Dispose();
        await tor.RemoveAsync(Address).ConfigureAwait(false);
    }
}
