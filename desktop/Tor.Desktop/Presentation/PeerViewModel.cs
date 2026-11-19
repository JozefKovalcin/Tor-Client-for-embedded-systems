using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tor.Application;
using Tor.Control;
using TorManager.Desktop.Services;

namespace TorManager.Desktop.Presentation;

internal enum PeerState
{
    WaitingForTor, Ready, CredentialNeeded, CredentialFailed, DeliveryUnavailable, InvalidPeerInput, UntrustedPeer, Registering, Connecting,
    Hosting, Connected, Disconnected, WaitingForReconnect, RejectedPeer, AuthenticationFailed,
    OperationFailed, SendFailed, CleanupUnconfirmed, Retrying, EndpointSaved, ContactChanged
}

internal sealed record PeerMessageRow(string Id, string Direction, string Status, string? Text);
internal sealed record PeerConnectionEvent(DateTimeOffset At, string Status)
{
    public string Display => $"{At.ToLocalTime():g} · {Status}";
}

internal sealed class PeerViewModel(IPeerTorService tor, PeerProfileStore? profile = null, IUiDispatcher? dispatcher = null,
    HostedOnionStore? hostingStore = null)
    : ObservableModel, IAsyncDisposable
{
    private const int MaximumReconnectAttempts = 4;
    private PeerIdentity? identity;
    private CancellationTokenSource? attempt;
    private PeerConnection? connection;
    private Task? worker, disposal;
    private bool available, stopping, closed;
    private string peerFingerprint = "", onionAddress = "", hostedAddress = "";
    private PeerState state;
    private string draft = "", lastMessage = "";
    private string label = "";
    private string pairingAddress = "";
    private bool comparedFingerprint;
    private IReadOnlyList<TrustedPeer> contacts = [];
    private TrustedPeer? selectedPeer;
    private bool canCreate;
    private (OnionAddress Address, string Fingerprint, bool Hosting)? resumeAfterTor;
    private OnionAddress? savedHostingAddress;
    private bool invalidHostingIdentity, approveHostingRotation;
    private int retry;
    private long revision = -1;
    private PeerDeliveryStore? delivery;
    private ReliablePeerSession? reliable;
    private PeerDeliverySnapshot? visibleDelivery;
    private IReadOnlyList<PeerMessageRow> deliveryRows = [];
    private IReadOnlyList<PeerMessageRow> historyRows = [];
    private bool saveHistory;
    private string deliveryNotice = "";
    private readonly Dictionary<string, PeerConnectionEvent[]> connectionHistory = new(StringComparer.Ordinal);

    private ActionCommand? hostCommand, connectCommand, createIdentityCommand, savePeerCommand, revokePeerCommand,
        saveEndpointCommand, rotatePeerCommand, createHostingCommand, rotateHostingCommand, transientHostCommand;
    private AsyncCommand? sendCommand, disconnectCommand;
    public ActionCommand HostCommand => hostCommand ??= new(
        () => Begin(hosting: true, hostAction: hostingStore is null ? null : HostingIdentityAction.Restore),
        () => CanBegin() && (hostingStore is null || savedHostingAddress is not null && !invalidHostingIdentity));
    public ActionCommand CreateHostingCommand => createHostingCommand ??= new(
        () => Begin(hosting: true, hostAction: HostingIdentityAction.Create),
        () => CanBegin() && hostingStore is not null && savedHostingAddress is null && !invalidHostingIdentity);
    public ActionCommand RotateHostingCommand => rotateHostingCommand ??= new(
        () => Begin(hosting: true, hostAction: HostingIdentityAction.Rotate),
        () => CanBegin() && CanApproveHostingRotation && ApproveHostingRotation);
    public ActionCommand TransientHostCommand => transientHostCommand ??= new(() => Begin(hosting: true),
        () => CanBegin() && !invalidHostingIdentity);
    public ActionCommand ConnectCommand => connectCommand ??= new(() => Begin(hosting: false), CanBegin);
    public AsyncCommand SendCommand => sendCommand ??= new(SendAsync, () => SetStatus(PeerState.SendFailed),
        () => (delivery is not null || connection is not null && profile is null) && !stopping && !closed && Draft.Length > 0);
    public AsyncCommand DisconnectCommand => disconnectCommand ??= new(DisconnectAsync,
        () => SetStatus(PeerState.CleanupUnconfirmed), () => (attempt is not null || resumeAfterTor is not null) && !stopping);
    public ActionCommand CreateIdentityCommand => createIdentityCommand ??= new(CreateProfile, () => canCreate && CanEditPeer);
    public ActionCommand SavePeerCommand => savePeerCommand ??= new(() => Pair(rotate: false), CanPair);
    public ActionCommand RotatePeerCommand => rotatePeerCommand ??= new(() => Pair(rotate: true), () => CanPair() && SelectedPeer is not null);
    public ActionCommand SaveEndpointCommand => saveEndpointCommand ??= new(SaveEndpoint,
        () => profile is not null && identity is not null && CanEditPeer && SelectedPeer is { } peer &&
            !string.Equals(OnionAddress.Trim(), peer.Onion ?? "", StringComparison.OrdinalIgnoreCase));
    public ActionCommand RevokePeerCommand => revokePeerCommand ??= new(Revoke, () => profile is not null && identity is not null && SelectedPeer is not null && CanEditPeer);

    public string Fingerprint => identity?.Fingerprint ?? "";
    public string Status => state switch
    {
        PeerState.Ready => "Ready to host or connect.",
        PeerState.CredentialNeeded => "Create a local peer identity to pair this profile.",
        PeerState.CredentialFailed => "The private peer profile could not be verified. Its identity was preserved.",
        PeerState.DeliveryUnavailable => "The private message store is unavailable. Check local storage before reconnecting.",
        PeerState.InvalidPeerInput => "Enter the peer’s SHA-256 fingerprint and a full v3 onion address when connecting.",
        PeerState.Registering => "Registering a loopback onion service…",
        PeerState.Connecting => "Connecting through Tor…",
        PeerState.Hosting => "Onion service registered. Waiting for an authenticated peer.",
        PeerState.Connected => "Connected · paired peer authenticated with mutual TLS",
        PeerState.Disconnected => "Peer disconnected.",
        PeerState.WaitingForReconnect => "Peer disconnected. Waiting for reconnection.",
        PeerState.Retrying => $"Connection interrupted. Retrying through Tor ({retry}/{MaximumReconnectAttempts}).",
        PeerState.RejectedPeer => "Peer authentication or connection ended. Waiting for reconnection.",
        PeerState.AuthenticationFailed => "The peer’s identity could not be authenticated.",
        PeerState.UntrustedPeer => "Select a paired contact. An invalid or revoked pin cannot connect.",
        PeerState.OperationFailed => "Peer operation ended. Check Tor, the onion address and the exchanged fingerprints.",
        PeerState.SendFailed => "The message could not be sent.",
        PeerState.CleanupUnconfirmed => "Peer cleanup could not be confirmed.",
        PeerState.EndpointSaved => "Onion address saved for the selected contact.",
        PeerState.ContactChanged => "The contact could not be updated. Check for an existing or revoked fingerprint and reload the profile if another window changed it.",
        _ => "Start Tor in Real Online to host or connect."
    };
    public string HostedAddress => hostedAddress;
    public string SavedHostingAddress => savedHostingAddress?.Host ?? "";
    public bool CanApproveHostingRotation => hostingStore is not null && savedHostingAddress is not null &&
        !invalidHostingIdentity && CanEditPeer;
    public bool ApproveHostingRotation
    {
        get => approveHostingRotation;
        set { if (Set(ref approveHostingRotation, value)) RotateHostingCommand.Invalidate(); }
    }
    public string HostingStatus => invalidHostingIdentity
        ? "The protected hosting identity could not be read. Restore access to the saved identity before hosting."
        : savedHostingAddress is null
            ? "Create a saved address when Tor is online and a paired contact is selected."
            : "The saved address can be hosted again after restarting Tor Manager.";
    public string LastMessage => lastMessage;
    public bool HasDurableDelivery => profile is not null;
    public IReadOnlyList<PeerMessageRow> DeliveryRows { get => deliveryRows; private set => Set(ref deliveryRows, value); }
    public IReadOnlyList<PeerMessageRow> HistoryRows { get => historyRows; private set => Set(ref historyRows, value); }
    public bool HasDeliveryTarget => delivery is not null;
    public IReadOnlyList<PeerConnectionEvent> ConnectionHistory =>
        SelectedPeer is { } peer && connectionHistory.TryGetValue(peer.Fingerprint, out var history) ? history : [];
    public bool SaveHistory
    {
        get => saveHistory;
        set
        {
            if (delivery is not { } store || identity is not { } local || SelectedPeer is not { } contact || value == saveHistory) return;
            try
            {
                RequireTrusted(local, contact.Fingerprint);
                store.SetHistoryEnabled(value);
                ApplyDelivery(store.Snapshot());
            }
            catch (Exception error) when (ProfileFailure(error) || error is AuthenticationException)
            {
                DeliveryNotice = "The encrypted history preference could not be saved.";
                Changed(nameof(SaveHistory));
            }
        }
    }
    public string DeliveryNotice { get => deliveryNotice; private set => Set(ref deliveryNotice, value); }
    public bool CanEditPeer => attempt is null && !stopping && !closed;
    public bool CanEditSelectedPeer => CanEditPeer && SelectedPeer is not null;
    public string PeerFingerprint
    {
        get => peerFingerprint;
        set { if (Set(ref peerFingerprint, value)) ComparedFingerprint = false; }
    }
    public string OnionAddress
    {
        get => onionAddress;
        set
        {
            if (!Set(ref onionAddress, value)) return;
            resumeAfterTor = null;
            DisconnectCommand.Invalidate();
            SaveEndpointCommand.Invalidate();
        }
    }
    public string Draft { get => draft; set { if (Set(ref draft, value)) SendCommand.Invalidate(); } }
    public string Label { get => label; set => Set(ref label, value); }
    public string PairingAddress { get => pairingAddress; set => Set(ref pairingAddress, value); }
    public bool ComparedFingerprint
    {
        get => comparedFingerprint;
        set
        {
            if (!Set(ref comparedFingerprint, value)) return;
            SavePeerCommand.Invalidate();
            RotatePeerCommand.Invalidate();
        }
    }
    public IReadOnlyList<TrustedPeer> Contacts { get => contacts; private set => Set(ref contacts, value); }
    public TrustedPeer? SelectedPeer
    {
        get => selectedPeer;
        set
        {
            if (!CanEditPeer) return;
            bool recipientChanged = selectedPeer?.Fingerprint != value?.Fingerprint;
            if (!Set(ref selectedPeer, value)) return;
            visibleDelivery = null;
            if (recipientChanged)
            {
                Draft = "";
                lastMessage = "";
                Changed(nameof(LastMessage));
                ComparedFingerprint = false;
                SetStatus(available ? PeerState.Ready : PeerState.WaitingForTor);
            }
            resumeAfterTor = null;
            OnionAddress = value?.Onion ?? "";
            DeliveryNotice = "";
            revision = -1;
            delivery = null;
            DeliveryRows = [];
            HistoryRows = [];
            Set(ref saveHistory, false, nameof(SaveHistory));
            if (value is not null && identity is not null && profile is not null)
            {
                try
                {
                    delivery = profile.OpenDelivery(identity, value);
                    ApplyDelivery(delivery.Snapshot());
                }
                catch (Exception error) when (ProfileFailure(error))
                {
                    delivery = null;
                    DeliveryNotice = "Could not open this contact's private delivery state.";
                }
            }
            Changed(nameof(HasDeliveryTarget));
            Changed(nameof(ConnectionHistory));
            Invalidate();
        }
    }

    public void SetAvailable(bool ready)
    {
        if (closed || ready == available) return;
        available = ready;
        if (ready) ReadExistingProfile();
        if (attempt is null && identity is not null)
            SetStatus(ready ? PeerState.Ready : PeerState.WaitingForTor);
        Invalidate();
        TryResume();
    }

    public void ReadExistingProfile()
    {
        if (closed) return;
        ReadHostingIdentity();
        if (identity is not null || profile is null && !available) return;
        try
        {
            identity = profile is null ? new PeerIdentity() : profile.OpenIdentity();
            canCreate = identity is null;
            if (canCreate) SetStatus(PeerState.CredentialNeeded);
            else LoadContacts();
        }
        catch (Exception error) when (ProfileFailure(error)) { SetStatus(PeerState.CredentialFailed); canCreate = false; }
        Changed(nameof(Fingerprint));
        Invalidate();
    }

    private void ReadHostingIdentity()
    {
        if (hostingStore is null) return;
        try
        {
            var address = hostingStore.ReadAddress();
            if (savedHostingAddress != address) ApproveHostingRotation = false;
            savedHostingAddress = address;
            invalidHostingIdentity = false;
        }
        catch (Exception error) when (ProfileFailure(error))
        {
            invalidHostingIdentity = true;
            savedHostingAddress = null;
            ApproveHostingRotation = false;
        }
        Changed(nameof(SavedHostingAddress));
        Changed(nameof(HostingStatus));
        Invalidate();
    }

    private bool CanBegin() => available && identity is not null && attempt is null && !stopping && !closed &&
        (profile is null || SelectedPeer is not null && delivery is not null);

    private void CreateProfile()
    {
        try
        {
            canCreate = false;
            identity = profile!.CreateIdentity();
            LoadContacts();
            SetStatus(available ? PeerState.Ready : PeerState.WaitingForTor);
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

    private bool CanPair() => profile is not null && identity is not null && ComparedFingerprint && CanEditPeer;

    private void Pair(bool rotate)
    {
        try
        {
            var peer = rotate
                ? profile!.Rotate(identity!, SelectedPeer!, Label, PeerFingerprint, PairingAddress, ComparedFingerprint)
                : profile!.Pair(identity!, Label, PeerFingerprint, PairingAddress, ComparedFingerprint);
            LoadContacts(peer.Fingerprint);
            Label = PeerFingerprint = PairingAddress = "";
            SetStatus(available ? PeerState.Ready : PeerState.WaitingForTor);
        }
        catch (FormatException) { SetStatus(PeerState.InvalidPeerInput); }
        catch (InvalidOperationException) { SetStatus(PeerState.ContactChanged); }
        catch (Exception error) when (ProfileFailure(error)) { SetStatus(PeerState.CredentialFailed); }
        finally { ComparedFingerprint = false; }
    }

    private void SaveEndpoint()
    {
        try
        {
            var updated = profile!.UpdateEndpoint(identity!, SelectedPeer!, OnionAddress);
            LoadContacts(updated.Fingerprint);
            SetStatus(PeerState.EndpointSaved);
        }
        catch (FormatException) { SetStatus(PeerState.InvalidPeerInput); }
        catch (InvalidOperationException) { SetStatus(PeerState.ContactChanged); }
        catch (Exception error) when (ProfileFailure(error)) { SetStatus(PeerState.CredentialFailed); }
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

    private void Begin(bool hosting, HostingIdentityAction? hostAction = null)
    {
        if (!CanBegin() || tor.GetPeerTransport() is not { } transport) return;
        if (hostAction is not null && transport is not IPersistentOnionPeerTransport)
        {
            SetStatus(PeerState.OperationFailed);
            return;
        }
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
        resumeAfterTor = null;
        retry = 0;
        attempt = request;
        lastMessage = "";
        Changed(nameof(LastMessage));
        SetStatus(hosting ? PeerState.Registering : PeerState.Connecting);
        var confirmedAddress = savedHostingAddress;
        ApproveHostingRotation = false;
        Invalidate();
        worker = RunAsync(transport, identity!, fingerprint, address, request, delivery, hostAction, confirmedAddress);
    }

    private async Task RunAsync(IOnionPeerTransport transport, PeerIdentity local, string fingerprint,
        OnionAddress? remote, CancellationTokenSource request, PeerDeliveryStore? store,
        HostingIdentityAction? hostAction, OnionAddress? confirmedAddress)
    {
        await Task.Yield();
        var token = request.Token;
        OnionAddress? registeredPersistent = null;
        try
        {
            var protocol = store is null ? PeerProtocol.Transient : PeerProtocol.Reliable;
            if (remote is not null)
            {
                await ConnectAsync(transport, remote, local, fingerprint, store, protocol, token);
                SetStatus(PeerState.Disconnected);
                return;
            }

            Func<int, CancellationToken, Task<OnionAddress>>? register = hostAction is { } action
                ? (port, ct) => hostingStore!.RegisterAsync((IPersistentOnionPeerTransport)transport, port, action, confirmedAddress, ct)
                : null;
            await using var host = await PeerHost.StartAsync(transport, local, fingerprint, token, protocol, register);
            if (hostAction is not null)
            {
                registeredPersistent = host.Address;
                ReadHostingIdentity();
            }
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
                    await ReadPeerAsync(inbound, token, store);
                    SetStatus(PeerState.WaitingForReconnect);
                }
                catch (Exception error) when (!token.IsCancellationRequested && error is not PeerDeliveryStorageException &&
                    error is AuthenticationException or IOException or InvalidDataException or OperationCanceledException or DecoderFallbackException)
                {
                    SetStatus(PeerState.RejectedPeer);
                    await Task.Delay(200, token);
                }
            }
        }
        catch (Exception error) when (token.IsCancellationRequested &&
            error is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            SetStatus(PeerState.Disconnected);
        }
        catch (AuthenticationException) { SetStatus(PeerState.AuthenticationFailed); }
        catch (PeerDeliveryStorageException) { SetStatus(PeerState.DeliveryUnavailable); }
        catch (Exception error) when (error is InvalidDataException or UnauthorizedAccessException or JsonException)
        { SetStatus(PeerState.CredentialFailed); }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or
            ObjectDisposedException or InvalidOperationException or DecoderFallbackException)
        { SetStatus(PeerState.OperationFailed); }
        finally
        {
            if (store is not null && (remote ?? registeredPersistent) is { } reopen &&
                !stopping && !closed && transport.Lifetime.IsCancellationRequested)
            {
                resumeAfterTor = (reopen, fingerprint, registeredPersistent is not null);
                SetStatus(PeerState.WaitingForTor);
            }
            attempt = null;
            request.Dispose();
            hostedAddress = "";
            Changed(nameof(HostedAddress));
            if (hostAction is not null) ReadHostingIdentity();
            Invalidate();
            TryResume(transport);
        }
    }

    private async Task ConnectAsync(IOnionPeerTransport transport, OnionAddress address, PeerIdentity local,
        string fingerprint, PeerDeliveryStore? store, PeerProtocol protocol, CancellationToken token)
    {
        for (int attemptNumber = 0; ; attemptNumber++)
        {
            token.ThrowIfCancellationRequested();
            RequireTrusted(local, fingerprint);
            try
            {
                await using var outgoing = await PeerConnection.ConnectAsync(transport, address, local, fingerprint, token, protocol);
                RequireTrusted(local, fingerprint);
                await ReadPeerAsync(outgoing, token, store);
                return; // An authenticated Close intentionally ends this connection.
            }
            catch (Exception error) when (store is not null && !token.IsCancellationRequested &&
                error is not PeerDeliveryStorageException &&
                attemptNumber < MaximumReconnectAttempts && error is IOException or SocketException or OperationCanceledException)
            {
                retry = attemptNumber + 1;
                SetStatus(PeerState.Retrying);
                await Task.Delay(TimeSpan.FromSeconds(1 << attemptNumber), token);
                SetStatus(PeerState.Connecting);
            }
        }
    }

    private void TryResume(IOnionPeerTransport? previous = null)
    {
        if (resumeAfterTor is { } request && request.Fingerprint == SelectedPeer?.Fingerprint
            && (request.Hosting ? !invalidHostingIdentity && request.Address == savedHostingAddress
                : string.Equals(request.Address.Host, OnionAddress.Trim(), StringComparison.OrdinalIgnoreCase))
            && CanBegin() && tor.GetPeerTransport() is { } current && (previous is null || current.Lifetime != previous.Lifetime)
            && !current.Lifetime.IsCancellationRequested)
            Begin(request.Hosting, request.Hosting ? HostingIdentityAction.Restore : null);
    }

    private async Task ReadPeerAsync(PeerConnection peer, CancellationToken token, PeerDeliveryStore? store)
    {
        token.ThrowIfCancellationRequested();
        connection = peer;
        SetStatus(PeerState.Connected);
        Invalidate();
        try
        {
            if (store is not null)
            {
                var session = new ReliablePeerSession(peer, store, data =>
                {
                    void Publish()
                    {
                        if (ReferenceEquals(delivery, store) && ReferenceEquals(connection, peer)) ApplyDelivery(data);
                    }
                    if (dispatcher is null) Publish();
                    else dispatcher.Post(Publish);
                });
                reliable = session;
                await session.RunAsync(token);
                return;
            }
            while (await peer.ReceiveAsync(token) is { } message)
            {
                lastMessage = message;
                Changed(nameof(LastMessage));
            }
        }
        finally
        {
            var previous = reliable;
            reliable = null;
            connection = null;
            if (store is not null && ReferenceEquals(delivery, store))
            {
                try { ApplyDelivery(previous?.DisplaySnapshot() ?? store.Snapshot()); }
                catch (Exception error) when (ProfileFailure(error))
                { DeliveryNotice = "Could not refresh the private delivery state."; }
            }
            Invalidate();
        }
    }

    private async Task SendAsync()
    {
        if (delivery is { } store && identity is { } local && SelectedPeer is { } contact)
        {
            try
            {
                RequireTrusted(local, contact.Fingerprint);
                store.Enqueue(Draft);
                Draft = "";
                reliable?.Wake();
                ApplyDelivery(reliable?.DisplaySnapshot() ?? store.Snapshot());
                DeliveryNotice = "Queued securely. Delivery changes after the peer acknowledges it.";
            }
            catch (ArgumentException) { DeliveryNotice = "Enter a message of 1 to 8192 UTF-8 bytes."; }
            catch (AuthenticationException) { DeliveryNotice = "This contact is no longer trusted."; }
            catch (Exception error) when (ProfileFailure(error)) { DeliveryNotice = "The private outbox could not accept this message."; }
            return;
        }
        if (connection is not { } peer || attempt is not { } request) return;
        var payload = Draft;
        await peer.SendAsync(payload, request.Token);
        if (Draft == payload) Draft = "";
    }

    private async Task DisconnectAsync()
    {
        resumeAfterTor = null;
        if (attempt is not { } request) { Invalidate(); return; }
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

    private void SetStatus(PeerState value)
    {
        var previous = state;
        if (!Set(ref state, value, nameof(Status))) return;
        if (visibleDelivery is { } snapshot && (previous == PeerState.Retrying || value == PeerState.Retrying))
            ApplyDelivery(snapshot);
        if (SelectedPeer is not { } peer ||
            value is not (PeerState.Registering or PeerState.Connecting or PeerState.Hosting or PeerState.Connected
                or PeerState.Disconnected or PeerState.WaitingForReconnect or PeerState.RejectedPeer
                or PeerState.AuthenticationFailed or PeerState.OperationFailed or PeerState.CleanupUnconfirmed or
                PeerState.DeliveryUnavailable or PeerState.Retrying)
                && !(value == PeerState.WaitingForTor && resumeAfterTor is not null)) return;
        connectionHistory.TryGetValue(peer.Fingerprint, out var history);
        connectionHistory[peer.Fingerprint] = [new(DateTimeOffset.UtcNow, Status), .. (history ?? []).Take(11)];
        Changed(nameof(ConnectionHistory));
    }

    private void ApplyDelivery(PeerDeliverySnapshot snapshot)
    {
        if (snapshot.Revision < revision) return;
        revision = snapshot.Revision;
        visibleDelivery = snapshot;
        Set(ref saveHistory, snapshot.HistoryEnabled, nameof(SaveHistory));
        HistoryRows = snapshot.History.Select(item => new PeerMessageRow(item.Id.ToString("N")[..8],
            item.Incoming ? "Incoming" : "Outgoing", "Archived", item.Text)).Reverse().ToArray();
        DeliveryRows = snapshot.Outbox.Select(item => new PeerMessageRow(item.Id.ToString("N")[..8], "Outgoing",
                item.Status switch
                {
                    PeerDeliveryStatus.Delivered => "Acknowledged",
                    PeerDeliveryStatus.Expired => "Expired",
                    _ when state == PeerState.Retrying => "Retrying",
                    PeerDeliveryStatus.Sent => "Sent · awaiting acknowledgement",
                    PeerDeliveryStatus.SendFailed => "Send failed · kept in queue",
                    _ => item.Status.ToString()
                }, item.Text))
            .Concat(snapshot.Inbox.Select(item => new PeerMessageRow(item.Id.ToString("N")[..8], "Incoming", "Received", item.Text)))
            .ToArray();
    }

    private void Invalidate()
    {
        Changed(nameof(CanEditPeer));
        Changed(nameof(CanEditSelectedPeer));
        Changed(nameof(CanApproveHostingRotation));
        HostCommand.Invalidate(); ConnectCommand.Invalidate(); SendCommand.Invalidate(); DisconnectCommand.Invalidate();
        CreateIdentityCommand.Invalidate(); SavePeerCommand.Invalidate(); RevokePeerCommand.Invalidate();
        SaveEndpointCommand.Invalidate(); RotatePeerCommand.Invalidate();
        CreateHostingCommand.Invalidate(); RotateHostingCommand.Invalidate(); TransientHostCommand.Invalidate();
    }
}
