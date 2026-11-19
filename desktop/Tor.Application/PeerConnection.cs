using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Tor.Control;

namespace Tor.Application;

public sealed class PeerIdentity : IDisposable
{
    internal X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }

    public PeerIdentity() : this(ImportEphemeral(CreateCertificate(TimeSpan.FromDays(1)))) { }

    private PeerIdentity(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new CryptographicException("The peer credential lacks its private key.");
        }
        Certificate = certificate;
        Fingerprint = Certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }

    internal static X509Certificate2 CreateCertificate(TimeSpan validity)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Tor Peer Session", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        {
            new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2")
        }, false));
        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddMinutes(-5), now.Add(validity));
    }

    private static X509Certificate2 ImportEphemeral(X509Certificate2 issued)
    {
        using var original = issued;
        if (OperatingSystem.IsWindows())
        {
            // Schannel needs a user key container during the handshake. Import without
            // PersistKeySet so certificate disposal releases it. The PFX stays in memory.
            var password = RandomNumberGenerator.GetHexString(32);
            var pfx = issued.Export(X509ContentType.Pkcs12, password);
            try { return X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.UserKeySet); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
        }
        else return new X509Certificate2(issued);
    }

    internal static PeerIdentity LoadPrivateCertificate(byte[] pfx) => new(X509CertificateLoader.LoadPkcs12(pfx, "",
        OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet));

    public void Dispose() => Certificate.Dispose();

    public static byte[] ParseFingerprint(string text)
    {
        if (text.Length != 64 || !text.All(char.IsAsciiHexDigit))
            throw new FormatException("Expected a 64-digit peer certificate SHA-256 fingerprint.");
        return Convert.FromHexString(text);
    }
}

public enum PeerProtocol : byte { Transient = 1, Reliable = 2 }

/// <summary>One mutually authenticated, versioned stream with one writer and one reader.</summary>
public sealed class PeerConnection : IAsyncDisposable
{
    public const int MaxMessageBytes = 16 * 1024;
    private static readonly TimeSpan IoDeadline = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HandshakeDeadline = TimeSpan.FromSeconds(12);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int HeaderLength = 12;
    private const byte Message = 1, Close = 2;
    private readonly SslStream stream;
    private readonly CancellationTokenRegistration owner;
    private readonly SemaphoreSlim writer = new(1, 1), reader = new(1, 1);
    private uint sent, received;
    private int disposed;
    public PeerProtocol Protocol { get; }
    internal string LocalFingerprint { get; }
    internal string RemoteFingerprint { get; }

    private PeerConnection(SslStream stream, CancellationToken lifetime, PeerProtocol protocol, string localPin, string remotePin)
    {
        this.stream = stream;
        Protocol = protocol;
        LocalFingerprint = localPin;
        RemoteFingerprint = remotePin;
        owner = lifetime.Register(static s => ((SslStream)s!).Dispose(), stream);
    }

