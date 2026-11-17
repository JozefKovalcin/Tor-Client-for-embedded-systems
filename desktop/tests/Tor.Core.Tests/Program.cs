using System.Security.Cryptography;
using System.Text.Json;
using Tor.Contracts;
using Tor.Core;

static class Check
{
    public static void That(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    public static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}

sealed class Handler(CommandKind kind, Func<TaskCommand, TaskExecutionContext, CancellationToken, Task<TaskExecutionResult>> run) : ITaskHandler
{
    public CommandKind Kind => kind;
    public Task<TaskExecutionResult> ExecuteAsync(TaskCommand command, TaskExecutionContext context, CancellationToken token) => run(command, context, token);
}

sealed class Workspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "tor-core-tests-" + Guid.NewGuid().ToString("N"));
    public VerifiedFile Input { get; }
    public VerifiedFileStore Files { get; }
    public Workspace()
    {
        PrivateStorage.CreateNewDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "input.bin"), "synthetic input");
        Input = new VerifiedFile("input.bin", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "input.bin")))));
        Files = new VerifiedFileStore(Root);
    }
    public TaskJournal Journal() => new(Root);
    public TaskSubmission Probe(string id, string[]? dependencies = null, ResourceRequest? resources = null) =>
        new(id, new DependencyProbeCommand([Input]), dependencies ?? [], resources ?? new(1, 1, 1, false));
    public void Dispose() => Directory.Delete(Root, true); // Only this harness's fresh, random temporary directory.
}

static class Program
{
    static readonly ResourceRequest Capacity = new(4, 100, 100, true);
    static readonly List<TaskProgress> Export = [];
    static TaskExecutionResult Success() => TaskExecutionResult.Success;
    static Handler Successful() => new(CommandKind.DependencyProbe, (_, context, _) =>
    { context.Report(TaskPhase.Executing, 1, 2, "steps"); return Task.FromResult(Success()); });

