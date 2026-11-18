using System.Runtime.Versioning;
using System.Text.Json;
using Tor.Adapters.Windows;
using Tor.Core;
using TorManager.Desktop.Services;

namespace TorManager.Desktop.Presentation;

internal sealed record DeveloperDependency(string Name, string Status, string Requirement, string Observation,
    string Path, string Method);
internal sealed record DeveloperBuild(string Id, string State, string Version, string Profile, string Smoke, JsonElement Details)
{
    public string Display => $"{Id} · {State} · {Version} · {Profile} · offline smoke {Smoke}";
}

[SupportedOSPlatform("windows")]
internal sealed class DeveloperViewModel : ObservableModel
{
    public static readonly string DefaultWorkspace = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TorManagerDeveloper");
    private string _workspace = DefaultWorkspace;
    private string _sources = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TorNativeBuild", "downloads");
    private string _profile = "optimized-static";
    private string _status = "Preflight has not run.", _report = "", _planDetails = "", _log = "";
    private string? _approval;
    private bool _confirmed, _ready, _closed;
    private DeveloperBuild? _selected, _compareWith;
    private CancellationTokenSource? _cancellation;
    private Task? _active;
    private IReadOnlyList<DeveloperDependency> _dependencies = [];
    private IReadOnlyList<DeveloperBuild> _builds = [];
    private readonly IReadOnlyList<AsyncCommand> _commands;
    private readonly IUiDispatcher _dispatcher;
    private readonly string? _bundledRuntime;
    private bool _approveRuntime;
    private bool _installCommitted;

    public IReadOnlyList<string> Profiles { get; } = ["optimized-static", "optimized-shared"];
    public string Workspace { get => _workspace; set { if (Set(ref _workspace, value)) InputsChanged(); } }
    public string Sources { get => _sources; set { if (Set(ref _sources, value)) InputsChanged(); } }
    public string Profile { get => _profile; set { if (Profiles.Contains(value) && Set(ref _profile, value)) InputsChanged(); } }
    public bool Idle => !_closed && _active is null && _cancellation is null;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Report { get => _report; private set => Set(ref _report, value); }
    public string PlanDetails { get => _planDetails; private set => Set(ref _planDetails, value); }
    public string Log { get => _log; private set => Set(ref _log, value); }
    public IReadOnlyList<DeveloperDependency> Dependencies { get => _dependencies; private set => Set(ref _dependencies, value); }
    public IReadOnlyList<DeveloperBuild> Builds { get => _builds; private set => Set(ref _builds, value); }
    public DeveloperBuild? Selected { get => _selected; set { if (Set(ref _selected, value)) RefreshSelection(); } }
    public DeveloperBuild? CompareWith { get => _compareWith; set { if (Set(ref _compareWith, value)) RefreshSelection(); } }
    public bool Confirmed { get => _confirmed; set { if (Set(ref _confirmed, value)) InstallCommand.Invalidate(); } }
    public bool ApproveRuntime { get => _approveRuntime; set { if (Set(ref _approveRuntime, value)) ActivateCommand.Invalidate(); } }
    public AsyncCommand ScanCommand { get; }
    public AsyncCommand PlanCommand { get; }
    public AsyncCommand InstallCommand { get; }
    public AsyncCommand BuildCommand { get; }
    public AsyncCommand ListCommand { get; }
    public AsyncCommand SmokeCommand { get; }
    public ActionCommand CancelCommand { get; }
    public AsyncCommand ActivateCommand { get; }
    public AsyncCommand RollbackCommand { get; }

    public DeveloperViewModel(IUiDispatcher dispatcher, string? bundledRuntime)
    {
        _dispatcher = dispatcher;
        _bundledRuntime = bundledRuntime;
        ScanCommand = RunCommand(WindowsDeveloperAction.Scan);
        PlanCommand = RunCommand(WindowsDeveloperAction.PlanInstall);
        InstallCommand = RunCommand(WindowsDeveloperAction.Install, () => Confirmed && _approval is not null);
        BuildCommand = RunCommand(WindowsDeveloperAction.Build, () => _ready);
        ListCommand = RunCommand(WindowsDeveloperAction.List);
        SmokeCommand = RunCommand(WindowsDeveloperAction.Smoke, () => Selected?.State == "BUILT");
        ActivateCommand = new(() => TrackAsync(WindowsDeveloperAction.Candidate), () => Status = "Activation failed.",
            () => Idle && ApproveRuntime && Selected is { State: "BUILT", Smoke: "PASS" });
        RollbackCommand = new(RollbackAsync, () => Status = "Rollback failed.", () => Idle && _bundledRuntime is not null);
        _commands = [ScanCommand, PlanCommand, InstallCommand, BuildCommand, ListCommand, SmokeCommand, ActivateCommand, RollbackCommand];
        CancelCommand = new(() => _cancellation?.Cancel(), () => _cancellation is not null && !_installCommitted);
    }

