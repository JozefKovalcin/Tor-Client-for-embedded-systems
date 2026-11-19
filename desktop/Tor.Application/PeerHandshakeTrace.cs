namespace Tor.Application;

internal enum PeerHandshakeStage
{
    ClientSocksEstablished, HostTcpAccepted, HostTcpOwnerRejected, TlsStarted,
    ClientHello, ServerHello, OtherFirstTlsRecord,
    RemotePinMatched, RemotePinRejected, TlsCompleted, AlpnVerified,
    ReadySent, ReadyReceived
}

internal enum PeerHandshakePhase { Tls, Ready }

internal sealed class PeerHandshakeTimeoutException(PeerHandshakePhase phase) : IOException(
    phase == PeerHandshakePhase.Tls ? "Peer TLS negotiation exceeded its deadline." : "Peer readiness exceeded its deadline.")
{
    internal PeerHandshakePhase Phase { get; } = phase;
}

// Only the opt-in qualification runner and offline fixtures wrap the stream.
// Retain only record type and handshake type, never any handshake contents.
internal sealed class PeerHandshakeTraceStream(Stream stream, bool server, Action<PeerHandshakeStage> trace) : Stream
{
    private readonly FirstRecord incoming = new();
    private readonly FirstRecord outgoing = new();

    private void Observe(FirstRecord record, ReadOnlySpan<byte> bytes, bool fromServer)
    {
        if (record.Seen >= 6 || bytes.IsEmpty) return;
        int consumed = Math.Min(bytes.Length, 6 - record.Seen);
        for (int index = 0; index < consumed; index++)
        {
            switch (record.Seen + index)
            {
                case 0: record.ContentType = bytes[index]; break;
                case 3: record.Length = bytes[index] << 8; break;
                case 4: record.Length |= bytes[index]; break;
            }
        }
        int handshakeIndex = 5 - record.Seen;
        if (bytes.Length <= handshakeIndex)
        {
            record.Seen += bytes.Length;
            return;
        }
        record.Seen = 6;
        trace((record.ContentType, record.Length, bytes[handshakeIndex], fromServer) switch
        {
            (22, > 0, 1, false) => PeerHandshakeStage.ClientHello,
            (22, > 0, 2, true) => PeerHandshakeStage.ServerHello,
            _ => PeerHandshakeStage.OtherFirstTlsRecord
        });
    }

    private sealed class FirstRecord
    {
        public int Seen;
        public int Length;
        public byte ContentType;
    }

    public override bool CanRead => stream.CanRead;
    public override bool CanWrite => stream.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Flush() => stream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = stream.Read(buffer, offset, count);
        Observe(incoming, buffer.AsSpan(offset, read), !server);
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        stream.Write(buffer, offset, count);
        Observe(outgoing, buffer.AsSpan(offset, count), server);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Observe(incoming, buffer.Span[..read], !server);
        return read;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        Observe(outgoing, buffer.Span, server);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) stream.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await stream.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
