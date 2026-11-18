using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tor.Application;
using Tor.Control;

namespace TorManager.Desktop.Presentation;

internal enum PeerState
{
    WaitingForTor, Ready, CredentialNeeded, CredentialFailed, InvalidPeerInput, UntrustedPeer, Registering, Connecting,
    Hosting, Connected, Disconnected, WaitingForReconnect, RejectedPeer, AuthenticationFailed,
    OperationFailed, SendFailed, CleanupUnconfirmed
}

internal sealed class PeerViewModel(IPeerTorService tor, PeerProfileStore? profile = null) : ObservableModel, IAsyncDisposable
{
    private PeerIdentity? identity;
    private CancellationTokenSource? attempt;
    private PeerConnection? connection;
    private Task? worker, disposal;
    private bool available, stopping, closed;
    private string peerFingerprint = "", onionAddress = "", hostedAddress = "";
    private PeerState state;
    private string draft = "", lastMessage = "";
    private string label = "";
    private bool comparedFingerprint;
    private IReadOnlyList<TrustedPeer> contacts = [];
    private TrustedPeer? selectedPeer;
    private bool canCreate;

    private ActionCommand? hostCommand, connectCommand, createIdentityCommand, savePeerCommand, revokePeerCommand;
    private AsyncCommand? sendCommand, disconnectCommand;
    public ActionCommand HostCommand => hostCommand ??= new(() => Begin(hosting: true), CanBegin);
    public ActionCommand ConnectCommand => connectCommand ??= new(() => Begin(hosting: false), CanBegin);
    public AsyncCommand SendCommand => sendCommand ??= new(SendAsync, () => SetStatus(PeerState.SendFailed),
        () => connection is not null && !stopping && Draft.Length > 0);
    public AsyncCommand DisconnectCommand => disconnectCommand ??= new(DisconnectAsync,
        () => SetStatus(PeerState.CleanupUnconfirmed), () => attempt is not null && !stopping);
    public ActionCommand CreateIdentityCommand => createIdentityCommand ??= new(CreateProfile, () => canCreate && available && CanEditPeer);
    public ActionCommand SavePeerCommand => savePeerCommand ??= new(Pair, () => profile is not null && identity is not null && ComparedFingerprint && CanEditPeer);
    public ActionCommand RevokePeerCommand => revokePeerCommand ??= new(Revoke, () => profile is not null && identity is not null && SelectedPeer is not null && CanEditPeer);

    public string Fingerprint => identity?.Fingerprint ?? "";
    public string Status => state switch
    {
        PeerState.Ready => "Ready to host or connect.",
        PeerState.CredentialNeeded => "Create a local peer identity to pair this profile.",
        PeerState.CredentialFailed => "The private peer profile could not be verified. Its identity was preserved.",
        PeerState.InvalidPeerInput => "Enter the peer’s SHA-256 fingerprint and a full v3 onion address when connecting.",
        PeerState.Registering => "Registering a loopback onion service…",
        PeerState.Connecting => "Connecting through Tor…",
        PeerState.Hosting => "Onion service registered. Waiting for an authenticated peer.",
        PeerState.Connected => "Connected · peer authenticated with mutual TLS",
        PeerState.Disconnected => "Peer disconnected.",
        PeerState.WaitingForReconnect => "Peer disconnected. Waiting for reconnection.",
        PeerState.RejectedPeer => "Peer authentication or connection ended. Waiting for reconnection.",
        PeerState.AuthenticationFailed => "The peer’s identity could not be authenticated.",
        PeerState.UntrustedPeer => "Select a paired contact. An invalid or revoked pin cannot connect.",
        PeerState.OperationFailed => "Peer operation ended. Check Tor, the onion address and the exchanged fingerprints.",
        PeerState.SendFailed => "The message could not be sent.",
        PeerState.CleanupUnconfirmed => "Peer cleanup could not be confirmed.",
        _ => "Start Tor in Real Online to host or connect."
    };
    public string HostedAddress => hostedAddress;
    public string LastMessage => lastMessage;
    public bool CanEditPeer => attempt is null && !stopping && !closed;
    public string PeerFingerprint
    {
        get => peerFingerprint;
        set { if (Set(ref peerFingerprint, value)) ComparedFingerprint = false; }
    }
    public string OnionAddress { get => onionAddress; set => Set(ref onionAddress, value); }
    public string Draft { get => draft; set { if (Set(ref draft, value)) SendCommand.Invalidate(); } }
    public string Label { get => label; set => Set(ref label, value); }
    public bool ComparedFingerprint { get => comparedFingerprint; set { if (Set(ref comparedFingerprint, value)) SavePeerCommand.Invalidate(); } }
    public IReadOnlyList<TrustedPeer> Contacts { get => contacts; private set => Set(ref contacts, value); }
    public TrustedPeer? SelectedPeer
    {
        get => selectedPeer;
        set
        {
            if (!CanEditPeer || !Set(ref selectedPeer, value)) return;
            OnionAddress = value?.Onion ?? "";
            Invalidate();
        }
    }

