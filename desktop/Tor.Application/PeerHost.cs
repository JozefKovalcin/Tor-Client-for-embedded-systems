using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
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
        CancellationToken token = default, PeerProtocol protocol = PeerProtocol.Transient,
        Func<int, CancellationToken, Task<OnionAddress>>? register = null)
    {
        var pin = PeerIdentity.ParseFingerprint(remoteFingerprint);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, tor.Lifetime);
        var listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        try
        {
            listener.Start(2);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var address = await (register ?? tor.HostAsync)(port, operation.Token).ConfigureAwait(false);
            return new(tor, listener, local, pin, address, protocol);
        }
        catch { listener.Stop(); throw; }
    }

    public Task WaitForPublicationAsync(CancellationToken token) => tor.WaitForPublicationAsync(Address, token);

    public Task<PeerConnection> AcceptAsync(CancellationToken token = default) => AcceptAsync(null, null, token);

    internal async Task<PeerConnection> AcceptAsync(Action<PeerHandshakeStage>? firstTrace,
        Action<PeerHandshakeStage>? secondTrace, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref accepting, 1) != 0) throw new InvalidOperationException("A peer accept is already pending.");
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, hostToken);
            var firstAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = AcceptOneAsync(firstTrace, firstAccepted, operation.Token);
            var second = AcceptOneAsync(secondTrace, secondAccepted, operation.Token);
            var completed = await Task.WhenAny(first, second).ConfigureAwait(false);
            var other = completed == first ? second : first;
            var otherAccepted = completed == first ? secondAccepted : firstAccepted;
            if (completed.IsFaulted && otherAccepted.Task.IsCompletedSuccessfully && !operation.IsCancellationRequested)
                (completed, other) = (other, completed);
            PeerConnection? winner = null;
            try { return winner = await completed.ConfigureAwait(false); }
            finally
            {
                try
                {
                    await operation.CancelAsync().ConfigureAwait(false);
                    try { await (await other.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false); }
                    catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or
                        AuthenticationException or ObjectDisposedException) { }
                }
                catch
                {
                    if (winner is not null) await winner.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally { Volatile.Write(ref accepting, 0); }
    }

    private async Task<PeerConnection> AcceptOneAsync(Action<PeerHandshakeStage>? trace,
        TaskCompletionSource verified, CancellationToken token)
    {
        // An owned TCP stream can stall before the first TLS record. Keep one
        // additional accept slot while the first negotiates, then close the loser.
        while (true)
        {
            var accepted = await listener.AcceptSocketAsync(token).ConfigureAwait(false);
            try
            {
                if (tor is IOwnedOnionPeerListener owner &&
                    !(accepted.LocalEndPoint is IPEndPoint local && accepted.RemoteEndPoint is IPEndPoint peer &&
                      owner.OwnsIncomingConnection(local, peer)))
                {
                    accepted.Dispose();
                    trace?.Invoke(PeerHandshakeStage.HostTcpOwnerRejected);
                    continue;
                }
                verified.TrySetResult();
                trace?.Invoke(PeerHandshakeStage.HostTcpAccepted);
                return await PeerConnection.AuthenticateAsync(new NetworkStream(accepted, ownsSocket: true), identity,
                    remotePin, true, hostToken, token, protocol, trace).ConfigureAwait(false);
            }
            catch
            {
                accepted.Dispose();
                throw;
            }
        }
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