    static async Task Main(string[] args)
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("dag-state-and-progress", Dag), ("raw-exit-and-blocked-dependent", RawExit),
            ("cancellation-awaits-adapter", Cancellation), ("retry-revalidates-input", Retry),
            ("heavy-memory-and-output-reservations", Reservations), ("cycle-and-capacity-rejection", Admission),
            ("interrupted-recovery-no-reexecution", Recovery), ("bounded-stream-and-logs", Streams),
            ("reference-security-and-output-verification", References), ("contract-binding-invalid-values", Binding),
            ("bounded-task-deadline", Deadline), ("unexpected-zero-exit-is-not-success", UnexpectedExit)
        };
        foreach (var (name, test) in tests) { await test(); Console.WriteLine("PASS " + name); }
        if (args.Length == 1) File.WriteAllText(args[0], JsonSerializer.Serialize(Export, ContractJson.Options));
        Console.WriteLine($"{tests.Length} backend behavior groups PASS");
    }

    static async Task Dag()
    {
        using var w = new Workspace(); var called = new List<string>();
        var handler = new Handler(CommandKind.DependencyProbe, (_, c, _) => { called.Add("run"); c.Report(TaskPhase.Executing, 1, 2, "steps"); return Task.FromResult(Success()); });
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        core.Submit([w.Probe("child", ["parent"]), w.Probe("parent")]);
        Export.AddRange(core.Snapshot());
        await core.RunUntilIdleAsync();
        Check.That(called.Count == 2 && core.Snapshot().All(x => x.State == "succeeded"), "DAG did not finish");
        Check.That(core.Logs().Where(x => x.TaskId == "parent").Select(x => x.Sequence).SequenceEqual(new long[] { 0, 1, 2, 3 }), "Nonmonotonic sequence");
        Check.That(core.Logs().First(x => x.TaskId == "child" && x.Event == TaskMessage.Started).TimestampUtc >=
            core.Logs().First(x => x.TaskId == "parent" && x.Event == TaskMessage.Succeeded).TimestampUtc, "Dependency ran early");
        Export.AddRange(core.Snapshot());
    }

    static async Task RawExit()
    {
        using var w = new Workspace();
        var handler = new Handler(CommandKind.DependencyProbe, (_, _, _) => Task.FromResult(new TaskExecutionResult(0xC0000142)));
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        core.Submit([w.Probe("failed"), w.Probe("blocked", ["failed"])]); await core.RunUntilIdleAsync();
        Check.That(core.Snapshot().Single(x => x.TaskId == "failed").RawExitCode == 0xC0000142, "Unsigned Windows exit lost");
        Check.That(core.Snapshot().Single(x => x.TaskId == "blocked").State == "blocked", "Failed dependency became successful");
        Export.AddRange(core.Snapshot());
    }

    static async Task Cancellation()
    {
        using var w = new Workspace(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var cleaned = false;
        var handler = new Handler(CommandKind.DependencyProbe, async (_, _, token) =>
        { started.SetResult(); try { await Task.Delay(Timeout.Infinite, token); } finally { await Task.Delay(10); cleaned = true; } return Success(); });
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        core.Submit([w.Probe("cancel"), w.Probe("child-cancel", ["cancel"])]);
        var running = core.RunUntilIdleAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Export.AddRange(core.Snapshot()); core.Cancel("cancel"); Export.AddRange(core.Snapshot()); await running.WaitAsync(TimeSpan.FromSeconds(5));
        Check.That(cleaned && core.Snapshot().Single(x => x.TaskId == "cancel").State == "cancelled", "Cancelled before adapter cleanup");
        Check.That(core.Snapshot().Single(x => x.TaskId == "child-cancel").State == "blocked", "New DAG child launched during cancellation");
        Export.AddRange(core.Snapshot());
    }

    static async Task Retry()
    {
        using var w = new Workspace(); int calls = 0;
        var handler = new Handler(CommandKind.DependencyProbe, (_, _, _) => { calls++; return Task.FromResult(new TaskExecutionResult(7)); });
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        core.Submit([w.Probe("original")]); await core.RunUntilIdleAsync();
        File.WriteAllText(Path.Combine(w.Root, "input.bin"), "changed synthetic input");
        core.Retry("original", "retry"); await core.RunUntilIdleAsync();
        Check.That(calls == 1 && core.Snapshot().Single(x => x.TaskId == "retry").State == "failed", "Retry failed to reverify input");
        Check.That(core.Snapshot().Single(x => x.TaskId == "retry").RetryOfTaskId == "original", "Retry lineage lost");
    }

    static async Task Reservations()
    {
        foreach (var mode in new[] { "heavy", "memory", "output" })
        {
            using var w = new Workspace(); int count = 0, peak = 0;
            var kind = mode == "output" ? CommandKind.NativeLinuxBuild : CommandKind.DependencyProbe;
            var handler = new Handler(kind, async (_, _, _) =>
            { var active = Interlocked.Increment(ref count); peak = Math.Max(peak, active); await Task.Delay(20); Interlocked.Decrement(ref count); return new TaskExecutionResult(1); });
            await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
            var resource = mode == "heavy" ? new ResourceRequest(1, 1, 1, true) : new ResourceRequest(1, mode == "memory" ? 70 : 1, 1, false);
            TaskSubmission task(string id, string output) => mode == "output"
                ? new(id, new NativeLinuxBuildCommand(w.Input, [], [output]), [], resource) : w.Probe(id, resources: resource);
            core.Submit([task("first", "artifact"), task("second", "ARTIFACT/bin")]); await core.RunUntilIdleAsync();
            Check.That(peak == 1, "Concurrent conflicting resource/output reservation: " + mode);
        }
    }

    static async Task Admission()
    {
        using var w = new Workspace(); await using var core = new TaskOrchestrator([Successful()], w.Files, w.Journal(), Capacity, 2);
        Check.Throws<InvalidDataException>(() => core.Submit([w.Probe("a", ["b"]), w.Probe("b", ["a"])]));
        Check.That(core.Snapshot().Count == 0, "Rejected graph partially admitted");
        core.Submit([w.Probe("one", resources: new(5, 1, 1, false)), w.Probe("two")]);
        Check.Throws<InvalidOperationException>(() => core.Submit([w.Probe("three")])); await core.RunUntilIdleAsync();
        Check.That(core.Snapshot().Single(x => x.TaskId == "one").State == "blocked", "Oversized reservation not blocked");
    }

    static async Task Recovery()
    {
        using var w = new Workspace();
        using (var journal = w.Journal()) journal.Write([
            new(1, "lost", null, 8, DateTimeOffset.UtcNow, "executing", "running", null, null, null, "Synthetic running fixture", true, null, null, null, null, null),
            new(1, "queued", null, 0, DateTimeOffset.UtcNow, "queued", "queued", null, null, null, "Synthetic queued fixture", true, null, null, null, null, null)]);
        int calls = 0;
        var handler = new Handler(CommandKind.DependencyProbe, (_, _, _) => { calls++; return Task.FromResult(Success()); });
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity, interruptionReason: "runner-lost");
        await core.RunUntilIdleAsync();
        Check.That(calls == 0, "Recovery silently executed work");
        var lost = core.Snapshot().Single(x => x.TaskId == "lost");
        Check.That(lost.State == "interrupted" && lost.ReasonCode == "runner-lost" && lost.Sequence == 9 && lost.RawExitCode is null, "Interrupted task mislabeled");
        Check.That(core.Snapshot().Single(x => x.TaskId == "queued").State == "blocked", "Recovered queued side effects eligible automatically");
        Check.Throws<InvalidOperationException>(() => core.Retry("lost", "implicit-retry"));
        core.Submit([w.Probe("explicit-retry") with { RetryOfTaskId = "lost" }]); await core.RunUntilIdleAsync();
        Check.That(calls == 1, "Explicit recovery resubmission did not run"); Export.AddRange(core.Snapshot());
    }

    static async Task Streams()
    {
        using var w = new Workspace();
        var handler = new Handler(CommandKind.DependencyProbe, (_, context, _) =>
        { for (int i = 0; i < 400; i++) context.Report(TaskPhase.Executing, i, 400, "steps"); return Task.FromResult(Success()); });
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var watcher = core.WatchProgressAsync(stop.Token).GetAsyncEnumerator();
        var first = watcher.MoveNextAsync().AsTask(); core.Submit([w.Probe("bounded")]); await first;
        await core.RunUntilIdleAsync();
        Check.That(core.Logs().Count == 256 && core.Snapshot().Single().State == "succeeded", "Bounded logs lost durable final state");
        while (await watcher.MoveNextAsync())
        {
            if (watcher.Current.Completed is not null) Export.Add(watcher.Current);
            if (watcher.Current.State == "succeeded") break;
        }
        Check.That(File.ReadAllBytes(Path.Combine(w.Root, "task-logs.json")).Length < 128 * 1024, "Log bytes unbounded");
        Export.AddRange(core.Snapshot());
    }

    static async Task References()
    {
        using var w = new Workspace();
        foreach (var value in new[] { "../escape", "/absolute", "C:/drive", "reports/control_auth_cookie", "key.pem", "onion-service/id" })
            Check.Throws<InvalidDataException>(() => w.Files.Resolve(value));
        if (OperatingSystem.IsWindows())
            foreach (var value in new[] { "NUL", "CON.txt", "folder/COM1", "artifact.", "folder./child" })
                Check.Throws<InvalidDataException>(() => w.Files.Resolve(value));
        var handler = new Handler(CommandKind.NativeLinuxBuild, (_, _, _) => Task.FromResult(Success()));
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        core.Submit([new("no-output", new NativeLinuxBuildCommand(w.Input, [], ["missing.bin"]), [], new(1, 1, 1, false))]);
        await core.RunUntilIdleAsync(); Check.That(core.Snapshot().Single().State == "failed", "Unverified output marked successful");
    }

    static Task Binding()
    {
        var row = new TaskProgress(1, "binding", null, 1, DateTimeOffset.UtcNow, "executing", "running", 1, 2, "steps", "Synthetic", true, null, null, null, null, null);
        row.Validate();
        Check.Throws<InvalidDataException>(() => (row with { Completed = double.NaN }).Validate());
        Check.Throws<InvalidDataException>(() => (row with { Completed = 3 }).Validate());
        Check.Throws<InvalidDataException>(() => (row with { State = "interrupted" }).Validate());
        Check.Throws<InvalidDataException>(() => (row with { State = "succeeded", RawExitCode = 9 }).Validate());
        Check.Throws<InvalidDataException>(() => (row with { SchemaVersion = 2 }).Validate());
        Check.That(!TaskOrchestrator.Allowed("succeeded", "running"), "Terminal state reopened");
        var json = JsonSerializer.Serialize(row with { State = "failed", RawExitCode = uint.MaxValue }, ContractJson.Options);
        foreach (var spellings in new[] { ("1.0", "2e0", "4294967295.0"), ("1e0", "2.0", "4.294967295e9") })
        {
            var alternate = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": " + spellings.Item1)
                .Replace("\"sequence\": 1", "\"sequence\": " + spellings.Item2)
                .Replace("\"rawExitCode\": 4294967295", "\"rawExitCode\": " + spellings.Item3);
            var parsed = JsonSerializer.Deserialize<TaskProgress>(alternate, ContractJson.Options)!;
            parsed.Validate(); Check.That(parsed.SchemaVersion == 1 && parsed.Sequence == 2 && parsed.RawExitCode == uint.MaxValue, "Integral JSON spelling rejected");
        }
        foreach (var bad in new[] { "true", "1.5", "1e-100", "1.00000000000000000000000000000001", "1e999", "4294967296" })
            Check.Throws<JsonException>(() => JsonSerializer.Deserialize<TaskProgress>(json.Replace("\"rawExitCode\": 4294967295", "\"rawExitCode\": " + bad), ContractJson.Options));
        Check.Throws<JsonException>(() => JsonSerializer.Deserialize<TaskProgress>(json.Replace("\"cancellationSupported\": true,", ""), ContractJson.Options));
        Check.Throws<JsonException>(() => JsonSerializer.Deserialize<TaskProgress>(json.Replace("\"parentTaskId\": null,", ""), ContractJson.Options));
        return Task.CompletedTask;
    }

    static async Task Deadline()
    {
        using var w = new Workspace();
        var handler = new Handler(CommandKind.DependencyProbe, async (_, _, token) =>
        { await Task.Delay(Timeout.Infinite, token); return Success(); });
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity,
            maximumTaskDuration: TimeSpan.FromMilliseconds(30));
        core.Submit([w.Probe("deadline")]); await core.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check.That(core.Snapshot().Single().State == "cancelled" && core.Snapshot().Single().ReasonCode == "deadline-exceeded", "Deadline lost or mislabeled");
    }
    static async Task UnexpectedExit()
    {
        using var w = new Workspace();
        var handler = new Handler(CommandKind.DependencyProbe, (_, _, _) => Task.FromResult(new TaskExecutionResult(0, FailureReason: "unexpected-exit")));
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        core.Submit([w.Probe("unexpected")]); await core.RunUntilIdleAsync();
        Check.That(core.Snapshot().Single().State == "failed" && core.Snapshot().Single().RawExitCode == 0, "Unexpected exit0 hidden as success");
    }
}