    private AsyncCommand RunCommand(WindowsDeveloperAction action, Func<bool>? condition = null) =>
        new(() => TrackAsync(action), () => Status = "Developer operation failed.", () => Idle && (condition?.Invoke() ?? true));

    private async Task TrackAsync(WindowsDeveloperAction action)
    {
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Invalidate();
        try
        {
            _active = RunAsync(action, cancellation.Token);
            await _active;
        }
        catch (OperationCanceledException) { Status = "Cancelled. Completed system transactions and saved evidence are retained."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Status = "BLOCKED · " + error.Message; }
        finally { _active = null; _cancellation = null; _installCommitted = false; Invalidate(); }
    }

    private async Task RunAsync(WindowsDeveloperAction action, CancellationToken token)
    {
        var spec = new WindowsDeveloperCommand(action, Profile, Sources, Workspace, _approval, Selected?.Id);
        Status = $"Running {action}…";
        Log = "";
        if (action is WindowsDeveloperAction.Scan or WindowsDeveloperAction.PlanInstall or WindowsDeveloperAction.Install)
        {
            _approval = null;
            Confirmed = false;
            PlanDetails = "";
        }
        if (action == WindowsDeveloperAction.Scan) _ready = false;
        if (action == WindowsDeveloperAction.Candidate)
        {
            ApproveRuntime = false;
            PrivateStorage.RequireOwnerOnly(Workspace);
            var result = await WindowsDeveloperRunner.RunAsync(spec, null, token);
            if (result.ExitCode != 0) { Status = "Activation BLOCKED."; Report = result.Errors; return; }
            var candidate = JsonSerializer.Deserialize<RuntimeCandidate>(result.Output, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("Missing verified runtime candidate.");
            token.ThrowIfCancellationRequested();
            WindowsRuntimeSelection.ForCurrentUser().Activate(candidate);
            Status = "Build approved. Restart Tor Manager to select it for future Tor sessions.";
            return;
        }
        if (action is WindowsDeveloperAction.Install or WindowsDeveloperAction.Build or WindowsDeveloperAction.Smoke)
        {
            WindowsDeveloperRunner.RequireLocal(Workspace);
            if (Workspace.Any(char.IsWhiteSpace)) throw new ArgumentException("Choose a workspace path without spaces.");
            if (!Directory.Exists(Workspace))
            {
                var parent = Path.GetDirectoryName(Workspace)!;
                for (string? current = parent; current is not null; current = Path.GetDirectoryName(current))
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("The workspace parent cannot contain links.");
                PrivateStorage.CreateNewDirectory(Workspace);
            }
            var taskId = "developer-" + Guid.NewGuid().ToString("N");
            PrivateStorage.RequireOwnerOnly(Workspace);
            var journalPath = Path.Combine(Workspace, "task-journal");
            if (!Directory.Exists(journalPath)) PrivateStorage.CreateNewDirectory(journalPath);
            PrivateStorage.RequireOwnerOnly(journalPath);
            using var journal = new TaskJournal(journalPath);
            var listener = new WindowsDeveloperRunner(AppendLog);
            var capacity = new ResourceRequest(1, 0, 0, true);
            await using var core = new TaskOrchestrator([listener], new VerifiedFileStore(Workspace), journal, capacity,
                maximumTasks: 1024, maximumTaskDuration: TimeSpan.FromHours(8));
            core.Submit([new(taskId, spec, [], capacity)]);
            using var watcher = CancellationTokenSource.CreateLinkedTokenSource(token);
            var updates = ObserveAsync(core, taskId, watcher.Token);
            try { await core.RunUntilIdleAsync(token); }
            finally { await watcher.CancelAsync(); await updates; }
            token.ThrowIfCancellationRequested();
            var outcome = core.Snapshot().Single(row => row.TaskId == taskId);
            var headline = $"{action} · {outcome.State.ToUpperInvariant()} · exit {outcome.RawExitCode?.ToString() ?? "unavailable"}";
            if (action == WindowsDeveloperAction.Install) await ReadAsync(spec with { Action = WindowsDeveloperAction.Scan }, token);
            else await ReadAsync(spec with { Action = WindowsDeveloperAction.List }, token);
            Status = headline + (action == WindowsDeveloperAction.Install ? " · dependencies rescanned" : "");
            return;
        }
        await ReadAsync(spec, token);
    }

    private async Task ObserveAsync(TaskOrchestrator core, string taskId, CancellationToken token)
    {
        try
        {
            await foreach (var row in core.WatchProgressAsync(token))
            {
                if (row.TaskId != taskId) continue;
                Status = $"{row.State} · {row.Phase}";
                if (row.State is not ("queued" or "running" or "cancelling")) break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task ReadAsync(WindowsDeveloperCommand spec, CancellationToken token)
    {
        var result = await WindowsDeveloperRunner.RunAsync(spec, null, token);
        if (result.ExitCode != 0)
        {
            Status = $"BLOCKED · exit {result.ExitCode}";
            Report = result.Errors;
            return;
        }
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement;
        if (spec.Action == WindowsDeveloperAction.Scan)
        {
            var rows = data.GetProperty("dependencies").EnumerateArray().Select(item => new DeveloperDependency(
                Text(item, "id"), Text(item, "status"), Text(item, "requirement") + (item.GetProperty("optional").GetBoolean() ? " · optional" : " · required"),
                $"{Text(item, "detectedVersion")} {Text(item, "reason")}", Text(item, "path"),
                $"{Text(item, "phase")} · {Text(item, "installationMethod")} {Text(item, "sourceUrl")}")).ToArray();
            Dependencies = rows;
            _ready = Text(data, "status") == "AVAILABLE";
            Status = _ready ? "Preflight AVAILABLE · compile is ready to start." : "Preflight BLOCKED · review required dependencies.";
            Report = JsonSerializer.Serialize(new { host = data.GetProperty("host"), resources = data.GetProperty("resources") },
                new JsonSerializerOptions { WriteIndented = true });
        }
        else if (spec.Action == WindowsDeveloperAction.PlanInstall)
        {
            if (Text(data, "status") == "AVAILABLE") _approval = Text(data, "approvalSha256");
            Status = "Installation plan · " + Text(data, "status");
            PlanDetails = Format(data);
        }
        else
        {
            var previous = Selected?.Id;
            Builds = data.GetProperty("builds").EnumerateArray().Select(item => new DeveloperBuild(Text(item, "id"),
                Text(item, "state"), Text(item, "version"), Text(item, "profile"), Text(item, "smokeState"), item.Clone())).ToArray();
            CompareWith = null;
            Selected = Builds.FirstOrDefault(item => item.Id == previous) ?? Builds.FirstOrDefault();
            RefreshSelection();
            Status = $"{Builds.Count} recorded builds.";
        }
    }

    private static string Format(JsonElement data) => JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var item) ? item.ToString() : "";

    private void AppendLog(string line)
    {
        if (line.StartsWith("[phase] installing", StringComparison.Ordinal))
            _dispatcher.Post(() => { if (_active is not null) { _installCommitted = true; CancelCommand.Invalidate(); } });
        lock (_pendingLog)
        {
            bool schedule = _pendingLog.Length == 0;
            if (_pendingLog.Length < 64_000) _pendingLog.AppendLine(line);
            if (schedule) _dispatcher.Post(FlushLog);
        }
    }
    private readonly System.Text.StringBuilder _pendingLog = new();
    private void FlushLog()
    {
        lock (_pendingLog)
        {
            var combined = Log + _pendingLog;
            Log = combined.Length <= 64_000 ? combined : combined[^64_000..];
            _pendingLog.Clear();
        }
    }
    private void RefreshSelection()
    {
        ApproveRuntime = false;
        Report = Selected is null ? "" : CompareWith is null ? Format(Selected.Details)
            : Format(Selected.Details) + Environment.NewLine + Format(CompareWith.Details);
        SmokeCommand.Invalidate();
        ActivateCommand.Invalidate();
    }

    private Task RollbackAsync()
    {
        try
        {
            WindowsRuntimeSelection.ForCurrentUser().Rollback(_bundledRuntime!);
            Status = "Previous runtime restored. Restart Tor Manager to select it.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { Status = "Rollback BLOCKED · " + error.Message; }
        return Task.CompletedTask;
    }
    private void InputsChanged()
    {
        _ready = false;
        _approval = null;
        Confirmed = false;
        PlanDetails = "";
        Selected = CompareWith = null;
        Builds = [];
        Dependencies = [];
        Report = "";
        Status = "Inputs changed. Run preflight again.";
        Invalidate();
    }
    private void Invalidate()
    {
        Changed(nameof(Idle));
        foreach (var command in _commands) command.Invalidate();
        CancelCommand.Invalidate();
    }

    public async Task CloseAsync()
    {
        _closed = true;
        if (!_installCommitted) _cancellation?.Cancel();
        if (_active is not null)
        {
            try { await _active; }
            catch (OperationCanceledException) { }
        }
    }
}
