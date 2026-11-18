using System.Collections.ObjectModel;
using System.Text.Json;
using Tor.Application;
using TorManager.Desktop.Services;

namespace TorManager.Desktop.Presentation;

internal sealed record NoticeCard(string Title, string Detail);
internal sealed record PageViewModel(string Title, string Description, bool ShowTasks = false,
    bool ShowDependencies = false, bool ShowSettings = false, IReadOnlyList<NoticeCard>? Notices = null,
    bool ShowOffline = false, bool ShowDeveloper = false, bool ShowDeveloperDependencies = false,
    bool ShowDeveloperBuild = false, bool ShowDeveloperArtifacts = false, bool ShowPeers = false);
internal sealed record LogCard(string Sequence, string TaskId, string Message, string Detail);
internal enum ConnectionMode { Demo, RealOffline, RealOnline }
internal enum WorkspaceMode { RunOnly, Developer, Embedded }

internal sealed class ShellViewModel : ObservableModel, IDisposable, IAsyncDisposable
{
    private readonly ITaskFeed _feed;
    private readonly IUiDispatcher _dispatcher;
    private readonly SettingsStore _settings;
    private readonly Func<bool>? _hasVerifiedCustomRuntime;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IReadOnlyList<PageViewModel> _demoPages;
    private readonly IReadOnlyList<PageViewModel> _offlinePages;
    private readonly IReadOnlyList<PageViewModel> _developerPages =
    [
        new("Dependencies", "Scan the Windows host and review a pinned installation plan.", ShowDeveloper: true, ShowDeveloperDependencies: true),
        new("Build Manager", "Verify local inputs and invoke the native MSYS2 UCRT64 recipe.", ShowDeveloper: true, ShowDeveloperBuild: true),
        new("Builds", "Inspect build records and run offline acceptance checks.", ShowDeveloper: true, ShowDeveloperArtifacts: true)
    ];
    private PageViewModel _currentPage;
    private string _selectedMode, _theme, _settingsNotice;
    private bool _animateSimulation;
    private WorkspaceMode _workspaceMode;
    private ConnectionMode _connectionMode;
    private RuntimeSource? _preferredRuntime;
    private readonly RuntimeSource? _activeRuntime;
    private bool _closing;
    private Task? _close;
    private string? _closeNotice;
    public ObservableCollection<PageViewModel> Pages { get; }
    public ObservableCollection<TaskCard> Tasks { get; } = new();
    public ObservableCollection<DependencyCard> Dependencies { get; }
    public ObservableCollection<LogCard> Logs { get; } = new();
    public IReadOnlyList<string> Modes { get; } = new[] { "Run Only", "Developer", "Embedded" };
    public IReadOnlyList<string> Themes { get; } = new[] { "System", "Dark", "Light" };
    public AsyncCommand ReplayCommand { get; }
    public ActionCommand SaveSettingsCommand { get; }
    public ActionCommand ShowDemoCommand { get; }
    public ActionCommand ShowRealOfflineCommand { get; }
    public ActionCommand ShowRealOnlineCommand { get; }
    public ActionCommand ShowDeveloperCommand { get; }
    public ActionCommand ShowRunOnlyCommand { get; }
    public ActionCommand SelectBundledRuntimeCommand { get; }
    public ActionCommand SelectCustomRuntimeCommand { get; }
    public DeveloperViewModel? Developer { get; }
    public bool HasDeveloper => Developer is not null;
    public OfflineTorViewModel? Offline { get; }
    public string? OfflineUnavailableReason { get; }
    public bool HasOfflineUnavailableReason => OfflineUnavailableReason is not null;
    public bool HasOffline => Offline is not null;
    public bool IsDemo => _connectionMode == ConnectionMode.Demo;
    public bool IsRealOffline => _connectionMode == ConnectionMode.RealOffline;
    public bool IsOnline => _connectionMode == ConnectionMode.RealOnline;
    public bool IsDeveloper => _workspaceMode == WorkspaceMode.Developer;
    public bool HasOnline => Offline?.SupportsOnline == true;
    public string ModeHeading => _connectionMode switch
    {
        ConnectionMode.Demo => "Demo · simulated data",
        ConnectionMode.RealOnline => "Real Online",
        _ => "Real Offline"
    };
    public string WorkspaceHeading => IsDeveloper ? "Developer workspace" : "Run Only workspace";
    public string ActiveRuntime => _activeRuntime switch
    {
        RuntimeSource.Bundled => "Bundled Tor",
        RuntimeSource.CustomVerified => "Custom verified Tor",
        _ => "Tor runtime unavailable"
    };
    public string RuntimePreference => _preferredRuntime switch
    {
        RuntimeSource.Bundled => "Next launch: bundled Tor",
        RuntimeSource.CustomVerified => "Next launch: approved custom Tor if available",
        _ => "Next launch: use an approved custom runtime when available"
    };
    public bool CanChooseRuntime => _hasVerifiedCustomRuntime is not null;
    public string TaskHeading => _connectionMode switch
    {
        ConnectionMode.Demo => "Simulated tasks",
        ConnectionMode.RealOnline => "Tor session tasks",
        _ => "Offline Tor tasks"
    };
    public string SidebarStatus => IsDemo ? "Example data" : Offline?.SessionText ?? "Runtime unavailable";
    public IEnumerable<TaskCard> VisibleTasks => IsDemo ? Tasks : Offline?.Tasks ?? [];
    public IEnumerable<LogCard> VisibleLogs => IsDemo ? Logs : Offline?.Logs ?? [];
    public PageViewModel CurrentPage { get => _currentPage; set => Set(ref _currentPage, value); }
    public string SettingsNotice { get => _settingsNotice; private set => Set(ref _settingsNotice, value); }
    public string SelectedMode
    {
        get => _selectedMode;
        set { if (Modes.Contains(value) && Set(ref _selectedMode, value)) Changed(nameof(ModeDescription)); }
    }
    public string Theme { get => _theme; set { if (Themes.Contains(value)) Set(ref _theme, value); } }
    public bool AnimateSimulation { get => _animateSimulation; set => Set(ref _animateSimulation, value); }
    public string ModeDescription => _closeNotice ?? (IsDeveloper
        ? "Developer actions use verified local inputs and require explicit requests."
        : IsDemo && !HasOffline && HasOfflineUnavailableReason
        ? OfflineUnavailableReason! : IsDemo ? SelectedMode switch
    {
        "Developer" => "Developer · Tools and builds require separate consent; this shell runs simulations only.",
        "Embedded" => "Embedded · PICO device pairing and deployment BLOCKED; no device connection is attempted.",
        _ => "Run Only · No compiler, package installation or build operation is started by this application."
    } : IsOnline ? "Start Tor explicitly to connect. Use SOCKS5 on loopback with remote DNS in each application."
        : "Tor runs with networking and SOCKS disabled. Control uses authenticated loopback.");
    public string TaskSummary => IsDemo
        ? $"{Tasks.Count} simulated tasks · {Tasks.Count(x => x.State == "running")} running · {Tasks.Count(x => x.State == "blocked")} blocked"
        : Offline?.TaskText ?? "Runtime unavailable";

