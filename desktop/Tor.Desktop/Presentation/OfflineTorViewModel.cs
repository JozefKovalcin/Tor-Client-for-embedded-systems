using System.Globalization;
using Tor.Adapters.Windows;
using Tor.Application;
using Tor.Core;
using TorManager.Desktop.Services;

namespace TorManager.Desktop.Presentation;

/// <summary>Projects one Windows session service subscription onto the UI dispatcher.</summary>
internal sealed class OfflineTorViewModel : ObservableModel, IAsyncDisposable
{
    private readonly IOfflineTorService _service;
    private readonly IOnlineTorService? _online;
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private OfflineTorSnapshot _snapshot;
    private Task? _reader;
    private Task? _close;
    private CancellationTokenSource? _startRequest;
    private CancellationTokenSource? _onlineAction;
    private bool _refreshing;
    private bool _stopping;
    private bool _closing;
    private string? _actionError;
    private string? _monitorError;
    private string? _onlineNotice;
    private string _socksPortText = IOnlineTorService.DefaultSocksPort.ToString(CultureInfo.InvariantCulture);
    private bool _isOnline;
    private bool _restarting;

    public AsyncCommand StartCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand RestartCommand { get; }
    public AsyncCommand NewIdentityCommand { get; }
    public AsyncCommand CheckConnectivityCommand { get; }
    public PeerViewModel? Peer { get; }
    public TaskCard? CurrentTask { get; private set; }
    public IReadOnlyList<TaskCard> Tasks { get; private set; } = [];
    public IReadOnlyList<LogCard> Logs { get; private set; } = [];
    public bool CanSwitchToDemo => !_closing && _startRequest is null && !_stopping &&
        !OwnsOrStartsProcess(_snapshot) && !OwnsOrStartsProcess(_service.Snapshot);
    public bool SupportsOnline => _online is not null;
    public bool IsOnline => _isOnline;
    public bool CanEditSocksPort => IsOnline && CanSwitchToDemo;
    public string SocksPortText
    {
        get => _socksPortText;
        set
        {
            if (!CanEditSocksPort || !Set(ref _socksPortText, value)) return;
            NotifyStatus();
        }
    }
    public bool InvalidSocksPort => IsOnline && !TrySocksPort(out _);
    public string RuntimeDigest => _snapshot.PackageStatus == RuntimePackageStatus.Available
        ? PinnedWindowsRuntime.Executable.Sha256 : "Unavailable pending verification";
    public string TorVersionText => _snapshot.Online?.TorVersion ?? "Version not reported";
    public string SocksStatusText => _snapshot.Online?.SocksPort is { } port
        ? FormattableString.Invariant($"SOCKS5 at 127.0.0.1:{port}") : "No SOCKS5 listener confirmed";
    public string CircuitStatusText => _snapshot.Online?.CircuitEstablished switch
    {
        true => "Tor reports a circuit is established",
        false => "Tor reports no circuit is established",
        null => "Circuit status not reported"
    };
    public string? OnlineNotice => _onlineAction is { IsCancellationRequested: false } ? "Waiting for Tor…" : _onlineNotice;
    public bool HasOnlineNotice => OnlineNotice is not null;
    public bool ShowBootstrapProgress => IsOnline && _snapshot.SessionStatus is
        (TorSessionStatus.Starting or TorSessionStatus.BootstrappingOnline or TorSessionStatus.RunningOnline);
    public bool BootstrapIndeterminate => _snapshot.BootstrapPercent is null;
    public double BootstrapValue => _snapshot.BootstrapPercent ?? 0;

    private static bool OwnsOrStartsProcess(OfflineTorSnapshot snapshot) =>
        snapshot.CanStop || snapshot.ProcessId is not null ||
        snapshot.SessionStatus is TorSessionStatus.Starting or TorSessionStatus.RunningOffline
            or TorSessionStatus.BootstrappingOnline or TorSessionStatus.RunningOnline or TorSessionStatus.Stopping;

