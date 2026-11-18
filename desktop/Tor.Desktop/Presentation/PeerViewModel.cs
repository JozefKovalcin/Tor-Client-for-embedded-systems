using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Tor.Application;
using Tor.Control;

namespace TorManager.Desktop.Presentation;

internal enum PeerState
{
    WaitingForTor, Ready, CredentialFailed, InvalidPeerInput, Registering, Connecting,
    Hosting, Connected, Disconnected, WaitingForReconnect, RejectedPeer, AuthenticationFailed,
    OperationFailed, SendFailed, CleanupUnconfirmed
}

internal sealed class PeerViewModel(IPeerTorService tor) : ObservableModel, IAsyncDisposable
{
    private PeerIdentity? identity;
    private CancellationTokenSource? attempt;
    private PeerConnection? connection;
    private Task? worker, disposal;
    private bool available, stopping, closed;
    private string peerFingerprint = "", onionAddress = "", hostedAddress = "";
    private PeerState state;
    private string draft = "", lastMessage = "";

    private ActionCommand? hostCommand, connectCommand;
    private AsyncCommand? sendCommand, disconnectCommand;
    public ActionCommand HostCommand => hostCommand ??= new(() => Begin(hosting: true), CanBegin);
    public ActionCommand ConnectCommand => connectCommand ??= new(() => Begin(hosting: false), CanBegin);
    public AsyncCommand SendCommand => sendCommand ??= new(SendAsync, () => SetStatus(PeerState.SendFailed),
        () => connection is not null && !stopping && Draft.Length > 0);
    public AsyncCommand DisconnectCommand => disconnectCommand ??= new(DisconnectAsync,
        () => SetStatus(PeerState.CleanupUnconfirmed), () => attempt is not null && !stopping);

    public string Fingerprint => identity?.Fingerprint ?? "";
    public string Status => state switch
    {
        PeerState.Ready => "Ready to host or connect.",
        PeerState.CredentialFailed => "Windows could not create the local peer credential.",
        PeerState.InvalidPeerInput => "Enter the peer’s SHA-256 fingerprint and a full v3 onion address when connecting.",
        PeerState.Registering => "Registering a loopback onion service…",
        PeerState.Connecting => "Connecting through Tor…",
        PeerState.Hosting => "Onion service registered. Waiting for an authenticated peer.",
        PeerState.Connected => "Connected · peer authenticated with mutual TLS",
        PeerState.Disconnected => "Peer disconnected.",
        PeerState.WaitingForReconnect => "Peer disconnected. Waiting for reconnection.",
        PeerState.RejectedPeer => "Peer authentication or connection ended. Waiting for reconnection.",
        PeerState.AuthenticationFailed => "The peer’s identity could not be authenticated.",
        PeerState.OperationFailed => "Peer operation ended. Check Tor, the onion address and the exchanged fingerprints.",
        PeerState.SendFailed => "The message could not be sent.",
        PeerState.CleanupUnconfirmed => "Peer cleanup could not be confirmed.",
        _ => "Start Tor in Real Online to host or connect."
    };
    public string HostedAddress => hostedAddress;
    public string LastMessage => lastMessage;
    public bool CanEditPeer => attempt is null && !stopping && !closed;
    public string PeerFingerprint { get => peerFingerprint; set => Set(ref peerFingerprint, value); }
    public string OnionAddress { get => onionAddress; set => Set(ref onionAddress, value); }
    public string Draft { get => draft; set { if (Set(ref draft, value)) SendCommand.Invalidate(); } }

    public void SetAvailable(bool ready)
    {
        if (closed || ready == available) return;
        available = ready;
        if (ready && identity is null)
        {
            try { identity = new PeerIdentity(); }
            catch (CryptographicException) { SetStatus(PeerState.CredentialFailed); }
            Changed(nameof(Fingerprint));
        }
        if (attempt is null && identity is not null)
            SetStatus(ready ? PeerState.Ready : PeerState.WaitingForTor);
        Invalidate();
    }

    private bool CanBegin() => available && identity is not null && attempt is null && !stopping && !closed;

