using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Tor.Control;
using Tor.Core;

namespace Tor.Application;

public sealed record TrustedPeer(string Label, string Fingerprint, string? Onion, bool Revoked);

/// <summary>Owns the local TLS key and an explicitly curated, immutable-pin trust store for one profile.</summary>
public sealed class PeerProfileStore
{
    private const string IdentityName = "local-identity.dpapi";
    private const string RecoveryName = "local-identity-recovery.dpapi";
    private const int Limit = 64 * 1024, MaxPeers = 128;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string root;
    private sealed record ContactFile(int Version, string LocalFingerprint, TrustedPeer[] Peers);

    public PeerProfileStore(string privateDirectory)
    {
        var path = Path.GetFullPath(privateDirectory);
        if (OperatingSystem.IsWindows() && (path.Length < 3 || !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' || path.AsSpan(2).Contains(':') ||
            new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed))
            throw new ArgumentException("Peer storage must be on a fixed local Windows drive.");
        root = Path.Combine(path, "peer-security");
    }

    public PeerIdentity? OpenIdentity()
    {
        if (!Exists(root)) return null;
        PrivateStorage.RequireOwnerOnly(root);
        return OpenBoundIdentity(Read(IdentityName));
    }

    public bool HasRecoveryCopy => Exists(Path.Combine(root, RecoveryName));

    public void SaveRecoveryCopy(PeerIdentity active)
    {
        ArgumentNullException.ThrowIfNull(active);
        using var gate = Lock();
        var protectedIdentity = Read(IdentityName);
        using var current = OpenBoundIdentity(protectedIdentity);
        if (active.Fingerprint != current.Fingerprint)
            throw new InvalidOperationException("The local identity changed. Reopen the profile before saving a recovery copy.");
        Write(RecoveryName, protectedIdentity, replace: false);
    }

    public PeerIdentity RecoverIdentity(bool explicitlyApproved)
    {
        if (!explicitlyApproved) throw new InvalidOperationException("Confirm recovery of the existing local TLS identity.");
        using var gate = Lock();
        var recovery = Read(RecoveryName);
        var identity = OpenBoundIdentity(recovery);
        try
        {
            bool present = Exists(Path.Combine(root, IdentityName));
            if (present)
            {
                byte[] previous = Read(IdentityName);
                try
                {
                    using var healthy = OpenProtectedIdentity(previous);
                    throw new InvalidOperationException("The current local identity can be opened and was preserved.");
                }
                catch (CryptographicException)
                {
                    // Keep the previous protected bytes inside the private directory.
                    Write("damaged-identity-" + Guid.NewGuid().ToString("N") + ".dpapi", previous, replace: false);
                }
            }
            Write(IdentityName, recovery, replace: present);
            return identity;
        }
        catch { identity.Dispose(); throw; }
    }

    private PeerIdentity OpenBoundIdentity(byte[] payload)
    {
        var identity = OpenProtectedIdentity(payload);
        try { _ = ReadContacts(identity); return identity; }
        catch { identity.Dispose(); throw; }
    }

