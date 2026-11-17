using System.Collections.ObjectModel;
using System.Text.Json;
using TorManager.Desktop.Services;

namespace TorManager.Desktop.Presentation;

internal sealed record NoticeCard(string Title, string Detail);
internal sealed record PageViewModel(string Title, string Description, bool ShowTasks = false,
    bool ShowDependencies = false, bool ShowSettings = false, IReadOnlyList<NoticeCard>? Notices = null);
internal sealed record LogCard(string Sequence, string TaskId, string Message, string Detail);

internal sealed class ShellViewModel : ObservableModel, IDisposable
{
    private readonly ITaskFeed _feed;
    private readonly IUiDispatcher _dispatcher;
    private readonly SettingsStore _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private PageViewModel _currentPage;
    private string _selectedMode, _theme, _settingsNotice;
    private bool _animateSimulation;
    public ObservableCollection<PageViewModel> Pages { get; }
    public ObservableCollection<TaskCard> Tasks { get; } = new();
    public ObservableCollection<DependencyCard> Dependencies { get; }
    public ObservableCollection<LogCard> Logs { get; } = new();
    public IReadOnlyList<string> Modes { get; } = new[] { "Run Only", "Developer", "Embedded" };
    public IReadOnlyList<string> Themes { get; } = new[] { "System", "Dark", "Light" };
    public AsyncCommand ReplayCommand { get; }
    public ActionCommand SaveSettingsCommand { get; }
    public PageViewModel CurrentPage { get => _currentPage; set => Set(ref _currentPage, value); }
    public string SettingsNotice { get => _settingsNotice; private set => Set(ref _settingsNotice, value); }
    public string SelectedMode
    {
        get => _selectedMode;
        set { if (Modes.Contains(value) && Set(ref _selectedMode, value)) Changed(nameof(ModeDescription)); }
    }
    public string Theme { get => _theme; set { if (Themes.Contains(value)) Set(ref _theme, value); } }
    public bool AnimateSimulation { get => _animateSimulation; set => Set(ref _animateSimulation, value); }
    public string ModeDescription => SelectedMode switch
    {
        "Developer" => "Developer · Tools and builds require separate consent; this shell runs simulations only.",
        "Embedded" => "Embedded · PICO device pairing and deployment BLOCKED; no device connection is attempted.",
        _ => "Run Only · No compiler, package installation or build operation is started by this application."
    };
    public string TaskSummary => $"{Tasks.Count} simulated tasks · {Tasks.Count(x => x.State == "running")} running · {Tasks.Count(x => x.State == "blocked")} blocked";

    public ShellViewModel(ITaskFeed feed, IUiDispatcher dispatcher, SettingsStore settings)
    {
        _feed = feed; _dispatcher = dispatcher; _settings = settings;
        var stored = settings.Load(out _settingsNotice);
        _selectedMode = stored.Mode; _theme = stored.Theme; _animateSimulation = stored.AnimateSimulation;
        Pages = new(new[]
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
        });
        _currentPage = Pages[0];
        Dependencies = new(feed.Dependencies);
        foreach (var task in feed.InitialTasks) Apply(task);
        ReplayCommand = new(ReplayAsync, () => _dispatcher.Post(() => SettingsNotice = "Simulation feed failed; no real task or process was started."));
        SaveSettingsCommand = new(() =>
        {
            try
            {
                _settings.Save(new(Mode: SelectedMode, Theme: Theme, AnimateSimulation: AnimateSimulation));
                SettingsNotice = "UI preferences saved in your local application-data directory.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            { SettingsNotice = "Settings could not be saved; existing file was preserved."; }
        });
    }

    public async Task ReplayAsync()
    {
        if (!AnimateSimulation) return;
        var token = _lifetime.Token;
        Tasks.Clear();
        Logs.Clear();
        foreach (var task in _feed.InitialTasks) Apply(task);
        await foreach (var task in _feed.WatchAsync(token).ConfigureAwait(false))
            await ApplyOnUiAsync(task, token).ConfigureAwait(false);
    }

    private Task ApplyOnUiAsync(TaskCard task, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Backpressure preserves every accepted terminal update and limits queued work to one callback.
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(() =>
        {
            if (token.IsCancellationRequested)
            {
                applied.TrySetCanceled(token);
                return;
            }
            try
            {
                Apply(task);
                applied.SetResult();
            }
            catch (Exception error) { applied.SetException(error); }
        });
        return applied.Task.WaitAsync(token);
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

    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