    private bool TrySocksPort(out int port) => int.TryParse(SocksPortText, NumberStyles.None,
        CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    public bool SelectOnline(bool online)
    {
        if (_isOnline == online) return true;
        if (!CanSwitchToDemo || (online && !SupportsOnline)) return false;
        _isOnline = online;
        _onlineNotice = null;
        Changed(nameof(IsOnline));
        NotifyStatus();
        return true;
    }

    public string PackageText => _snapshot.PackageStatus switch
    {
        RuntimePackageStatus.Available => "Verified pinned runtime",
        RuntimePackageStatus.Missing => "Tor executable or required DLL missing · Start blocked",
        RuntimePackageStatus.Invalid => "Runtime hash or path is invalid · Start blocked",
        RuntimePackageStatus.Blocked => "Runtime check blocked",
        _ => _refreshing ? "Checking pinned runtime…" : "Runtime verification pending"
    };
    public string SessionText => _snapshot.SessionStatus switch
    {
        TorSessionStatus.Starting => "Starting",
        TorSessionStatus.RunningOffline => "Running offline",
        TorSessionStatus.BootstrappingOnline => "Bootstrapping on the Tor network",
        TorSessionStatus.RunningOnline => "Running online",
        TorSessionStatus.Stopping => "Stopping",
        TorSessionStatus.Failed => "Failed",
        TorSessionStatus.Interrupted => "Interrupted",
        _ => _startRequest is not null ? "Starting" : "Stopped"
    };
    public string ControlText => _snapshot.ControlStatus switch
    {
        TorControlStatus.Authenticated => "Authenticated with SAFECOOKIE",
        TorControlStatus.Authenticating => "Authenticating",
        _ => "Disconnected"
    };
    public string ProcessText => _snapshot.ProcessId is { } id
        ? FormattableString.Invariant($"PID {id}")
        : "No process ID reported";
    public string BootstrapText => _snapshot.BootstrapPercent is { } percent
        ? FormattableString.Invariant($"{percent}% · {_snapshot.BootstrapTag ?? "phase unavailable"}")
        : "No bootstrap data received";
    public string TaskText => CurrentTask is { } task ? $"{task.StateLabel} · {task.Phase}" : "No task recorded";
    public string OutcomeText => CurrentTask switch
    {
        { RawExitCode: { } code } => FormattableString.Invariant($"Original exit code {code}"),
        { TerminationSignal: { } signal } => FormattableString.Invariant($"Termination signal {signal}"),
        _ => "No exit or termination result available"
    };
    public string? ErrorText => _actionError ?? _monitorError ?? _snapshot.LastError;
    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    public string RecoveryText => _snapshot.SessionStatus == TorSessionStatus.Interrupted
        ? "The previous run was interrupted. A new Start request is required."
        : "Each Start request is explicit. Existing private state is retained.";

    public OfflineTorViewModel(IOfflineTorService service, IUiDispatcher dispatcher, PeerProfileStore? peerProfile = null)
    {
        Peer = service is IPeerTorService peers ? new PeerViewModel(peers, peerProfile) : null;
        _service = service;
        _online = service as IOnlineTorService;
        _dispatcher = dispatcher;
        _snapshot = service.Snapshot;
        StartCommand = new(() => StartAsync(restart: false), () => ReportActionError("Start failed. Review the task and runtime status."),
            () => !_closing && !_refreshing && !_stopping && _startRequest is null && _service.Snapshot.CanStart &&
                (!IsOnline || TrySocksPort(out _)));
        StopCommand = new(StopAsync, () => ReportActionError("Stop did not confirm process termination. Review the live status."),
            () => !_closing && !_stopping && (_startRequest is not null || _service.Snapshot.CanStop));
        RefreshCommand = new(RefreshAsync, () => ReportActionError("Runtime verification could not finish."),
            () => !_closing && !_refreshing && !_stopping && _startRequest is null && !_service.Snapshot.CanStop);
        RestartCommand = new(() => StartAsync(restart: true), () => ReportActionError("Online restart failed. Review the task and runtime status."),
            () => IsOnline && !_closing && !_refreshing && !_stopping && _startRequest is null &&
                TrySocksPort(out _) && _service.Snapshot is { Online: not null, CanStop: true,
                    SessionStatus: TorSessionStatus.BootstrappingOnline or TorSessionStatus.RunningOnline });
        NewIdentityCommand = new(() => ApplyOnlineActionAsync((service, token) => service.RequestNewIdentityAsync(token)),
            () => ReportActionError("Tor did not confirm the identity request."), CanInvokeOnlineAction);
        CheckConnectivityCommand = new(() => ApplyOnlineActionAsync((service, token) => service.CheckConnectivityAsync(token)),
            () => ReportActionError("The SOCKS5 connectivity check could not finish."), CanInvokeOnlineAction);
        Apply(_snapshot);
    }

    public async Task OpenAsync()
    {
        if (_closing || _reader is not null) return;
        _reader = WatchAsync(_lifetime.Token);
        try { await RefreshAsync(); }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception) { ReportActionError("Runtime verification could not finish. Use Check runtime to try again."); }
    }

