using System.Security.Cryptography;

namespace Tor.Control;

/// <summary>Opaque Tor ED25519-V3 private key. Dispose after storage or registration.</summary>
public sealed class OnionServiceKey : IDisposable
{
    public const int ByteLength = 64;
    private readonly byte[] bytes;
    private bool disposed;

    public OnionServiceKey(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength) throw new InvalidDataException("Invalid Onion Service key length.");
        this.bytes = bytes.ToArray();
    }

    internal static OnionServiceKey Parse(ReadOnlySpan<char> value)
    {
        const string prefix = "ED25519-V3:";
        Span<byte> decoded = stackalloc byte[ByteLength];
        try
        {
            if (value.Length != prefix.Length + 88 || !value.StartsWith(prefix, StringComparison.Ordinal) ||
                !Convert.TryFromBase64Chars(value[prefix.Length..], decoded, out int count) || count != ByteLength)
                throw new InvalidDataException("Invalid Onion Service key reply.");
            Span<char> canonical = stackalloc char[88];
            Convert.TryToBase64Chars(decoded, canonical, out _);
            bool matches = value[prefix.Length..].SequenceEqual(canonical);
            canonical.Clear();
            if (!matches) throw new InvalidDataException("Invalid Onion Service key reply.");
            return new(decoded);
        }
        finally { CryptographicOperations.ZeroMemory(decoded); }
    }

    public void CopyTo(Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        bytes.AsSpan().CopyTo(destination);
    }

    internal string ControlSpec()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return "ED25519-V3:" + Convert.ToBase64String(bytes);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(bytes);
        disposed = true;
    }
}