    private void Begin(bool hosting)
    {
        if (!CanBegin() || tor.GetPeerTransport() is not { } transport) return;
        OnionAddress? address;
        var fingerprint = PeerFingerprint.Trim();
        try
        {
            _ = PeerIdentity.ParseFingerprint(fingerprint);
            address = hosting ? null : Tor.Control.OnionAddress.Parse(OnionAddress.Trim());
        }
        catch (FormatException)
        {
            SetStatus(PeerState.InvalidPeerInput);
            return;
        }
        var request = CancellationTokenSource.CreateLinkedTokenSource(transport.Lifetime);
        attempt = request;
        lastMessage = "";
        Changed(nameof(LastMessage));
        SetStatus(hosting ? PeerState.Registering : PeerState.Connecting);
        Invalidate();
        worker = RunAsync(transport, identity!, fingerprint, address, request);
    }

    private async Task RunAsync(IOnionPeerTransport transport, PeerIdentity local, string fingerprint,
        OnionAddress? remote, CancellationTokenSource request)
    {
        await Task.Yield();
        var token = request.Token;
        try
        {
            if (remote is not null)
            {
                await using var outgoing = await PeerConnection.ConnectAsync(transport, remote, local, fingerprint, token);
                await ReadPeerAsync(outgoing, token);
                SetStatus(PeerState.Disconnected);
                return;
            }

            await using var host = await PeerHost.StartAsync(transport, local, fingerprint, token);
            hostedAddress = host.Address.Host;
            Changed(nameof(HostedAddress));
            SetStatus(PeerState.Hosting);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var inbound = await host.AcceptAsync(token);
                    await ReadPeerAsync(inbound, token);
                    SetStatus(PeerState.WaitingForReconnect);
                }
                catch (Exception error) when (!token.IsCancellationRequested &&
                    error is AuthenticationException or IOException or OperationCanceledException or DecoderFallbackException)
                {
                    SetStatus(PeerState.RejectedPeer);
                    await Task.Delay(200, token);
                }
            }
        }
        catch (Exception error) when (token.IsCancellationRequested &&
            error is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        { SetStatus(PeerState.Disconnected); }
        catch (AuthenticationException) { SetStatus(PeerState.AuthenticationFailed); }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or
            ObjectDisposedException or InvalidOperationException or DecoderFallbackException)
        { SetStatus(PeerState.OperationFailed); }
        finally
        {
            attempt = null;
            request.Dispose();
            hostedAddress = "";
            Changed(nameof(HostedAddress));
            Invalidate();
        }
    }

    private async Task ReadPeerAsync(PeerConnection peer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        connection = peer;
        SetStatus(PeerState.Connected);
        Invalidate();
        try
        {
            while (await peer.ReceiveAsync(token) is { } message)
            {
                lastMessage = message;
                Changed(nameof(LastMessage));
            }
        }
        finally { connection = null; Invalidate(); }
    }

    private async Task SendAsync()
    {
        if (connection is not { } peer || attempt is not { } request) return;
        var payload = Draft;
        await peer.SendAsync(payload, request.Token);
        if (Draft == payload) Draft = "";
    }

    private async Task DisconnectAsync()
    {
        if (attempt is not { } request) return;
        stopping = true;
        Invalidate();
        var pending = worker;
        try
        {
            if (connection is { } peer)
            {
                try { await peer.CloseAsync(); }
                catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException) { }
            }
            if (ReferenceEquals(request, attempt)) await request.CancelAsync();
            if (pending is not null) await pending;
        }
        finally { stopping = false; Invalidate(); }
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        closed = true;
        try { await DisconnectAsync(); }
        finally { identity?.Dispose(); }
    }

    private void SetStatus(PeerState value) => Set(ref state, value, nameof(Status));

    private void Invalidate()
    {
        Changed(nameof(CanEditPeer));
        HostCommand.Invalidate(); ConnectCommand.Invalidate(); SendCommand.Invalidate(); DisconnectCommand.Invalidate();
    }
}
