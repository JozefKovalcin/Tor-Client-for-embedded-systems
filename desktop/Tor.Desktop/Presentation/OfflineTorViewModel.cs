using System.Globalization;
using Tor.Application;
using Tor.Core;
using TorManager.Desktop.Services;

namespace TorManager.Desktop.Presentation;

/// <summary>Projects one offline service subscription onto the UI dispatcher.</summary>
internal sealed class OfflineTorViewModel : ObservableModel, IAsyncDisposable
{
    private readonly IOfflineTorService _service;
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private OfflineTorSnapshot _snapshot;
    private Task? _reader;
    private Task? _close;
    private CancellationTokenSource? _startRequest;
    private bool _refreshing;
    private bool _stopping;
    private bool _closing;
    private string? _actionError;
    private string? _monitorError;

    public AsyncCommand StartCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public TaskCard? CurrentTask { get; private set; }
    public IReadOnlyList<TaskCard> Tasks { get; private set; } = [];
    public IReadOnlyList<LogCard> Logs { get; private set; } = [];
    public bool CanSwitchToDemo => !_closing && _startRequest is null && !_stopping &&
        !OwnsOrStartsProcess(_snapshot) && !OwnsOrStartsProcess(_service.Snapshot);

    private static bool OwnsOrStartsProcess(OfflineTorSnapshot snapshot) =>
        snapshot.CanStop || snapshot.ProcessId is not null ||
        snapshot.SessionStatus is TorSessionStatus.Starting or TorSessionStatus.RunningOffline or TorSessionStatus.Stopping;

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

    public OfflineTorViewModel(IOfflineTorService service, IUiDispatcher dispatcher)
    {
        _service = service;
        _dispatcher = dispatcher;
        _snapshot = service.Snapshot;
        StartCommand = new(StartAsync, () => ReportActionError("Start failed. Review the task and runtime status."),
            () => !_closing && !_refreshing && !_stopping && _startRequest is null && _service.Snapshot.CanStart);
        StopCommand = new(StopAsync, () => ReportActionError("Stop did not confirm process termination. Review the live status."),
            () => !_closing && !_stopping && (_startRequest is not null || _service.Snapshot.CanStop));
        RefreshCommand = new(RefreshAsync, () => ReportActionError("Runtime verification could not finish."),
            () => !_closing && !_refreshing && !_stopping && _startRequest is null && !_service.Snapshot.CanStop);
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

    private async Task StartAsync()
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _startRequest = request;
        _actionError = null;
        NotifyStatus();
        try { await Task.Run(() => _service.StartAsync(request.Token), request.Token); }
        finally { _startRequest = null; NotifyStatus(); }
    }

    private async Task StopAsync()
    {
        var token = _lifetime.Token;
        _stopping = true;
        _actionError = null;
        NotifyStatus();
        try
        {
            // A stop request can arrive before the preflight has submitted a task.
            if (!_service.Snapshot.CanStop) _startRequest?.Cancel();
            await Task.Run(() => _service.StopAsync(token), token);
        }
        finally { _stopping = false; NotifyStatus(); }
    }

    private void ReportActionError(string message)
    {
        if (_closing) return;
        _actionError = message;
        NotifyStatus();
    }

    private void Apply(OfflineTorSnapshot snapshot)
    {
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
        StartCommand.Invalidate();
        StopCommand.Invalidate();
        RefreshCommand.Invalidate();
    }

    public ValueTask DisposeAsync() => new(_close ??= CloseAsync());

    private async Task CloseAsync()
    {
        _closing = true;
        _lifetime.Cancel();
        NotifyStatus();
        try
        {
            await Task.Run(async () => await _service.DisposeAsync()).ConfigureAwait(false);
            if (_service.Snapshot.ProcessId is not null)
                throw new InvalidOperationException("Offline process termination was not confirmed.");
        }
        finally
        {
            try { if (_reader is not null) await _reader.ConfigureAwait(false); }
            finally { _lifetime.Dispose(); }
        }
    }
}