    public static async Task<PeerConnection> ConnectAsync(IOnionPeerTransport transport, OnionAddress address,
        PeerIdentity local, string remoteFingerprint, CancellationToken token = default, PeerProtocol protocol = PeerProtocol.Transient)
    {
        byte[] pin = PeerIdentity.ParseFingerprint(remoteFingerprint);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, transport.Lifetime);
        var tunnel = await transport.ConnectAsync(address, operation.Token).ConfigureAwait(false);
        return await AuthenticateAsync(tunnel, local, pin, false, transport.Lifetime, operation.Token, protocol).ConfigureAwait(false);
    }

    internal static async Task<PeerConnection> AuthenticateAsync(Stream transport, PeerIdentity local, byte[] remotePin,
        bool server, CancellationToken lifetime, CancellationToken token, PeerProtocol protocol = PeerProtocol.Transient,
        Action<PeerHandshakeStage>? trace = null, TimeSpan? tlsDeadline = null, TimeSpan? readyDeadline = null)
    {
        if (trace is not null) transport = new PeerHandshakeTraceStream(transport, server, trace);
        var tls = new SslStream(transport, leaveInnerStreamOpen: false);
        using var negotiation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
        negotiation.CancelAfter(tlsDeadline ?? HandshakeDeadline);
        var phase = PeerHandshakePhase.Tls;
        var stageToken = negotiation.Token;
        bool MatchesPin(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
        {
            bool matched = cert is not null && CryptographicOperations.FixedTimeEquals(cert.GetCertHash(HashAlgorithmName.SHA256), remotePin);
            trace?.Invoke(matched ? PeerHandshakeStage.RemotePinMatched : PeerHandshakeStage.RemotePinRejected);
            return matched;
        }
        var chainPolicy = new X509ChainPolicy { DisableCertificateDownloads = true, RevocationMode = X509RevocationMode.NoCheck };
        try
        {
            var alpn = protocol switch
            {
                PeerProtocol.Transient => new SslApplicationProtocol("tor-peer/1"),
                PeerProtocol.Reliable => new SslApplicationProtocol("tor-peer/2"),
                _ => throw new ArgumentOutOfRangeException(nameof(protocol))
            };
            trace?.Invoke(PeerHandshakeStage.TlsStarted);
            if (server)
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = local.Certificate, ClientCertificateRequired = true,
                    RemoteCertificateValidationCallback = MatchesPin, CertificateChainPolicy = chainPolicy,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ApplicationProtocols = [alpn], AllowTlsResume = false,
                    AllowRenegotiation = false
                }, negotiation.Token).ConfigureAwait(false);
            }
            else
            {
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "tor-peer.invalid", ClientCertificates = new X509CertificateCollection { local.Certificate },
                    LocalCertificateSelectionCallback = (_, _, _, _, _) => local.Certificate,
                    RemoteCertificateValidationCallback = MatchesPin, CertificateChainPolicy = chainPolicy,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ApplicationProtocols = [alpn], AllowTlsResume = false,
                    AllowRenegotiation = false
                }, negotiation.Token).ConfigureAwait(false);
            }
            negotiation.Token.ThrowIfCancellationRequested();
            negotiation.CancelAfter(Timeout.InfiniteTimeSpan);
            trace?.Invoke(PeerHandshakeStage.TlsCompleted);
            if (!tls.IsMutuallyAuthenticated || !tls.IsEncrypted || tls.RemoteCertificate is null ||
                tls.NegotiatedApplicationProtocol != alpn)
                throw new AuthenticationException("Mutual TLS and the selected peer protocol were not negotiated.");
            trace?.Invoke(PeerHandshakeStage.AlpnVerified);
            // The acknowledgement occurs after the server has validated the client certificate.
            // This keeps a TLS 1.3 client from reporting a connection before both pins passed.
            byte[] ready = [(byte)'T', (byte)'P', (byte)protocol, server ? (byte)'S' : (byte)'C'];
            byte[] expected = [(byte)'T', (byte)'P', (byte)protocol, server ? (byte)'C' : (byte)'S'];
            var reply = new byte[4];
            phase = PeerHandshakePhase.Ready;
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            operation.CancelAfter(readyDeadline ?? HandshakeDeadline);
            stageToken = operation.Token;
            if (!server)
            {
                await tls.WriteAsync(ready, operation.Token).ConfigureAwait(false);
                trace?.Invoke(PeerHandshakeStage.ReadySent);
            }
            await tls.ReadExactlyAsync(reply, operation.Token).ConfigureAwait(false);
            if (!reply.AsSpan().SequenceEqual(expected)) throw new AuthenticationException("Peer protocol acknowledgement failed.");
            trace?.Invoke(PeerHandshakeStage.ReadyReceived);
            if (server)
            {
                await tls.WriteAsync(ready, operation.Token).ConfigureAwait(false);
                trace?.Invoke(PeerHandshakeStage.ReadySent);
            }
            operation.Token.ThrowIfCancellationRequested();
            return new(tls, lifetime, protocol, local.Fingerprint, Convert.ToHexString(remotePin));
        }
        catch (OperationCanceledException) when (stageToken.IsCancellationRequested &&
            !token.IsCancellationRequested && !lifetime.IsCancellationRequested)
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            throw new PeerHandshakeTimeoutException(phase);
        }
        catch { await tls.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public Task SendAsync(string message, CancellationToken token = default)
    {
        if (message.Length > MaxMessageBytes || StrictUtf8.GetByteCount(message) is < 1 or > MaxMessageBytes)
            throw new ArgumentException("The UTF-8 message must contain 1 to 16384 bytes.", nameof(message));
        return WriteAsync(Message, StrictUtf8.GetBytes(message), token);
    }

    public async Task<string?> ReceiveAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        token.ThrowIfCancellationRequested();
        if (!reader.Wait(0)) throw new InvalidOperationException("A receive is already in progress.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var header = new byte[HeaderLength];
            if (Protocol == PeerProtocol.Reliable)
            {
                // Reliable peers may remain idle. Bound the remainder once a frame starts.
                await stream.ReadExactlyAsync(header.AsMemory(0, 1), deadline.Token).ConfigureAwait(false);
                deadline.CancelAfter(IoDeadline);
                await stream.ReadExactlyAsync(header.AsMemory(1), deadline.Token).ConfigureAwait(false);
            }
            else
            {
                deadline.CancelAfter(IoDeadline);
                await stream.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            }
            var sequence = checked(received + 1);
            var (kind, length) = DecodeHeader(header, sequence, Protocol);
            received = sequence;
            if (kind == Close)
            {
                await DisposeAsync().ConfigureAwait(false);
                return null;
            }
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, deadline.Token).ConfigureAwait(false);
            return StrictUtf8.GetString(body);
        }
        catch (Exception error) when (error is InvalidDataException or DecoderFallbackException)
        {
            await DisposeAsync().ConfigureAwait(false);
            // Surface rejected records through the stream error handled by both GUI roles.
            throw new IOException("The peer sent an invalid application record.");
        }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
        finally { reader.Release(); }
    }

    public async Task CloseAsync()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await WriteAsync(Close, [], deadline.Token).ConfigureAwait(false); }
        finally { await DisposeAsync().ConfigureAwait(false); }
    }

    private async Task WriteAsync(byte kind, byte[] bytes, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        token.ThrowIfCancellationRequested();
        if (!writer.Wait(0)) throw new InvalidOperationException("A send is already in progress.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(IoDeadline);
        try
        {
            var sequence = checked(sent + 1);
            byte[] frame = new byte[HeaderLength + bytes.Length];
            frame[0] = (byte)'T'; frame[1] = (byte)'P'; frame[2] = (byte)Protocol; frame[3] = kind;
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), sequence);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8), (uint)bytes.Length);
            bytes.CopyTo(frame.AsSpan(HeaderLength));
            await stream.WriteAsync(frame, deadline.Token).ConfigureAwait(false);
            sent = sequence;
        }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
        finally { writer.Release(); }
    }

    internal static (byte Kind, int Length) DecodeHeader(ReadOnlySpan<byte> frame, uint sequence,
        PeerProtocol protocol = PeerProtocol.Transient)
    {
        if (frame.Length != HeaderLength || frame[0] != 'T' || frame[1] != 'P' || frame[2] != (byte)protocol ||
            sequence == 0 || BinaryPrimitives.ReadUInt32BigEndian(frame[4..]) != sequence)
            throw new InvalidDataException("Invalid peer frame version or sequence.");
        uint length = BinaryPrimitives.ReadUInt32BigEndian(frame[8..]);
        if (frame[3] == Message && length is > 0 and <= MaxMessageBytes || frame[3] == Close && length == 0)
            return (frame[3], (int)length);
        throw new InvalidDataException("Invalid peer frame type or length.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await owner.DisposeAsync().ConfigureAwait(false);
        await stream.DisposeAsync().ConfigureAwait(false);
    }
}
