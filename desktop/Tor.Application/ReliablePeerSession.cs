using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Tor.Application;

/// <summary>Retries a durable outbox on a freshly authenticated v2 connection.</summary>
public sealed class ReliablePeerSession
{
    private readonly PeerConnection peer;
    private readonly PeerDeliveryStore store;
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly ConcurrentDictionary<Guid, byte> sent = new();
    private readonly Channel<byte> change = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Action<PeerDeliverySnapshot> observe;
    private int started;

    public ReliablePeerSession(PeerConnection peer, PeerDeliveryStore store, Action<PeerDeliverySnapshot> observe)
    {
        if (peer.Protocol != PeerProtocol.Reliable) throw new ArgumentException("Reliable peer protocol is required.");
        if (store.LocalFingerprint != peer.LocalFingerprint || store.RemoteFingerprint != peer.RemoteFingerprint)
            throw new ArgumentException("The encrypted queue must match the authenticated pair.");
        this.peer = peer;
        this.store = store;
        this.observe = observe;
    }

    public void Wake() => change.Writer.TryWrite(0);

    public async Task RunAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("A delivery session can only run once.");
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(token);
        var reads = ReadAsync(scope.Token);
        var writes = WritePendingAsync(scope.Token);
        var outcome = await Task.WhenAny(reads, writes).ConfigureAwait(false);
        try { await outcome.ConfigureAwait(false); }
        catch (Exception error) when (token.IsCancellationRequested && IsStopError(error))
        { throw new OperationCanceledException(token); }
        finally
        {
            await scope.CancelAsync().ConfigureAwait(false);
            var other = ReferenceEquals(outcome, reads) ? writes : reads;
            try { await other.ConfigureAwait(false); }
            catch (Exception error) when (scope.IsCancellationRequested && IsStopError(error)) { }
        }
    }

    private static bool IsStopError(Exception error) =>
        error is OperationCanceledException or IOException or ObjectDisposedException;

    private async Task WritePendingAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = store.Snapshot();
            observe(snapshot);
            var pending = snapshot.Outbox.Where(row => row.Status == PeerDeliveryStatus.Pending).ToArray();
            var pendingIds = pending.Select(row => row.Id).ToHashSet();
            foreach (var id in sent.Keys)
                if (!pendingIds.Contains(id)) sent.TryRemove(id, out _);
            foreach (var item in pending)
            {
                if (!sent.TryAdd(item.Id, 0)) continue;
                await WriteAsync(ReliablePeerWire.Encode(item), token).ConfigureAwait(false);
            }
            using var refresh = CancellationTokenSource.CreateLinkedTokenSource(token);
            refresh.CancelAfter(TimeSpan.FromSeconds(15));
            try { await change.Reader.ReadAsync(refresh.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        }
    }

    private async Task ReadAsync(CancellationToken token)
    {
        while (await peer.ReceiveAsync(token).ConfigureAwait(false) is { } text)
        {
            var received = ReliablePeerWire.Decode(text);
            if (received.IsAck)
            {
                if (sent.ContainsKey(received.Id))
                {
                    store.Acknowledge(received.Id);
                    observe(store.Snapshot());
                }
                continue;
            }
            var acceptance = store.Accept(received.Id, received.ExpiresUtc, received.Text!);
            if (acceptance == PeerAcceptance.Expired) continue;
            if (acceptance == PeerAcceptance.Accepted) observe(store.Snapshot());
            await WriteAsync(ReliablePeerWire.Ack(received.Id), token).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(string wire, CancellationToken token)
    {
        await writer.WaitAsync(token).ConfigureAwait(false);
        try { await peer.SendAsync(wire, token).ConfigureAwait(false); }
        finally { writer.Release(); }
    }
}
