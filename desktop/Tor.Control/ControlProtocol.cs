using System.Text;

namespace Tor.Control;

public sealed class TorControlException(string reason) : IOException(reason);
public sealed record ControlReply(int Code, IReadOnlyList<string> Lines);
public sealed record BootstrapProgress(int Percent, string Tag);

/// <summary>Bounded Tor wire framing. Raw reply data is sensitive and is never logged by this library.</summary>
public sealed class TorReplyReader(Stream stream)
{
    private const int MaximumLineBytes = 8192;
    private const int MaximumReplyBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public async Task<ControlReply> ReadAsync(CancellationToken cancellationToken = default)
    {
        var lines = new List<string>(); int? code = null; int total = 0;
        while (true)
        {
            var line = await LineAsync(cancellationToken).ConfigureAwait(false);
            if (line.Length < 4 || !int.TryParse(line.AsSpan(0, 3), out var current) || current is < 100 or > 699 ||
                line[3] is not (' ' or '-' or '+')) throw new TorControlException("Malformed control reply frame");
            code ??= current;
            if (current != code) throw new TorControlException("Mixed status codes inside a reply");
            lines.Add(line[4..]); total += Encoding.UTF8.GetByteCount(line);
            if (line[3] == '+')
            {
                while (true)
                {
                    var data = await LineAsync(cancellationToken).ConfigureAwait(false);
                    if (data == ".") break;
                    if (data.StartsWith("..", StringComparison.Ordinal)) data = data[1..];
                    lines.Add(data); total += Encoding.UTF8.GetByteCount(data);
                    if (total > MaximumReplyBytes || lines.Count > 256) throw new TorControlException("Control reply limit exceeded");
                }
            }
            if (total > MaximumReplyBytes || lines.Count > 256) throw new TorControlException("Control reply limit exceeded");
            if (line[3] == ' ') return new ControlReply(code.Value, lines.ToArray());
        }
    }
    private async Task<string> LineAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1]; bool carriage = false;
        while (true)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) != 1)
                throw new TorControlException("Control connection closed during reply");
            var value = one[0];
            if (carriage)
            {
                if (value != '\n') throw new TorControlException("Control reply requires CRLF");
                return StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            }
            if (value == '\r') { carriage = true; continue; }
            if (value == '\n' || value == 0 || (value < 32 && value != '\t')) throw new TorControlException("Invalid control byte");
            if (buffer.Length >= MaximumLineBytes) throw new TorControlException("Control line limit exceeded");
            buffer.WriteByte(value);
        }
    }
}
