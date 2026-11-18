using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Tor.Application;
using Tor.Control;
using Tor.Core;
using TorManager.Desktop.Presentation;
using TorManager.Desktop.Services;

internal static class OnlineDesktopTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    public static async Task RunAsync(ITaskFeed demo, SettingsStore settings, Action<bool, string> check)
    {
        var service = new FakeService();
        bool customVerified = false;
        await using var shell = new ShellViewModel(demo, new ImmediateDispatcher(), settings, service,
            activeRuntime: RuntimeSource.Bundled, hasVerifiedCustomRuntime: () => customVerified);
        await shell.OpenAsync().WaitAsync(Limit);
        var model = shell.Offline!;
        check(shell.Pages.Single(page => page.ShowPeers).Title == "Peers",
            "paired contact navigation is available before switching to Real Online");
        check(shell.ModeHeading == "Real Offline" && !model.IsOnline && service.Watchers == 1,
            "online-capable Windows shell still opens in Real Offline with one watcher");
        shell.ShowDemo();
        shell.ShowRealOnline();
        check(shell.ModeHeading == "Real Online" && model.IsOnline && service.OnlineStarts == 0 && service.OfflineStarts == 0 &&
            model.SocksPortText == "9150" && !model.NewIdentityCommand.CanExecute(null) && !model.CheckConnectivityCommand.CanExecute(null),
            "selecting Real Online authorizes no start or proxy probe");

        foreach (var badPort in new[] { "0", "65536", "not a port", "127.0.0.1", "" })
        {
            model.SocksPortText = badPort;
            if (!model.InvalidSocksPort || model.StartCommand.CanExecute(null))
                throw new InvalidOperationException("Invalid port offered a start");
        }
        model.SocksPortText = "19150";
        model.StartCommand.Execute(null);
        model.StartCommand.Execute(null);
        await service.StartSubmitted.Task.WaitAsync(Limit);
        await ObserveAsync(model, () => model.ShowBootstrapProgress && model.BootstrapIndeterminate);
        shell.ShowRealOffline();
        shell.ShowDemo();
        check(shell.IsOnline && service.OnlineStarts == 1 && service.Port == 19150 && !model.CanEditSocksPort &&
            model.SocksStatusText == "No SOCKS5 listener confirmed" && model.TorVersionText == "Version not reported",
            "explicit start forwards a validated port once and preserves the active workspace while status is unknown");
        if (OperatingSystem.IsWindows())
        {
            shell.ShowDeveloperCommand.Execute(null);
            check(shell.IsDeveloper && shell.IsOnline && shell.CurrentPage.ShowDeveloper &&
                shell.Pages.Any(page => page.ShowPeers) && service.OnlineStarts == 1 && service.Watchers == 1,
                "Developer retains the active real connection, peer page and current Tor watcher");
            shell.ShowRunOnlyCommand.Execute(null);
            check(!shell.IsDeveloper && shell.IsOnline && model.IsOnline && service.OnlineStarts == 1,
                "returning to Run Only retains the current Tor session");
        }
        shell.SelectBundledRuntimeCommand.Execute(null);
        shell.SelectCustomRuntimeCommand.Execute(null);
        check(settings.Load(out _).PreferredRuntime == RuntimeSource.Bundled,
            "choosing custom without a verified approval preserves the previous setting");
        customVerified = true;
        shell.SelectCustomRuntimeCommand.Execute(null);
        check(settings.Load(out _).PreferredRuntime == RuntimeSource.CustomVerified && shell.ActiveRuntime == "Bundled Tor" &&
            shell.IsOnline && service.OnlineStarts == 1, "runtime selection is persisted for next launch and leaves the active session intact");

        service.Publish(service.Snapshot with
        {
            SessionStatus = TorSessionStatus.BootstrappingOnline, ProcessId = 71, ControlStatus = TorControlStatus.Authenticated,
            BootstrapPercent = 33, BootstrapTag = "connecting", Online = new(19150, 19150, "0.4.9.14", false, false),
            VerifiedRuntimeSha256 = new string('a', 64)
        });
        await ObserveAsync(model, () => model.BootstrapText == "33% · connecting");
        check(model.ProcessText == "PID 71" && model.ControlText == "Authenticated with SAFECOOKIE" &&
            model.BootstrapValue == 33 && !model.BootstrapIndeterminate && model.SocksStatusText == "SOCKS5 at 127.0.0.1:19150" &&
            model.TorVersionText == "0.4.9.14" && model.CircuitStatusText == "Tor reports no circuit is established" &&
            model.RuntimeDigest == new string('a', 64) && !model.CheckConnectivityCommand.CanExecute(null),
            "listener availability, bootstrap progress, control, circuit status and pinned provenance remain distinct");

        service.Publish(service.Snapshot with
        {
            SessionStatus = TorSessionStatus.RunningOnline, BootstrapPercent = 100, BootstrapTag = "done",
            Online = new(19150, 19150, "0.4.9.14", true, true)
        });
        await ObserveAsync(model, () => model.SessionText == "Running online" && model.NewIdentityCommand.CanExecute(null));
        model.NewIdentityCommand.Execute(null);
        await ObserveAsync(model, () => model.OnlineNotice == "Request submitted; application unconfirmed.");
        check(service.IdentityRequests == 1 && model.CircuitStatusText == "Tor reports a circuit is established",
            "an acknowledged NEWNYM request never becomes a claim of identity change");

        model.CheckConnectivityCommand.Execute(null);
        model.CheckConnectivityCommand.Execute(null);
        await service.ProbeStarted.Task.WaitAsync(Limit);
        check(service.Probes == 1 && !model.NewIdentityCommand.CanExecute(null), "the explicit connectivity test runs once and disables overlapping actions");
        model.RestartCommand.Execute(null);
        await service.ProbeCancelled.Task.WaitAsync(Limit);
        await service.Restarted.Task.WaitAsync(Limit);
        check(service.Restarts == 1 && service.Port == 19150 && model.OnlineNotice is null,
            "restart cancels an in-flight probe and discards its session result");

        model.StopCommand.Execute(null);
        await service.Stopped.Task.WaitAsync(Limit);
        await ObserveAsync(model, () => model.SessionText == "Stopped" && model.CanSwitchToDemo);
        shell.ShowRealOffline();
        check(shell.ModeHeading == "Real Offline" && !model.IsOnline && service.Watchers == 1 && service.OfflineStarts == 0 &&
            !model.CheckConnectivityCommand.CanExecute(null), "confirmed stop permits returning to offline without starting a process");
        Console.WriteLine("online desktop interaction assertions PASS");
    }

    private static async Task ObserveAsync(OfflineTorViewModel model, Func<bool> observed)
    {
        var done = Signal();
        void OnChange(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (observed()) done.TrySetResult();
        }
        model.PropertyChanged += OnChange;
        try
        {
            if (observed()) return;
            await done.Task.WaitAsync(Limit);
        }
        finally { model.PropertyChanged -= OnChange; }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static async Task RunPeerAsync(string fresh, Action<bool, string> check)
    {
        check(Path.IsPathFullyQualified(fresh) && !Directory.Exists(fresh), "peer UI uses an unused isolated profile");
        var profile = new PeerProfileStore(fresh);
        var hosting = new HostedOnionStore(fresh);
        var service = new FakeService();
        await using var model = new PeerViewModel(service, profile, hostingStore: hosting);
        model.ReadExistingProfile();
        check(model.CreateIdentityCommand.CanExecute(null) && !Directory.Exists(fresh), "profile discovery creates no identity or directory");
        model.CreateIdentityCommand.Execute(null);
        string original = model.Fingerprint;
        check(original.Length == 64 && service.OnlineStarts == 0, "explicit identity creation works before starting Tor");
        using var fixture = new PeerIdentity();
        model.Label = "Test peer";
        model.PeerFingerprint = fixture.Fingerprint;
        model.ComparedFingerprint = true;
        model.PeerFingerprint = new string('F', 64);
        check(!model.SavePeerCommand.CanExecute(null), "editing the typed pin resets consent");
        model.PeerFingerprint = fixture.Fingerprint;
        model.ComparedFingerprint = true;
        const string address = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.onion";
        model.PairingAddress = address;
        model.SavePeerCommand.Execute(null);
        check(!model.SaveHistory && model.HistoryRows.Count == 0, "conversation history starts disabled");
        model.SaveHistory = true;
        model.Draft = "durable fixture";
        model.SendCommand.Execute(null);
        check(model.DeliveryRows.Single().Status == "Pending" && service.OnlineStarts == 0,
            "queued offline message is pending and starts no transport");
        await using (var reopened = new PeerViewModel(service, profile))
        {
            reopened.ReadExistingProfile();
            reopened.SelectedPeer = reopened.Contacts.Single();
            check(reopened.Fingerprint == original && reopened.DeliveryRows.Single().Text == "durable fixture" && reopened.SaveHistory,
                "identity, pin, pending message and explicit history preference survive a new model instance");
        }
        using var first = new PausedPeerTransport();
        service.PeerTransport = first;
        model.SetAvailable(true);
        model.ConnectCommand.Execute(null);
        await first.Started.Task.WaitAsync(Limit);
        model.SetAvailable(false);
        first.Dispose();
        await WaitUntilAsync(() => model.CanEditPeer);
        using var second = new PausedPeerTransport();
        service.PeerTransport = second;
        model.SetAvailable(true);
        await second.Started.Task.WaitAsync(Limit);
        check(second.Address?.Host == address, "Tor restart retries only the original paired onion");
        model.SetAvailable(false);
        second.Dispose();
        await WaitUntilAsync(() => model.CanEditPeer);
        model.OnionAddress = new string('b', 56) + ".onion";
        using var third = new PausedPeerTransport();
        service.PeerTransport = third;
        model.SetAvailable(true);
        check(model.CanEditPeer && !third.Started.Task.IsCompleted, "editing the onion cancels the pending reconnect intent");
        check(model.ConnectionHistory.Any(item => item.Status.Contains("Start Tor")) &&
            model.ConnectionHistory.Any(item => item.Status == "Connecting through Tor…"),
            "the selected contact shows connection and restart events");

        var previous = model.SelectedPeer!;
        model.OnionAddress = "not-an-onion";
        model.SaveEndpointCommand.Execute(null);
        check(model.SelectedPeer == previous && model.Status.Contains("v3 onion"), "an invalid address leaves the saved contact intact");
        string updatedAddress = new string('c', 56) + ".onion";
        model.OnionAddress = updatedAddress;
        model.Draft = "unsent for original contact";
        model.SaveEndpointCommand.Execute(null);
        check(model.SelectedPeer is { } updated && updated.Fingerprint == fixture.Fingerprint &&
            updated.Onion == updatedAddress && model.Draft.Length != 0 && model.DeliveryRows.Single().Status == "Pending",
            "saving an address preserves the authenticated recipient, draft and durable queue");

        using var replacement = new PeerIdentity();
        model.Label = "Replacement peer";
        model.PeerFingerprint = replacement.Fingerprint;
        model.PairingAddress = updatedAddress;
        check(!model.RotatePeerCommand.CanExecute(null), "rotation needs fresh out-of-band confirmation");
        model.ComparedFingerprint = true;
        model.RotatePeerCommand.Execute(null);
        check(model.Contacts.Single().Fingerprint == replacement.Fingerprint && model.Draft.Length == 0 &&
            model.DeliveryRows.Count == 0 && model.ConnectionHistory.Count == 0 && !model.SaveHistory,
            "rotating a peer pin leaves the old draft, queue, events and history out of the new conversation");

        using var sameLocal = profile.OpenIdentity()!;
        var persisted = profile.ReadContacts(sameLocal);
        check(sameLocal.Fingerprint == original && persisted.Single(peer => peer.Revoked).Fingerprint == fixture.Fingerprint &&
            persisted.Single(peer => !peer.Revoked).Onion == updatedAddress, "local identity, revocation and replacement survive reopening");
        try
        {
            profile.OpenDelivery(sameLocal, previous);
            check(false, "revoked pin must not reopen an outbox");
        }
        catch (InvalidOperationException) { check(true, "revoked pin cannot reopen the old outbox"); }

        using var hostTor = new HostingTransport(1);
        service.PeerTransport = hostTor;
        check(!model.HostCommand.CanExecute(null) && model.CreateHostingCommand.CanExecute(null),
            "a fresh profile requires explicit saved identity creation");
        model.CreateHostingCommand.Execute(null);
        await WaitUntilAsync(() => model.HostedAddress.Length != 0);
        var saved = model.HostedAddress;
        check(saved == model.SavedHostingAddress && hostTor.Generations == 1 &&
            profile.ReadContacts(sameLocal).SequenceEqual(persisted), "hosting generation preserves the separate TLS and trust state");
        string storedFile = Path.Combine(fresh, "onion-hosting", "identity.dpapi");
        var stored = File.ReadAllBytes(storedFile);
        check(stored.AsSpan().IndexOf(HostingTransport.FixtureKey(1)) < 0, "the saved file contains no clear fixture key");

        model.SetAvailable(false);
        hostTor.Dispose();
        await WaitUntilAsync(() => model.CanEditPeer);
        using var restarted = new HostingTransport(2);
        service.PeerTransport = restarted;
        model.SetAvailable(true);
        await WaitUntilAsync(() => model.HostedAddress == saved);
        check(restarted.Restores == 1 && restarted.Generations == 0, "Tor restart hosts the same saved key");
        model.DisconnectCommand.Execute(null);
        await WaitUntilAsync(() => model.CanEditPeer);

        await using var reopenedHost = new PeerViewModel(service, profile, hostingStore: new HostedOnionStore(fresh));
        reopenedHost.ReadExistingProfile();
        reopenedHost.SelectedPeer = reopenedHost.Contacts.Single();
        reopenedHost.SetAvailable(true);
        check(reopenedHost.SavedHostingAddress == saved && !reopenedHost.RotateHostingCommand.CanExecute(null),
            "Manager reopening sees the same saved address and requires separate rotation approval");
        reopenedHost.HostCommand.Execute(null);
        await WaitUntilAsync(() => reopenedHost.HostedAddress == saved);
        reopenedHost.DisconnectCommand.Execute(null);
        await WaitUntilAsync(() => reopenedHost.CanEditPeer);
        reopenedHost.ApproveHostingRotation = true;
        reopenedHost.RotateHostingCommand.Execute(null);
        await WaitUntilAsync(() => reopenedHost.HostedAddress.Length != 0);
        check(reopenedHost.HostedAddress != saved && restarted.Generations == 1 && !reopenedHost.ApproveHostingRotation,
            "approved rotation changes the saved address and consumes consent");
        reopenedHost.DisconnectCommand.Execute(null);
        await WaitUntilAsync(() => reopenedHost.CanEditPeer);

        File.WriteAllBytes(storedFile, [1, 2, 3]);
        reopenedHost.ReadExistingProfile();
        check(!reopenedHost.CreateHostingCommand.CanExecute(null) && !reopenedHost.HostCommand.CanExecute(null) &&
            !reopenedHost.RotateHostingCommand.CanExecute(null) && File.ReadAllBytes(storedFile).SequenceEqual(new byte[] { 1, 2, 3 }),
            "unreadable protected storage blocks all identity changes and stays intact");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(Limit);
        while (!condition()) await Task.Delay(10, deadline.Token);
    }

    private sealed class PausedPeerTransport : IOnionPeerTransport, IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        public TaskCompletionSource Started { get; } = Signal();
        public CancellationToken Lifetime => cancellation.Token;
        public OnionAddress? Address { get; private set; }
        public async Task<Stream> ConnectAsync(OnionAddress address, CancellationToken token)
        {
            Address = address;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }
        public Task<OnionAddress> HostAsync(int loopbackPort, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveAsync(OnionAddress address) => throw new NotSupportedException();
        public Task WaitForPublicationAsync(OnionAddress address, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() => cancellation.Cancel();
    }

    private sealed class HostingTransport(byte generation) : IPersistentOnionPeerTransport, IDisposable
    {
        private readonly CancellationTokenSource lifetime = new();
        public CancellationToken Lifetime => lifetime.Token;
        public int Generations { get; private set; }
        public int Restores { get; private set; }
        public static byte[] FixtureKey(byte value) => Enumerable.Repeat(value, OnionServiceKey.ByteLength).ToArray();
        private static OnionAddress Address(byte value) => OnionAddress.Parse(new string((char)('a' + value), 56) + ".onion");
        public Task<(OnionAddress Address, OnionServiceKey Key)> GenerateHostAsync(int port, CancellationToken token)
        {
            Generations++;
            return Task.FromResult((Address(generation), new OnionServiceKey(FixtureKey(generation))));
        }
        public Task<OnionAddress> RestoreHostAsync(int port, OnionAddress address, OnionServiceKey key, CancellationToken token)
        {
            Restores++;
            var bytes = new byte[OnionServiceKey.ByteLength];
            key.CopyTo(bytes);
            if (Address(bytes[0]) != address || !bytes.SequenceEqual(FixtureKey(bytes[0])))
                throw new InvalidDataException("Restored fixture key changed.");
            return Task.FromResult(address);
        }
        public Task<OnionAddress> HostAsync(int port, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveAsync(OnionAddress address) => Task.CompletedTask;
        public Task WaitForPublicationAsync(OnionAddress address, CancellationToken token) => throw new NotSupportedException();
        public Task<Stream> ConnectAsync(OnionAddress address, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() => lifetime.Cancel();
    }

    private sealed class FakeService : IPeerTorService
    {
        private readonly Channel<OfflineTorSnapshot> _updates = Channel.CreateUnbounded<OfflineTorSnapshot>();
        private OfflineTorSnapshot _snapshot = new(RuntimePackageStatus.Available, TorSessionStatus.Stopped,
            TorControlStatus.Disconnected, null, null, null, null, null, [], [], true, false);
        public OfflineTorSnapshot Snapshot => Volatile.Read(ref _snapshot);
        public TaskCompletionSource StartSubmitted { get; } = Signal();
        public TaskCompletionSource ProbeStarted { get; } = Signal();
        public TaskCompletionSource ProbeCancelled { get; } = Signal();
        public TaskCompletionSource Restarted { get; } = Signal();
        public TaskCompletionSource Stopped { get; } = Signal();
        public int Watchers { get; private set; }
        public int OfflineStarts { get; private set; }
        public int OnlineStarts { get; private set; }
        public int Port { get; private set; }
        public int Restarts { get; private set; }
        public int Probes { get; private set; }
        public int IdentityRequests { get; private set; }
        public IOnionPeerTransport? PeerTransport { get; set; }
        public IOnionPeerTransport? GetPeerTransport() => PeerTransport;

        public void Publish(OfflineTorSnapshot value)
        {
            Volatile.Write(ref _snapshot, value);
            _updates.Writer.TryWrite(value);
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            OfflineStarts++;
            return Task.CompletedTask;
        }

        public Task StartOnlineAsync(int socksPort, CancellationToken cancellationToken = default)
        {
            OnlineStarts++;
            Port = socksPort;
            Publish(Snapshot with { SessionStatus = TorSessionStatus.Starting, CanStart = false, CanStop = true,
                Online = new(socksPort, null, null, null, false) });
            StartSubmitted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task RestartOnlineAsync(int socksPort, CancellationToken cancellationToken = default)
        {
            Restarts++;
            Port = socksPort;
            Restarted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Publish(Snapshot with { SessionStatus = TorSessionStatus.Stopped, ControlStatus = TorControlStatus.Disconnected,
                ProcessId = null, BootstrapPercent = null, BootstrapTag = null, Online = null, CanStart = true, CanStop = false });
            Stopped.TrySetResult();
            return Task.CompletedTask;
        }

        public Task<OnlineActionResult> RequestNewIdentityAsync(CancellationToken cancellationToken = default)
        {
            IdentityRequests++;
            return Task.FromResult(new OnlineActionResult(OnlineActionStatus.Submitted, "Request submitted; application unconfirmed."));
        }

        public async Task<OnlineActionResult> CheckConnectivityAsync(CancellationToken cancellationToken = default)
        {
            Probes++;
            ProbeStarted.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { ProbeCancelled.TrySetResult(); }
            throw new InvalidOperationException("Fake probe did not wait for cancellation");
        }

        public async IAsyncEnumerable<OfflineTorSnapshot> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Watchers++;
            yield return Snapshot;
            await foreach (var value in _updates.Reader.ReadAllAsync(cancellationToken)) yield return value;
        }

        public ValueTask DisposeAsync()
        {
            _updates.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
