using System.Globalization;
using System.Text;

namespace Tor.Application;

internal sealed record PeerEnvelope(Guid Id, long ExpiresUtc, string? Text)
{
    public bool IsAck => Text is null;
}

internal static class ReliablePeerWire
{
    internal const int MaxTextBytes = 8192;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static bool ValidText(string? text)
    {
        if (text is null || text.Length is < 1 or > MaxTextBytes) return false;
        try { return Utf8.GetByteCount(text) <= MaxTextBytes; }
        catch (EncoderFallbackException) { return false; }
    }

    internal static string EncodeText(string text) => ValidText(text)
        ? Convert.ToBase64String(Utf8.GetBytes(text))
        : throw new ArgumentException("Reliable messages must be 1 to 8192 UTF-8 bytes.", nameof(text));

    internal static string Encode(PeerDeliveryItem item) =>
        FormattableString.Invariant($"M {item.Id:N} {item.ExpiresUtc} {EncodeText(item.Text!)}");

    internal static string Ack(Guid id) => FormattableString.Invariant($"A {id:N}");

    internal static PeerEnvelope Decode(string payload)
    {
        string[] fields = payload.Split(' ', 4);
        if (fields.Length < 2 || fields[1].Length != 32 || !Guid.TryParseExact(fields[1], "N", out var id) || id == Guid.Empty)
            throw new InvalidDataException("Malformed peer delivery record.");
        if (fields is ["A", _]) return new(id, 0, null);
        if (fields is ["M", _, var expires, var encoded] && encoded.Length <= (MaxTextBytes + 2) / 3 * 4 &&
            long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            try
            {
                var bytes = Convert.FromBase64String(encoded);
                if (bytes.Length is > 0 and <= MaxTextBytes && Convert.ToBase64String(bytes) == encoded)
                    return new(id, seconds, Utf8.GetString(bytes));
            }
            catch (Exception error) when (error is FormatException or DecoderFallbackException) { }
        }
        throw new InvalidDataException("Malformed peer delivery record.");
    }
}