    public ShellViewModel(ITaskFeed feed, IUiDispatcher dispatcher, SettingsStore settings,
        IOfflineTorService? offline = null, string? offlineUnavailableReason = null, bool showRealWhenUnavailable = false,
        string? bundledRuntime = null, PeerProfileStore? peerProfile = null, HostedOnionStore? hostingIdentity = null,
        RuntimeSource? activeRuntime = null, Func<bool>? hasVerifiedCustomRuntime = null)
    {
        _feed = feed; _dispatcher = dispatcher; _settings = settings;
        var stored = settings.Load(out _settingsNotice);
        _preferredRuntime = stored.PreferredRuntime;
        _activeRuntime = activeRuntime;
        _hasVerifiedCustomRuntime = hasVerifiedCustomRuntime;
        _selectedMode = stored.Mode; _theme = stored.Theme; _animateSimulation = stored.AnimateSimulation;
        _demoPages = new[]
        {
            new PageViewModel("Dashboard", "Local workspace overview. Every observation below is SIMULATED; actual Tor monitoring is not connected.", ShowTasks: true,
                Notices: new[] { new NoticeCard("Tor integration · NOT CONNECTED", "Authenticated Tor monitoring is not connected. Bootstrap, health and traffic are unavailable."),
                    new NoticeCard("Runtime qualification · NOT RUN", "Native Linux builds and physical PICO qualification require separate evidence.") }),
            new PageViewModel("Build Manager", "Build plan placeholder. No build, download or installer can be started here.",
                Notices: new[] { FixtureNotice("BuildRequest", "Build request"), FixtureNotice("BuildArtifact", "Artifact inventory"),
                    FixtureNotice("TargetDescriptor", "Selected target"), new NoticeCard("Executor integration · NOT CONNECTED", "Source preflight and build execution are not connected to this screen. No build can be started.") }),
            new PageViewModel("Dependencies", "All five dependency states shown below are simulated. Host diagnostics and repair are not connected.", ShowDependencies: true),
            new PageViewModel("Devices", "No paired or physical endpoint is connected.", Notices: new[] { FixtureNotice("DeviceInfo", "PICO device · BLOCKED") }),
            new PageViewModel("Tests", "Synthetic test records never imply executed runtime acceptance.", Notices: new[] { FixtureNotice("TestResult", "Test record · NOT RUN") }),
            new PageViewModel("Reports", "Read-only synthetic evidence summary. Historical reports are not rewritten or imported as current success.", Notices: new[] { FixtureNotice("EvidenceManifest", "Evidence · NOT RUN") }),
            new PageViewModel("Logs and Progress", "Bounded simulation log and task details, including raw exits and interrupted-run reasons.", ShowTasks: true),
            new PageViewModel("Settings", "UI preferences only. No Tor identity, cookie, credential or device configuration is stored.", ShowSettings: true)
        };
        _offlinePages = new[]
        {
            new PageViewModel("Dashboard", "Start and monitor the verified Windows Tor runtime.", ShowOffline: true),
            new PageViewModel("Peers", "Pair contacts and exchange messages over Tor.", ShowPeers: true),
            new PageViewModel("Logs and Progress", "Task history and sanitized lifecycle events from this workspace.", ShowTasks: true),
            new PageViewModel("Settings", "Save your appearance and Demo preferences.", ShowSettings: true)
        }.Where(page => !page.ShowPeers || offline is IPeerTorService).ToArray();
        Offline = offline is null ? null : new(offline, dispatcher, peerProfile, hostingIdentity);
        if (OperatingSystem.IsWindows()) Developer = new(dispatcher, bundledRuntime);
        OfflineUnavailableReason = offlineUnavailableReason;
        _connectionMode = offline is null && !showRealWhenUnavailable ? ConnectionMode.Demo : ConnectionMode.RealOffline;
        Pages = new(IsDemo ? _demoPages : _offlinePages);
        _currentPage = Pages[0];
        Dependencies = new(feed.Dependencies);
        foreach (var task in feed.InitialTasks) Apply(task);
        ReplayCommand = new(ReplayAsync, () => SettingsNotice = "The Demo replay could not finish.",
            () => IsDemo && !_closing);
        ShowDemoCommand = new(ShowDemo, () => !_closing && !IsDemo && (Offline?.CanSwitchToDemo ?? true));
        ShowRealOfflineCommand = new(ShowRealOffline, () => !_closing && _connectionMode != ConnectionMode.RealOffline &&
            (HasOffline || showRealWhenUnavailable) && (Offline?.CanSwitchToDemo ?? true));
        ShowRealOnlineCommand = new(ShowRealOnline, () => !_closing && !IsOnline && HasOnline && Offline!.CanSwitchToDemo);
        ShowDeveloperCommand = new(() => SelectWorkspace(WorkspaceMode.Developer),
            () => !_closing && HasDeveloper && !IsDeveloper);
        ShowRunOnlyCommand = new(() => SelectWorkspace(WorkspaceMode.RunOnly), () => !_closing && IsDeveloper);
        SelectBundledRuntimeCommand = new(() => SetRuntimeSource(RuntimeSource.Bundled), () => !_closing && CanChooseRuntime);
        SelectCustomRuntimeCommand = new(() => SetRuntimeSource(RuntimeSource.CustomVerified), () => !_closing && CanChooseRuntime);
        if (Offline is not null) Offline.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(Offline.CanSwitchToDemo))
            {
                ShowDemoCommand.Invalidate();
                ShowRealOfflineCommand.Invalidate();
                ShowRealOnlineCommand.Invalidate();
                ShowDeveloperCommand.Invalidate();
            }
            if (IsDemo) return;
            if (change.PropertyName == nameof(Offline.Tasks)) Changed(nameof(VisibleTasks));
            if (change.PropertyName == nameof(Offline.Logs)) Changed(nameof(VisibleLogs));
            if (change.PropertyName == nameof(Offline.SessionText)) Changed(nameof(SidebarStatus));
            if (change.PropertyName == nameof(Offline.TaskText)) Changed(nameof(TaskSummary));
        };
        SaveSettingsCommand = new(() =>
        {
            try
            {
                _settings.Save(new(Mode: SelectedMode, Theme: Theme, AnimateSimulation: AnimateSimulation,
                    PreferredRuntime: _preferredRuntime));
                SettingsNotice = "UI preferences saved in your local application-data directory.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            { SettingsNotice = "Settings could not be saved; existing file was preserved."; }
        }, () => !_closing);
    }

    public async Task OpenAsync()
    {
        if (IsDemo) ReplayCommand.Execute(null);
        if (Offline is not null) await Offline.OpenAsync();
    }

    public void ShowDemo()
    {
        if (_closing || IsDemo || Offline?.CanSwitchToDemo == false) return;
        SelectConnection(ConnectionMode.Demo);
    }

    public void ShowRealOffline()
    {
        if (!ShowRealOfflineCommand.CanExecute(null)) return;
        SelectConnection(ConnectionMode.RealOffline);
    }

    public void ShowRealOnline()
    {
        if (!ShowRealOnlineCommand.CanExecute(null)) return;
        SelectConnection(ConnectionMode.RealOnline);
    }

    private void SelectWorkspace(WorkspaceMode mode)
    {
        _workspaceMode = mode;
        UpdateNavigation(IsDeveloper ? _developerPages[0] : null);
    }

    private void SelectConnection(ConnectionMode mode)
    {
        if (Offline?.SelectOnline(mode == ConnectionMode.RealOnline) == false) return;
        _connectionMode = mode;
        UpdateNavigation(IsDeveloper ? CurrentPage : null);
    }

    private void UpdateNavigation(PageViewModel? preferred)
    {
        var connectionPages = IsDemo ? _demoPages : _offlinePages;
        var pages = IsDeveloper
            ? connectionPages.Where(page => !_developerPages.Any(developer => developer.Title == page.Title)).Concat(_developerPages)
            : connectionPages;
        Pages.Clear();
        foreach (var page in pages) Pages.Add(page);
        CurrentPage = preferred is not null && Pages.Contains(preferred) ? preferred : Pages[0];
        Changed(nameof(IsDemo));
        Changed(nameof(IsRealOffline));
        Changed(nameof(IsOnline));
        Changed(nameof(IsDeveloper));
        Changed(nameof(ModeHeading));
        Changed(nameof(WorkspaceHeading));
        Changed(nameof(TaskHeading));
        Changed(nameof(SidebarStatus));
        Changed(nameof(VisibleTasks));
        Changed(nameof(VisibleLogs));
        Changed(nameof(TaskSummary));
        Changed(nameof(ModeDescription));
        ReplayCommand.Invalidate();
        ShowDemoCommand.Invalidate();
        ShowRealOfflineCommand.Invalidate();
        ShowRealOnlineCommand.Invalidate();
        ShowDeveloperCommand.Invalidate();
        ShowRunOnlyCommand.Invalidate();
    }

    private void SetRuntimeSource(RuntimeSource selected)
    {
        try
        {
            if (selected == RuntimeSource.CustomVerified && _hasVerifiedCustomRuntime?.Invoke() != true)
            {
                SettingsNotice = "Approve a build after its offline smoke test passes.";
                return;
            }
            var saved = _settings.Load(out _);
            _settings.Save(saved with { PreferredRuntime = selected });
            _preferredRuntime = selected;
            Changed(nameof(RuntimePreference));
            SettingsNotice = "Runtime preference saved. Restart Tor Manager to apply it.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            JsonException or InvalidOperationException or System.Security.SecurityException)
        {
            SettingsNotice = "The runtime selection could not be verified or saved.";
        }
    }

    public async Task ReplayAsync()
    {
        if (!IsDemo || _closing || !AnimateSimulation) return;
        var token = _lifetime.Token;
        Tasks.Clear();
        Logs.Clear();
        foreach (var task in _feed.InitialTasks) Apply(task);
        await foreach (var task in _feed.WatchAsync(token).ConfigureAwait(false))
            await _dispatcher.ApplyAsync(() => Apply(task), token).ConfigureAwait(false);
    }

    private void Apply(TaskCard task)
    {
        var previous = Tasks.FirstOrDefault(item => item.TaskId == task.TaskId);
        if (previous is not null)
        {
            if (task.Sequence <= previous.Sequence) return;
            Tasks[Tasks.IndexOf(previous)] = task;
        }
        else Tasks.Add(task);
        Logs.Add(new(task.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), task.TaskId, task.Message, task.Detail));
        while (Logs.Count > 100) Logs.RemoveAt(0);
        Changed(nameof(TaskSummary));
    }

    private static NoticeCard FixtureNotice(string name, string title)
    {
        using var stream = typeof(ShellViewModel).Assembly.GetManifestResourceStream("TorManager.Desktop.SampleData." + name + ".json")
            ?? throw new InvalidOperationException("Synthetic resource absent");
        using var json = JsonDocument.Parse(stream);
        JsonFields.Version(json.RootElement);
        var data = json.RootElement;
        var detail = name switch
        {
            "DeviceInfo" => $"{data.GetProperty("deviceId").GetString()} · pairing: {data.GetProperty("pairing").GetString()} · certainty: {data.GetProperty("certainty").GetString()} · board/carrier/DTB unknown",
            "TestResult" => data.GetProperty("status").GetString() + " · " + data.GetProperty("reason").GetString(),
            "EvidenceManifest" => data.GetProperty("comparison").GetProperty("status").GetString() + " · Synthetic evidence only; no physical or runtime qualification",
            "BuildRequest" => data.GetProperty("adapterId").GetString() + " · mode: " + data.GetProperty("mode").GetString(),
            "BuildArtifact" => data.GetProperty("kind").GetString() + " · Synthetic metadata only; no artifact is shipped",
            _ => data.GetProperty("os").GetString() + " / " + data.GetProperty("architecture").GetString() + " · " + data.GetProperty("certainty").GetString()
        };
        return new(title, "SIMULATION · " + detail);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync() => new(_close ??= CloseAsync());

    internal void ReportCloseFailure()
    {
        _closeNotice = "Shutdown could not confirm cleanup. Review the task status. Close again to exit.";
        Changed(nameof(ModeDescription));
    }

    private async Task CloseAsync()
    {
        _closing = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _closeNotice = "Closing the Tor workspace…";
        Changed(nameof(ModeDescription));
        ReplayCommand.Invalidate();
        ShowDemoCommand.Invalidate();
        ShowRealOfflineCommand.Invalidate();
        ShowRealOnlineCommand.Invalidate();
        ShowDeveloperCommand.Invalidate();
        ShowRunOnlyCommand.Invalidate();
        SelectBundledRuntimeCommand.Invalidate();
        SelectCustomRuntimeCommand.Invalidate();
        SaveSettingsCommand.Invalidate();
        try
        {
            if (OperatingSystem.IsWindows() && Developer is not null) await Developer.CloseAsync();
        }
        finally
        {
            if (Offline is not null) await Offline.DisposeAsync().ConfigureAwait(false);
        }
    }
}