    private async Task WatchAsync(CancellationToken token)
    {
        try
        {
            await foreach (var snapshot in _service.WatchAsync(token).ConfigureAwait(false))
                await _dispatcher.ApplyAsync(() => Apply(snapshot), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            try
            {
                await _dispatcher.ApplyAsync(() =>
                {
                    _monitorError = "Live status updates stopped. Controls remain subject to the backend state.";
                    NotifyStatus();
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    private async Task RefreshAsync()
    {
        var token = _lifetime.Token;
        _refreshing = true;
        _actionError = null;
        NotifyStatus();
        try { await Task.Run(() => _service.RefreshAsync(token), token); }
        finally { _refreshing = false; NotifyStatus(); }
    }

    private async Task StartAsync(bool restart)
    {
        var online = IsOnline ? _online : null;
        var port = 0;
        if (IsOnline && (online is null || !TrySocksPort(out port))) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _onlineAction?.Cancel();
        _onlineNotice = null;
        _startRequest = request;
        _restarting = restart;
        _actionError = null;
        NotifyStatus();
        try
        {
            if (online is not null)
                await Task.Run(() => restart ? online.RestartOnlineAsync(port, request.Token) : online.StartOnlineAsync(port, request.Token), request.Token);
            else
                await Task.Run(() => _service.StartAsync(request.Token), request.Token);
        }
        finally { _startRequest = null; _restarting = false; NotifyStatus(); }
    }

    private async Task StopAsync()
    {
        var token = _lifetime.Token;
        _stopping = true;
        _onlineAction?.Cancel();
        _onlineNotice = null;
        _actionError = null;
        NotifyStatus();
        try
        {
            // A stop request can arrive before the preflight has submitted a task.
            if (!_service.Snapshot.CanStop || _restarting) _startRequest?.Cancel();
            await Task.Run(() => _service.StopAsync(token), token);
        }
        finally { _stopping = false; NotifyStatus(); }
    }

    private bool CanInvokeOnlineAction() => IsOnline && !_closing && !_stopping && _startRequest is null &&
        _onlineAction is null && _service.Snapshot.Online?.CanUseOnlineActions == true;

    private async Task ApplyOnlineActionAsync(Func<IOnlineTorService, CancellationToken, Task<OnlineActionResult>> action)
    {
        if (_online is not { } online || !CanInvokeOnlineAction()) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _onlineAction = request;
        _actionError = null;
        _onlineNotice = null;
        NotifyStatus();
        try
        {
            var result = await Task.Run(() => action(online, request.Token), request.Token);
            if (!request.IsCancellationRequested && _service.Snapshot.Online?.CanUseOnlineActions == true)
                _onlineNotice = result.Message;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            _onlineAction = null;
            NotifyStatus();
        }
    }

    private void ReportActionError(string message)
    {
        if (_closing) return;
        _actionError = message;
        NotifyStatus();
    }

    private void Apply(OfflineTorSnapshot snapshot)
    {
        if (_snapshot.Online?.CanUseOnlineActions == true && snapshot.Online?.CanUseOnlineActions != true)
        {
            _onlineAction?.Cancel();
            _onlineNotice = null;
        }
        _snapshot = snapshot;
        CurrentTask = snapshot.CurrentTask is { } current ? TaskCard.FromProgress(current) : null;
        // Keep the latest bounded view of each stream and replace it atomically on the UI thread.
        Tasks = snapshot.Tasks.OrderByDescending(task => task.TimestampUtc).Take(100)
            .Select(TaskCard.FromProgress).ToArray();
        Logs = snapshot.Logs.TakeLast(100).Reverse().Select(log => new LogCard(
            log.Sequence.ToString(CultureInfo.InvariantCulture), log.TaskId,
            $"{log.TimestampUtc:HH:mm:ss}Z · {log.Event}",
            log.RawExitCode is { } code ? FormattableString.Invariant($"Original exit code {code}") :
            log.TerminationSignal is { } signal ? FormattableString.Invariant($"Termination signal {signal}") :
            string.Empty)).ToArray();
        Changed(nameof(CurrentTask));
        Changed(nameof(Tasks));
        Changed(nameof(Logs));
        NotifyStatus();
    }

    private void NotifyStatus()
    {
        Peer?.SetAvailable(IsOnline && !_closing && _snapshot.Online?.CanUseOnlineActions == true);
        Changed(nameof(PackageText));
        Changed(nameof(SessionText));
        Changed(nameof(ControlText));
        Changed(nameof(ProcessText));
        Changed(nameof(BootstrapText));
        Changed(nameof(TaskText));
        Changed(nameof(OutcomeText));
        Changed(nameof(ErrorText));
        Changed(nameof(HasError));
        Changed(nameof(RecoveryText));
        Changed(nameof(CanSwitchToDemo));
        Changed(nameof(CanEditSocksPort));
        Changed(nameof(InvalidSocksPort));
        Changed(nameof(RuntimeDigest));
        Changed(nameof(TorVersionText));
        Changed(nameof(SocksStatusText));
        Changed(nameof(CircuitStatusText));
        Changed(nameof(OnlineNotice));
        Changed(nameof(HasOnlineNotice));
        Changed(nameof(ShowBootstrapProgress));
        Changed(nameof(BootstrapIndeterminate));
        Changed(nameof(BootstrapValue));
        StartCommand.Invalidate();
        StopCommand.Invalidate();
        RefreshCommand.Invalidate();
        RestartCommand.Invalidate();
        NewIdentityCommand.Invalidate();
        CheckConnectivityCommand.Invalidate();
    }

    public ValueTask DisposeAsync() => new(_close ??= CloseAsync());

    private async Task CloseAsync()
    {
        _closing = true;
        _lifetime.Cancel();
        NotifyStatus();
        try
        {
            try { if (Peer is not null) await Peer.DisposeAsync(); }
            finally { await Task.Run(async () => await _service.DisposeAsync()).ConfigureAwait(false); }
            if (_service.Snapshot.ProcessId is not null)
                throw new InvalidOperationException("Tor process termination was not confirmed.");
        }
        finally
        {
            try { if (_reader is not null) await _reader.ConfigureAwait(false); }
            finally { _lifetime.Dispose(); }
        }
    }
}