    private static PeerIdentity OpenProtectedIdentity(byte[] payload)
    {
        byte[] clear = PeerPrivateData.Unprotect(payload);
        try
        {
            return PeerIdentity.LoadPrivateCertificate(clear);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public PeerIdentity CreateIdentity()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string parent = Path.GetDirectoryName(root)!;
        if (!Exists(parent))
        {
            _ = new VerifiedFileStore(Path.GetDirectoryName(parent)!);
            PrivateStorage.CreateNewDirectory(parent);
        }
        PrivateStorage.RequireOwnerOnly(parent);
        // An existing profile without its credential is an error, including an
        // interrupted initialization. Do not silently generate a new identity.
        PrivateStorage.CreateNewDirectory(root);
        using var certificate = PeerIdentity.CreateCertificate(TimeSpan.FromDays(730));
        var pfx = certificate.Export(X509ContentType.Pkcs12, "");
        try
        {
            var identity = PeerIdentity.LoadPrivateCertificate(pfx);
            try
            {
                Write("contacts.json", JsonSerializer.SerializeToUtf8Bytes(new ContactFile(1, identity.Fingerprint, []), Json), replace: false);
                Write(IdentityName, PeerPrivateData.Protect(pfx), replace: false);
                return identity;
            }
            catch { identity.Dispose(); throw; }
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    public IReadOnlyList<TrustedPeer> ReadContacts(PeerIdentity identity)
    {
        var file = JsonSerializer.Deserialize<ContactFile>(Read("contacts.json"), Json);
        if (file?.Version != 1 || file.LocalFingerprint != identity.Fingerprint || file.Peers is null ||
            file.Peers.Length > MaxPeers || file.Peers.Any(peer => peer is null || !Valid(peer) ||
                peer.Fingerprint == identity.Fingerprint) ||
            file.Peers.Select(peer => peer.Fingerprint).Distinct().Count() != file.Peers.Length)
            throw new InvalidDataException("Invalid peer trust store.");
        return file.Peers;
    }

    public TrustedPeer Pair(PeerIdentity identity, string label, string remoteFingerprint, string? onion,
        bool comparedOverTrustedChannel)
        => AddPin(identity, label, remoteFingerprint, onion, comparedOverTrustedChannel, null);

    public TrustedPeer Rotate(PeerIdentity identity, TrustedPeer current, string label, string remoteFingerprint,
        string? onion, bool comparedOverTrustedChannel)
        => AddPin(identity, label, remoteFingerprint, onion, comparedOverTrustedChannel, current);

    private TrustedPeer AddPin(PeerIdentity identity, string label, string remoteFingerprint, string? onion,
        bool comparedOverTrustedChannel, TrustedPeer? replacing)
    {
        if (!comparedOverTrustedChannel) throw new InvalidOperationException("Confirm the independently compared fingerprint.");
        var fingerprint = Convert.ToHexString(PeerIdentity.ParseFingerprint(remoteFingerprint.Trim()));
        var peer = new TrustedPeer(label.Trim(), fingerprint,
            string.IsNullOrWhiteSpace(onion) ? null : OnionAddress.Parse(onion.Trim()).Host, false);
        if (!Valid(peer) || peer.Fingerprint == identity.Fingerprint) throw new FormatException("Invalid trusted peer details.");
        using var gate = Lock();
        var contacts = ReadContacts(identity);
        if (contacts.Any(item => item.Fingerprint == peer.Fingerprint))
            throw new InvalidOperationException("This fingerprint already has a trust or revocation record.");
        if (contacts.Count >= MaxPeers) throw new InvalidOperationException("The private contact store is full.");
        if (replacing is not null && (replacing.Revoked || !contacts.Contains(replacing)))
            throw new InvalidOperationException("The contact changed; reload it before rotating its pin.");
        Save(identity, [.. contacts.Select(item => item == replacing ? item with { Revoked = true } : item), peer]);
        return peer;
    }

    public TrustedPeer UpdateEndpoint(PeerIdentity identity, TrustedPeer current, string? onion)
    {
        var updated = current with { Onion = string.IsNullOrWhiteSpace(onion) ? null : OnionAddress.Parse(onion.Trim()).Host };
        using var gate = Lock();
        var contacts = ReadContacts(identity).ToArray();
        int index = Array.IndexOf(contacts, current);
        if (current.Revoked || index < 0) throw new InvalidOperationException("The contact changed; reload it before editing.");
        contacts[index] = updated;
        Save(identity, contacts);
        return updated;
    }

    public void Revoke(PeerIdentity identity, string fingerprint)
    {
        using var gate = Lock();
        var contacts = ReadContacts(identity).ToArray();
        int index = Array.FindIndex(contacts, peer => peer.Fingerprint == fingerprint && !peer.Revoked);
        if (index < 0) throw new InvalidOperationException("Select an active trusted peer.");
        contacts[index] = contacts[index] with { Revoked = true };
        Save(identity, contacts);
    }

    public PeerDeliveryStore OpenDelivery(PeerIdentity identity, TrustedPeer peer)
    {
        if (!ReadContacts(identity).Any(contact => contact.Fingerprint == peer.Fingerprint && !contact.Revoked))
            throw new InvalidOperationException("The selected peer is not active in this profile.");
        return new(root, identity.Fingerprint, peer.Fingerprint);
    }

    private void Save(PeerIdentity identity, TrustedPeer[] contacts) =>
        Write("contacts.json", JsonSerializer.SerializeToUtf8Bytes(new ContactFile(1, identity.Fingerprint, contacts), Json), replace: true);

    private static bool Valid(TrustedPeer peer)
    {
        if (peer.Label is null || peer.Label.Length is < 1 or > 64 || peer.Label.Any(char.IsControl) ||
            peer.Fingerprint is null || peer.Fingerprint.Length != 64 || !peer.Fingerprint.All(char.IsAsciiHexDigit) ||
            peer.Fingerprint != peer.Fingerprint.ToUpperInvariant())
            return false;
        if (peer.Onion is null) return true;
        try { return OnionAddress.Parse(peer.Onion).Host == peer.Onion; }
        catch (FormatException) { return false; }
    }

    private FileStream Lock()
    {
        var path = Path.Combine(root, ".contacts-writer");
        return PrivateStorage.OpenPrivateFile(path, FileMode.OpenOrCreate);
    }

    private byte[] Read(string name)
    {
        PrivateStorage.RequireOwnerOnly(root);
        var path = Path.Combine(root, name);
        PrivateStorage.RequireOwnerOnlyFile(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length is < 1 or > Limit) throw new InvalidDataException("Invalid private peer data length.");
        var data = new byte[(int)file.Length];
        file.ReadExactly(data);
        return data;
    }

    private void Write(string name, byte[] data, bool replace)
    {
        if (data.Length is < 1 or > Limit) throw new InvalidDataException("Private peer data exceeds its storage limit.");
        string path = Path.Combine(root, name);
        string temp = Path.Combine(root, "pending-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var file = PrivateStorage.OpenPrivateFile(temp, FileMode.CreateNew))
            {
                file.Write(data);
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: replace);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
