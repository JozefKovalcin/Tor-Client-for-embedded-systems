using System.Security.Cryptography;
using System.Text;
using Tor.Control;
using Tor.Core;

namespace Tor.Application;

public enum HostingIdentityAction { Restore, Create, Rotate }

/// <summary>Stores a Tor-hosting key separately from peer certificates and contact trust.</summary>
public sealed class HostedOnionStore(string privateProfile)
{
    private const int AddressSize = 62;
    private const int HeaderSize = 4;
    private const int MaxProtectedSize = 4096;
    private readonly string profile = Path.GetFullPath(privateProfile);
    private string DirectoryPath => Path.Combine(profile, "onion-hosting");
    private string IdentityPath => Path.Combine(DirectoryPath, "identity.dpapi");

    public OnionAddress? ReadAddress()
    {
        if (!Exists(DirectoryPath)) return null;
        var identity = Read();
        using var key = identity.Key;
        return identity.Address;
    }

    public async Task<OnionAddress> RegisterAsync(IPersistentOnionPeerTransport tor, int port,
        HostingIdentityAction action, OnionAddress? previouslyConfirmed, CancellationToken token)
    {
        PrivateStorage.RequireOwnerOnly(profile);
        var lockPath = Path.Combine(profile, ".onion-hosting-writer");
        if (Exists(lockPath)) PrivateStorage.RequireOwnerOnlyFile(lockPath);
        using var writer = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        PrivateStorage.RequireOwnerOnlyFile(lockPath);
        var current = Exists(DirectoryPath) ? Read() : ((OnionAddress Address, OnionServiceKey Key)?)null;
        using var currentKey = current?.Key;
        if (action == HostingIdentityAction.Restore)
        {
            if (current is not { } saved || saved.Address != previouslyConfirmed)
                throw new InvalidDataException("The saved hosting address changed. Reload before hosting.");
            return await tor.RestoreHostAsync(port, saved.Address, saved.Key, token).ConfigureAwait(false);
        }
        if (action == HostingIdentityAction.Create ? current is not null || previouslyConfirmed is not null :
            action != HostingIdentityAction.Rotate || current is null || current.Value.Address != previouslyConfirmed)
            throw new InvalidDataException("The hosting identity no longer matches the confirmed operation.");

        var generated = await tor.GenerateHostAsync(port, token).ConfigureAwait(false);
        using var generatedKey = generated.Key;
        try
        {
            token.ThrowIfCancellationRequested();
            if (current?.Address == generated.Address) throw new InvalidDataException("Tor returned the current hosting address.");
            Save(generated.Address, generatedKey, replace: current is not null);
            return generated.Address;
        }
        catch
        {
            await tor.RemoveAsync(generated.Address).ConfigureAwait(false);
            throw;
        }
    }

    private (OnionAddress Address, OnionServiceKey Key) Read()
    {
        PrivateStorage.RequireOwnerOnly(DirectoryPath);
        PrivateStorage.RequireOwnerOnlyFile(IdentityPath);
        using var file = new FileStream(IdentityPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length is < 1 or > MaxProtectedSize) throw new InvalidDataException("Invalid protected hosting identity length.");
        var protectedBytes = new byte[(int)file.Length];
        file.ReadExactly(protectedBytes);
        byte[] clear = PeerPrivateData.Unprotect(protectedBytes);
        try
        {
            if (clear.Length != HeaderSize + AddressSize + OnionServiceKey.ByteLength ||
                !clear.AsSpan(0, HeaderSize).SequenceEqual("TOH1"u8))
                throw new InvalidDataException("Invalid protected hosting identity format.");
            var address = OnionAddress.Parse(Encoding.ASCII.GetString(clear, HeaderSize, AddressSize));
            return (address, new OnionServiceKey(clear.AsSpan(HeaderSize + AddressSize)));
        }
        catch (FormatException) { throw new InvalidDataException("Invalid saved hosting address."); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private void Save(OnionAddress address, OnionServiceKey key, bool replace)
    {
        var clear = new byte[HeaderSize + AddressSize + OnionServiceKey.ByteLength];
        byte[] protectedBytes;
        try
        {
            "TOH1"u8.CopyTo(clear);
            Encoding.ASCII.GetBytes(address.Host, clear.AsSpan(HeaderSize, AddressSize));
            key.CopyTo(clear.AsSpan(HeaderSize + AddressSize));
            protectedBytes = PeerPrivateData.Protect(clear);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }

        var suffix = "pending-" + Guid.NewGuid().ToString("N");
        var staging = replace ? DirectoryPath : Path.Combine(profile, ".onion-hosting-" + suffix);
        if (!replace) PrivateStorage.CreateNewDirectory(staging);
        PrivateStorage.RequireOwnerOnly(staging);
        var temp = Path.Combine(staging, replace ? suffix : Path.GetFileName(IdentityPath));
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(protectedBytes);
                file.Flush(flushToDisk: true);
            }
            PrivateStorage.RequireOwnerOnlyFile(temp);
            if (replace) File.Move(temp, IdentityPath, overwrite: true);
            else Directory.Move(staging, DirectoryPath);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (!replace && Directory.Exists(staging)) Directory.Delete(staging);
        }
    }

    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
