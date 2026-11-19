using System.Security.Cryptography;
using System.Text.Json;
using Tor.Core;

namespace Tor.Application;

public enum PeerDeliveryStatus { Pending, Delivered, Expired, Sending, Sent, SendFailed }
public sealed class PeerDeliveryStorageException() : IOException("The private peer delivery state is unavailable.");
public sealed record PeerDeliveryItem(Guid Id, long ExpiresUtc, string? Text, PeerDeliveryStatus Status);
public sealed record PeerHistoryItem(Guid Id, bool Incoming, long ReceivedUtc, string Text);
public sealed record PeerDeliverySnapshot(long Revision, IReadOnlyList<PeerDeliveryItem> Outbox,
    IReadOnlyList<PeerDeliveryItem> Inbox, bool HistoryEnabled, IReadOnlyList<PeerHistoryItem> History);
internal enum PeerAcceptance { Accepted, Duplicate, Expired }

/// <summary>Bounded encrypted outbox and inbox, scoped to one pair of pinned peer identities.</summary>
public sealed class PeerDeliveryStore
{
    public const int TtlSeconds = 900;
    private const int MaximumEntries = 64, MaximumBytes = 1024 * 1024;
    private const int HistoryTtlSeconds = 7 * 24 * 60 * 60;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string root, path, writer, local, remote;
    private readonly TimeProvider clock;
    private readonly Action<string, string> publish;
    private readonly object gate = new();
    internal string LocalFingerprint => local;
    internal string RemoteFingerprint => remote;

    private sealed record QueueFile(int Version, string Local, string Remote, long Revision,
        List<PeerDeliveryItem> Outbox, List<PeerDeliveryItem> Inbox)
    {
        public bool HistoryEnabled { get; set; }
        public List<PeerHistoryItem> History { get; init; } = [];
    }

    internal PeerDeliveryStore(string securityRoot, string localFingerprint, string peerFingerprint,
        TimeProvider? clock = null, Action<string, string>? publish = null)
    {
        root = securityRoot;
        local = Convert.ToHexString(PeerIdentity.ParseFingerprint(localFingerprint));
        remote = Convert.ToHexString(PeerIdentity.ParseFingerprint(peerFingerprint));
        path = Path.Combine(root, "delivery-" + remote + ".dpapi");
        writer = Path.Combine(root, "delivery-" + remote + ".lock");
        this.clock = clock ?? TimeProvider.System;
        this.publish = publish ?? (static (source, destination) => File.Move(source, destination, overwrite: true));
    }

    public PeerDeliveryItem Enqueue(string text)
    {
        ReliablePeerWire.EncodeText(text); // Check both the UTF-8 limit and well-formedness before persistence.
        return Transaction((data, now) =>
        {
            if (data.Outbox.Count == MaximumEntries)
            {
                int terminal = data.Outbox.FindIndex(item => item.Status != PeerDeliveryStatus.Pending);
                if (terminal < 0) throw new InvalidOperationException("The pending peer outbox is full.");
                data.Outbox.RemoveAt(terminal);
            }
            var item = new PeerDeliveryItem(Guid.NewGuid(), now + TtlSeconds, text, PeerDeliveryStatus.Pending);
            data.Outbox.Add(item);
            return (item, true);
        });
    }

    public PeerDeliverySnapshot Snapshot() => Transaction((data, _) =>
        (new PeerDeliverySnapshot(data.Revision, data.Outbox.ToArray(), data.Inbox.ToArray(),
            data.HistoryEnabled, data.History.ToArray()), false));

    public void SetHistoryEnabled(bool enabled) => Transaction((data, _) =>
    {
        bool changed = data.HistoryEnabled != enabled;
        data.HistoryEnabled = enabled;
        return (changed, changed);
    });

    internal PeerAcceptance Accept(Guid id, long expires, string text)
    {
        ReliablePeerWire.EncodeText(text);
        return Transaction((data, now) =>
        {
            if (id == Guid.Empty || expires > now + TtlSeconds + 120)
                throw new InvalidDataException("Invalid peer message ID or expiry.");
            if (expires <= now) return (PeerAcceptance.Expired, false);
            var previous = data.Inbox.Find(item => item.Id == id);
            if (previous is not null)
            {
                if (previous.ExpiresUtc != expires || previous.Text != text)
                    throw new InvalidDataException("Peer message ID was reused with different data.");
                return (PeerAcceptance.Duplicate, false);
            }
            if (data.Inbox.Count >= MaximumEntries) throw new IOException("The peer inbox is full.");
            data.Inbox.Add(new(id, expires, text, PeerDeliveryStatus.Delivered));
            Archive(data, id, incoming: true, now, text);
            return (PeerAcceptance.Accepted, true);
        });
    }

    internal void Acknowledge(Guid id) => Transaction((data, now) =>
    {
        int index = data.Outbox.FindIndex(item => item.Id == id && item.Status == PeerDeliveryStatus.Pending);
        if (index < 0) return (false, false);
        var item = data.Outbox[index];
        Archive(data, id, incoming: false, now, item.Text!);
        data.Outbox[index] = item with { Text = null, Status = PeerDeliveryStatus.Delivered };
        return (true, true);
    });

