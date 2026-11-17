using System.Text.Json;
using System.Text.Json.Nodes;
using TorManager.Desktop.Presentation;
using TorManager.Desktop.Services;

// Package-free behavioral harness. Tests actual UI presentation/service code,
// not Avalonia rendering. Run from desktop with the pinned SDK already present.
var passed = 0;
void Check(bool condition, string test)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + test);
    Console.WriteLine("PASS: " + test);
    passed++;
}
void Reject(Action action, string test)
{
    try { action(); }
    catch (JsonException) { Check(true, test); return; }
    throw new InvalidOperationException("FAIL: accepted " + test);
}
JsonElement Element(JsonNode value) => JsonSerializer.SerializeToElement(value);
var feed = new SimulationTaskFeed();
Check(feed.InitialTasks.Select(x => x.State).ToHashSet().SetEquals(new[] { "queued", "running", "cancelling", "cancelled", "succeeded", "failed", "blocked", "interrupted" }), "all eight task states displayed");
Check(feed.Dependencies.Select(x => x.Status).ToHashSet().SetEquals(new[] { "AVAILABLE", "MISSING", "INCOMPATIBLE", "UNKNOWN", "BLOCKED" }), "all five dependency classifications preserved");
var failed = feed.InitialTasks.Single(x => x.State == "failed");
Check(failed.RawExitCode == 17 && failed.Detail.Contains("17"), "failed raw exit retained");
var interrupted = feed.InitialTasks.Single(x => x.State == "interrupted");
Check(interrupted.ReasonCode == "runner-crash" && interrupted.StateLabel == "Interrupted", "crashed runner is interrupted, not completed");
Check(feed.InitialTasks.Single(x => x.State == "cancelling").IsActiveIndeterminate && !interrupted.IsActiveIndeterminate, "indeterminate active progress distinct from unavailable terminal progress");
var progress = feed.InitialTasks.Single(x => x.State == "running") with { Completed = 3, Total = 10 };
Check(progress.ProgressValue == 30 && !progress.IsIndeterminate, "determinate progress ratio");
Check((progress with { Completed = 1e308, Total = 1e308 }).ProgressValue == 100, "large finite progress does not overflow the percentage");
using var sample = typeof(SimulationTaskFeed).Assembly.GetManifestResourceStream("TorManager.Desktop.SampleData.TaskProgress.json")!;
var task = JsonNode.Parse(sample)!.AsObject();
task["sequence"] = 1.0; task["rawExitCode"] = 17.0;
Check(TaskCard.FromJson(Element(task)).RawExitCode == 17, "W1 integral decimal numbers accepted");
task["sequence"] = 9007199254740993L;
Check(TaskCard.FromJson(Element(task)).Sequence == 9007199254740993L, "sequence integers above double precision remain exact");
task["sequence"] = 1;
var exponential = task.ToJsonString().Replace("\"sequence\":1", "\"sequence\":2e0");
using (var exponentDocument = JsonDocument.Parse(exponential))
    Check(TaskCard.FromJson(exponentDocument.RootElement).Sequence == 2, "W1 integral exponent accepted");
foreach (var invalid in new JsonNode?[] { JsonValue.Create(true), JsonValue.Create(1.5) })
{
    task["sequence"] = invalid?.DeepClone();
    Reject(() => TaskCard.FromJson(Element(task)), "boolean/fractional sequence rejected");
}
task["sequence"] = 1;
task["rawExitCode"] = 4294967295.0;
Check(TaskCard.FromJson(Element(task)).RawExitCode == uint.MaxValue, "maximum raw Windows exit code retained");
task["rawExitCode"] = 4294967296.0;
Reject(() => TaskCard.FromJson(Element(task)), "out-of-range raw exit rejected");
task["rawExitCode"] = null;
using (var overflowDocument = JsonDocument.Parse(task.ToJsonString().Replace("\"sequence\":1", "\"sequence\":1e999")))
    Reject(() => TaskCard.FromJson(overflowDocument.RootElement), "nonfinite numeric overflow rejected");
using (var tinyFraction = JsonDocument.Parse(task.ToJsonString().Replace("\"sequence\":1", "\"sequence\":1e-100")))
    Reject(() => TaskCard.FromJson(tinyFraction.RootElement), "tiny fractional sequence does not round to integer zero");
