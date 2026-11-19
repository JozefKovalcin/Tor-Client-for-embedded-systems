using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace Tor.Application;

public sealed class PeerDeliveryStorageException() : IOException("The private peer delivery state is unavailable.");

/// <summary>Retries a durable outbox on a freshly authenticated v2 connection.</summary>
public sealed class ReliablePeerSession
{
    private readonly PeerConnection peer;
    private readonly PeerDeliveryStore store;
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly ConcurrentDictionary<Guid, PeerDeliveryStatus> sent = new();
    private readonly object publication = new();
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

    public PeerDeliverySnapshot DisplaySnapshot()
    {
        var snapshot = WithStore(store.Snapshot);
        // The overlay is session-local. A durable ACK or expiry always wins.
        return snapshot with { Outbox = snapshot.Outbox.Select(row =>
            row.Status == PeerDeliveryStatus.Pending && sent.TryGetValue(row.Id, out var state)
                ? row with { Status = state }
                : row).ToArray() };
    }

    private void Publish()
    {
        lock (publication) observe(DisplaySnapshot());
    }

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
            var snapshot = WithStore(store.Snapshot);
            Publish();
            var pending = snapshot.Outbox.Where(row => row.Status == PeerDeliveryStatus.Pending).ToArray();
            var pendingIds = pending.Select(row => row.Id).ToHashSet();
            foreach (var id in sent.Keys)
                if (!pendingIds.Contains(id)) sent.TryRemove(id, out _);
            foreach (var item in pending)
            {
                if (!sent.TryAdd(item.Id, PeerDeliveryStatus.Sending)) continue;
                Publish();
                try { await WriteAsync(ReliablePeerWire.Encode(item), token).ConfigureAwait(false); }
                catch (Exception error) when (!token.IsCancellationRequested && IsStopError(error))
                {
                    sent[item.Id] = PeerDeliveryStatus.SendFailed;
                    Publish();
                    throw;
                }
                sent[item.Id] = PeerDeliveryStatus.Sent;
                Publish();
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
                    WithStore(() => { store.Acknowledge(received.Id); return true; });
                    Publish();
                }
                continue;
            }
            var acceptance = WithStore(() => store.Accept(received.Id, received.ExpiresUtc, received.Text!), receiving: true);
            if (acceptance == PeerAcceptance.Expired) continue;
            if (acceptance == PeerAcceptance.Accepted) Publish();
            await WriteAsync(ReliablePeerWire.Ack(received.Id), token).ConfigureAwait(false);
        }
    }

    private static T WithStore<T>(Func<T> operation, bool receiving = false)
    {
        try { return operation(); }
        catch (Exception error) when ((!receiving || error is not InvalidDataException) &&
            error is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidDataException)
        {
            throw new PeerDeliveryStorageException();
        }
    }

    private async Task WriteAsync(string wire, CancellationToken token)
    {
        await writer.WaitAsync(token).ConfigureAwait(false);
        try { await peer.SendAsync(wire, token).ConfigureAwait(false); }
        finally { writer.Release(); }
    }
}