    private T Transaction<T>(Func<QueueFile, long, (T Result, bool Changed)> operation)
    {
        lock (gate)
        {
            using var lease = AcquireWriter();
            QueueFile data;
            try { data = Read(); }
            catch (InvalidDataException) { throw new PeerDeliveryStorageException(); }
            long now = clock.GetUtcNow().ToUnixTimeSeconds();
            bool expired = data.Inbox.RemoveAll(item => item.ExpiresUtc <= now) != 0;
            expired |= data.History.RemoveAll(item => item.ReceivedUtc <= now - HistoryTtlSeconds) != 0;
            for (int index = 0; index < data.Outbox.Count; index++)
            {
                var item = data.Outbox[index];
                if (item.Status != PeerDeliveryStatus.Pending || item.ExpiresUtc > now) continue;
                data.Outbox[index] = item with { Text = null, Status = PeerDeliveryStatus.Expired };
                expired = true;
            }
            if (expired) data = data with { Revision = checked(data.Revision + 1) };
            var result = operation(data, now);
            if (result.Changed && !expired) data = data with { Revision = checked(data.Revision + 1) };
            if (expired || result.Changed)
            {
                try { Write(data); }
                catch (InvalidDataException) { throw new PeerDeliveryStorageException(); }
            }
            return result.Result;
        }
    }

    private FileStream AcquireWriter()
    {
        try
        {
            PrivateStorage.RequireOwnerOnly(root);
            CheckPrivateFile(writer);
            var lease = new FileStream(writer, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try { PrivateStorage.RequireOwnerOnlyFile(writer); return lease; }
            catch { lease.Dispose(); throw; }
        }
        catch (InvalidDataException) { throw new PeerDeliveryStorageException(); }
    }

    private QueueFile Read()
    {
        if (!CheckPrivateFile(path)) return new(1, local, remote, 0, [], []);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > MaximumBytes + 8192) throw new InvalidDataException("Invalid encrypted delivery file size.");
        var protectedData = new byte[(int)stream.Length];
        stream.ReadExactly(protectedData);
        var clear = PeerPrivateData.Unprotect(protectedData);
        try
        {
            var data = JsonSerializer.Deserialize<QueueFile>(clear, Json);
            if (clear.Length > MaximumBytes || data?.Version != 1 || data.Revision < 0 || data.Local != local || data.Remote != remote ||
                !Valid(data.Outbox, inbox: false) || !Valid(data.Inbox, inbox: true) ||
                data.History is null || data.History.Count > MaximumEntries ||
                data.History.Any(item => item is null || item.Id == Guid.Empty || item.ReceivedUtc <= 0 ||
                    !ReliablePeerWire.ValidText(item.Text)))
                throw new InvalidDataException("Invalid per-peer delivery state.");
            return data;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private static bool Valid(List<PeerDeliveryItem>? items, bool inbox) => items is { Count: <= MaximumEntries } &&
        items.All(item =>
            item is { Id: var id, ExpiresUtc: > 0 } && id != Guid.Empty &&
            (inbox ? item.Status == PeerDeliveryStatus.Delivered && ReliablePeerWire.ValidText(item.Text)
                : item.Status switch
                {
                    PeerDeliveryStatus.Pending => ReliablePeerWire.ValidText(item.Text),
                    PeerDeliveryStatus.Delivered or PeerDeliveryStatus.Expired => item.Text is null,
                    _ => false
                })) &&
        items.Select(item => item.Id).Distinct().Count() == items.Count;

    private void Write(QueueFile data)
    {
        byte[] clear;
        while (true)
        {
            clear = JsonSerializer.SerializeToUtf8Bytes(data, Json);
            if (clear.Length <= MaximumBytes) break;
            CryptographicOperations.ZeroMemory(clear);
            if (data.History.Count == 0) throw new IOException("The encrypted queue is at its byte limit.");
            data.History.RemoveAt(0); // Delivery takes precedence over optional archival data.
        }
        byte[] protectedData;
        try
        {
            protectedData = PeerPrivateData.Protect(clear);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
        string temp = Path.Combine(root, "pending-delivery-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(protectedData);
                stream.Flush(flushToDisk: true);
            }
            PrivateStorage.RequireOwnerOnlyFile(temp);
            publish(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void Archive(QueueFile data, Guid id, bool incoming, long now, string text)
    {
        if (!data.HistoryEnabled) return;
        if (data.History.Count == MaximumEntries) data.History.RemoveAt(0);
        data.History.Add(new(id, incoming, now, text));
    }

    private static bool CheckPrivateFile(string file)
    {
        try { _ = File.GetAttributes(file); }
        catch (FileNotFoundException) { return false; }
        PrivateStorage.RequireOwnerOnlyFile(file);
        return true;
    }
}