task["schemaVersion"] = 2;
Reject(() => TaskCard.FromJson(Element(task)), "unsupported schema version rejected");
task["schemaVersion"] = 1; task["completed"] = 10; task["total"] = 9; task["unit"] = "steps";
Reject(() => TaskCard.FromJson(Element(task)), "over-complete progress rejected");
task["completed"] = null; task["total"] = null; task["unit"] = null; task["reasonCode"] = null;
Reject(() => TaskCard.FromJson(Element(task)), "missing interruption reason rejected");
task["reasonCode"] = "runner-crash"; task["state"] = "successful-ish";
Reject(() => TaskCard.FromJson(Element(task)), "unknown state rejected");

var folder = Path.Combine(Path.GetTempPath(), "tor-desktop-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var path = Path.Combine(folder, "settings.json");
    var store = new SettingsStore(path);
    using var shell = new ShellViewModel(feed, new ImmediateDispatcher(), store);
    Check(!File.Exists(path), "startup does not write settings");
    Check(shell.Pages.Select(x => x.Title).SequenceEqual(new[] { "Dashboard", "Build Manager", "Dependencies", "Devices", "Tests", "Reports", "Logs and Progress", "Settings" }), "eight functional navigation destinations");
    foreach (var page in shell.Pages) { shell.CurrentPage = page; Check(shell.CurrentPage == page, "navigate " + page.Title); }
    foreach (var mode in shell.Modes) { shell.SelectedMode = mode; Check(shell.ModeDescription.StartsWith(mode), "switch mode " + mode); }
    shell.Theme = "Light"; shell.AnimateSimulation = false;
    shell.SaveSettingsCommand.Execute(null);
    Check(store.Load(out _).Mode == "Embedded" && store.Load(out _).Theme == "Light" && !store.Load(out _).AnimateSimulation, "UI settings persisted and reloaded");
    File.WriteAllText(path, "{ invalid JSON");
    Check(store.Load(out var invalidNotice) == new UiSettings() && invalidNotice.Contains("preserved") && File.ReadAllText(path) == "{ invalid JSON", "invalid settings preserve file and use defaults");
    shell.AnimateSimulation = true;
    await shell.ReplayAsync();
    Check(shell.Tasks.Single(x => x.TaskId == "demo-running").State == "succeeded" && shell.Tasks.Single(x => x.TaskId == "demo-failed").State == "failed", "finite simulated stream updates one task without rewriting failures");
    await shell.ReplayAsync();
    Check(shell.Tasks.Single(x => x.TaskId == "demo-running").Sequence == 12 && shell.Logs.Count <= 100, "replay resets stream and remains bounded");
    using (var burst = new ShellViewModel(new BurstFeed(feed), new ImmediateDispatcher(), new SettingsStore(Path.Combine(folder, "burst.json"))))
    {
        await burst.ReplayAsync();
        Check(burst.Logs.Count == 100 && burst.Logs[0].Sequence == "202" && burst.Logs[^1].Sequence == "301",
            "high-volume stream drops old log entries at the exact bound");
        Check(burst.Tasks.Single(x => x.TaskId == "demo-running").Sequence == 301 && !burst.Logs.Any(x => x.Message == "stale event"),
            "stale sequence cannot roll back task progress or append a log");
    }
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var cancelled = false;
    try { await foreach (var _ in feed.WatchAsync(cancellation.Token)) { } }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "feed cancellation observed");
}
finally
{
    // Only this harness-created file and empty unique directory; no recursive cleanup.
    File.Delete(Path.Combine(folder, "settings.json"));
    Directory.Delete(folder);
}
Console.WriteLine($"{passed} behavioral assertions PASS; Avalonia render/launch not exercised by this harness.");

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action update) => update();
}

internal sealed class BurstFeed(SimulationTaskFeed source) : ITaskFeed
{
    public IReadOnlyList<TaskCard> InitialTasks => source.InitialTasks;
    public IReadOnlyList<DependencyCard> Dependencies => source.Dependencies;
    public async IAsyncEnumerable<TaskCard> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        var task = InitialTasks.Single(row => row.TaskId == "demo-running");
        for (var sequence = 2; sequence <= 301; sequence++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return task with { Sequence = sequence, Completed = sequence, Total = 301 };
        }
        yield return task with { Sequence = 300, Message = "stale event" };
    }
}