    public void SetAvailable(bool ready)
    {
        if (closed || ready == available) return;
        available = ready;
        if (ready && identity is null)
        {
            try
            {
                identity = profile is null ? new PeerIdentity() : profile.OpenIdentity();
                canCreate = identity is null;
                if (canCreate) SetStatus(PeerState.CredentialNeeded);
                else LoadContacts();
            }
            catch (Exception error) when (ProfileFailure(error)) { SetStatus(PeerState.CredentialFailed); canCreate = false; }
            Changed(nameof(Fingerprint));
        }
        if (attempt is null && identity is not null)
            SetStatus(ready ? PeerState.Ready : PeerState.WaitingForTor);
        Invalidate();
    }

    private bool CanBegin() => available && identity is not null && attempt is null && !stopping && !closed &&
        (profile is null || SelectedPeer is not null);

    private void CreateProfile()
    {
        try
        {
            canCreate = false;
            identity = profile!.CreateIdentity();
            LoadContacts();
            SetStatus(PeerState.Ready);
        }
        catch (Exception error) when (ProfileFailure(error)) { SetStatus(PeerState.CredentialFailed); }
        finally { Changed(nameof(Fingerprint)); Invalidate(); }
    }

    private void LoadContacts(string? selection = null)
    {
        if (profile is null || identity is null) return;
        selection ??= SelectedPeer?.Fingerprint;
        Contacts = profile.ReadContacts(identity).Where(peer => !peer.Revoked).ToArray();
        SelectedPeer = Contacts.FirstOrDefault(peer => peer.Fingerprint == selection);
    }

    private void Pair()
    {
        try
        {
            var peer = profile!.Pair(identity!, Label, PeerFingerprint, OnionAddress, ComparedFingerprint);
            LoadContacts(peer.Fingerprint);
            Label = PeerFingerprint = "";
            SetStatus(PeerState.Ready);
        }
        catch (FormatException) { SetStatus(PeerState.InvalidPeerInput); }
        catch (InvalidOperationException) { SetStatus(PeerState.UntrustedPeer); }
        catch (Exception error) when (ProfileFailure(error)) { SetStatus(PeerState.CredentialFailed); }
        finally { ComparedFingerprint = false; }
    }

    private void Revoke()
    {
        try
        {
            profile!.Revoke(identity!, SelectedPeer!.Fingerprint);
            LoadContacts();
            SetStatus(PeerState.UntrustedPeer);
        }
        catch (Exception error) when (ProfileFailure(error)) { SetStatus(PeerState.CredentialFailed); }
    }

    private void RequireTrusted(PeerIdentity local, string fingerprint)
    {
        if (profile is not null && !profile.ReadContacts(local).Any(peer => peer.Fingerprint == fingerprint && !peer.Revoked))
            throw new AuthenticationException("The selected trust record is unavailable or revoked.");
    }

    private static bool ProfileFailure(Exception error) => error is CryptographicException or IOException
        or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or System.Security.SecurityException;

    private void Begin(bool hosting)
    {
        if (!CanBegin() || tor.GetPeerTransport() is not { } transport) return;
        OnionAddress? address;
        var fingerprint = SelectedPeer?.Fingerprint ?? PeerFingerprint.Trim();
        try
        {
            _ = PeerIdentity.ParseFingerprint(fingerprint);
            RequireTrusted(identity!, fingerprint);
            address = hosting ? null : Tor.Control.OnionAddress.Parse(OnionAddress.Trim());
        }
        catch (FormatException)
        {
            SetStatus(PeerState.InvalidPeerInput);
            return;
        }
        catch (AuthenticationException) { SetStatus(PeerState.UntrustedPeer); return; }
        catch (Exception error) when (ProfileFailure(error)) { SetStatus(PeerState.CredentialFailed); return; }
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
                RequireTrusted(local, fingerprint);
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
                    try { RequireTrusted(local, fingerprint); }
                    catch (AuthenticationException) { SetStatus(PeerState.UntrustedPeer); return; }
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
        catch (Exception error) when (error is InvalidDataException or UnauthorizedAccessException or JsonException)
        { SetStatus(PeerState.CredentialFailed); }
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
        CreateIdentityCommand.Invalidate(); SavePeerCommand.Invalidate(); RevokePeerCommand.Invalidate();
    }
}
