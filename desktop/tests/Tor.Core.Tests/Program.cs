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
sealed record UnsupportedCommand() : TaskCommand(CommandKind.DependencyProbe, Array.Empty<VerifiedFile>(), Array.Empty<string>());

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
            ("bounded-task-deadline", Deadline), ("unexpected-zero-exit-is-not-success", UnexpectedExit),
            ("unsupported-command-rejects-whole-graph", AtomicCommandRejection),
            ("journal-failure-still-cancels-and-cleans-up", JournalFailureCancellation),
            ("log-publication-failure-rejects-admission", LogFailureAdmission),
            ("launch-task-state-write-failure-releases-reservation", () => LaunchWriteFailure("tasks.json")),
            ("launch-log-write-failure-releases-reservation", () => LaunchWriteFailure("task-logs.json")),
            ("terminal-log-write-failure-does-not-report-success", TerminalWriteFailure),
            ("pump-cancellation-awaits-all-adapters-during-journal-failure", PumpCancellationWriteFailure)
        };
        var selected = args is ["--test", var group] ? tests.Where(t => t.Name == group).ToArray() : tests;
        Check.That(selected.Length > 0, "Unknown test group");
        foreach (var (name, test) in selected) { await Task.Run(test).WaitAsync(TimeSpan.FromSeconds(15)); Console.WriteLine("PASS " + name); }
        if (args.Length == 1) File.WriteAllText(args[0], JsonSerializer.Serialize(Export, ContractJson.Options));
        Console.WriteLine($"{selected.Length} backend behavior groups PASS");
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
        core.Submit([w.Probe("three", ["two"])]);
        await core.RunUntilIdleAsync();
        Check.That(core.Snapshot().Select(x => x.TaskId).ToHashSet().SetEquals(["two", "three"]) &&
            Directory.GetFiles(w.Root, "history-*.json").Length == 1, "Finished tasks did not make space without losing a dependency");
        Check.Throws<InvalidDataException>(() => core.Submit([w.Probe("one")]));
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
        var events = core.Logs().ToArray();
        await core.DisposeAsync();
        await using var reopened = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        Check.That(events.Length > 0 && reopened.Logs().SequenceEqual(events), "Reopen lost structured events");
        reopened.Submit([w.Probe("next")]);
        Check.That(reopened.Logs().Take(events.Length).SequenceEqual(events), "Next submission overwrote historical events");
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
        Check.Throws<InvalidDataException>(() => (row with { State = "failed", ReasonCode = "private_key" }).Validate());
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
        using var sessionWorkspace = new Workspace();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new Handler(CommandKind.WindowsTorSession, async (_, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Success();
        });
        await using var longRunning = new TaskOrchestrator([session], sessionWorkspace.Files, sessionWorkspace.Journal(),
            Capacity, maximumTaskDuration: TimeSpan.FromMilliseconds(30));
        longRunning.Submit([new("tor-session", new WindowsTorSessionCommand(sessionWorkspace.Input, []), [], new(1, 0, 0, false))]);
        var pending = longRunning.RunUntilIdleAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(100);
        Check.That(longRunning.Snapshot().Single().State == "running", "Healthy Tor received a finite task deadline");
        longRunning.Cancel("tor-session");
        await pending.WaitAsync(TimeSpan.FromSeconds(3));
        Check.That(longRunning.Snapshot().Single().ReasonCode == "user-cancelled", "Tor no longer responds to explicit Stop");
    }
    static async Task UnexpectedExit()
    {
        using var w = new Workspace();
        var handler = new Handler(CommandKind.DependencyProbe, (_, _, _) => Task.FromResult(new TaskExecutionResult(0, FailureReason: "unexpected-exit")));
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        core.Submit([w.Probe("unexpected")]); await core.RunUntilIdleAsync();
        Check.That(core.Snapshot().Single().State == "failed" && core.Snapshot().Single().RawExitCode == 0, "Unexpected exit0 hidden as success");
    }
    static async Task AtomicCommandRejection()
    {
        using var w = new Workspace(); int calls = 0;
        var handler = new Handler(CommandKind.DependencyProbe, (_, _, _) => { calls++; return Task.FromResult(Success()); });
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        Check.Throws<InvalidDataException>(() => core.Submit([w.Probe("valid-before-unsupported"),
            new("unsupported", new UnsupportedCommand(), [], new(1, 1, 1, false))]));
        Check.That(core.Snapshot().Count == 0, "Rejected graph was partially queued");
        await core.RunUntilIdleAsync(); Check.That(calls == 0, "Rejected graph executed a command");
    }
    static async Task JournalFailureCancellation()
    {
        foreach (var cancelFirst in new[] { true, false })
        {
            using var w = new Workspace(); int started = 0, cleaned = 0;
            var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Handler(CommandKind.DependencyProbe, async (_, _, token) =>
            {
                if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { Interlocked.Increment(ref cleaned); }
                return Success();
            });
            var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
            core.Submit([w.Probe("first"), w.Probe("second")]); var running = core.RunUntilIdleAsync();
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var path = Path.Combine(w.Root, "tasks.json"); File.Move(path, path + ".preserved"); Directory.CreateDirectory(path);
            Exception? cancellationError = null, disposalError = null, runnerError = null;
            if (cancelFirst) { try { core.Cancel("first"); } catch (Exception error) { cancellationError = error; } }
            try { await core.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception error) { disposalError = error; }
            try { await running; } catch (Exception error) { runnerError = error; }
            if (cancelFirst) Check.That(cancellationError is IOException or UnauthorizedAccessException, "Expected cancellation journal failure");
            Check.That(disposalError is AggregateException, "Expected aggregated disposal journal failure, got " + disposalError?.GetType().Name);
            Check.That(runnerError is IOException or UnauthorizedAccessException or ObjectDisposedException, "Unexpected runner storage-failure classification");
            Check.That(cleaned == 2, "Journal failure prevented an adapter from receiving/finishing cancellation");
            using var reopened = w.Journal(); // Disposal must release single-writer ownership despite recorded failures.
        }
    }
    static async Task LogFailureAdmission()
    {
        using var w = new Workspace(); int calls = 0;
        var handler = new Handler(CommandKind.DependencyProbe, (_, _, _) => { calls++; return Task.FromResult(Success()); });
        await using var core = new TaskOrchestrator([handler], w.Files, w.Journal(), Capacity);
        core.Submit([w.Probe("kept")]); await core.RunUntilIdleAsync();
        var beforeRows = core.Snapshot().ToArray(); var beforeLogs = core.Logs().ToArray();
        using var cancel = new CancellationTokenSource();
        await using var subscriber = core.WatchProgressAsync(cancel.Token).GetAsyncEnumerator();
        var pending = subscriber.MoveNextAsync().AsTask();
        var path = Path.Combine(w.Root, "task-logs.json"); File.Move(path, path + ".preserved"); Directory.CreateDirectory(path);
        Exception? rejected = null;
        try { core.Submit([w.Probe("rejected-log")]); } catch (Exception error) { rejected = error; }
        Check.That(rejected is IOException or UnauthorizedAccessException, "Expected log publication I/O failure");
        Check.That(core.Snapshot().SequenceEqual(beforeRows) && core.Logs().SequenceEqual(beforeLogs), "Rejected graph altered old queue/log snapshots");
        Check.That(!pending.IsCompleted, "Rejected graph published a queued event");
        cancel.Cancel(); try { await pending; } catch (OperationCanceledException) { }
        Directory.Delete(path); File.Move(path + ".preserved", path); // Only undo this test's empty fault-injection directory.
        await core.RunUntilIdleAsync(); Check.That(calls == 1, "Rejected graph executed after log storage recovered");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(w.Root, "tasks.json")));
        Check.That(document.RootElement.GetArrayLength() == 1, "Rejected graph remained in durable task journal");
    }

    static async Task RequireWriteFailure(Task operation)
    {
        try { await operation.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected an observable journal write failure");
    }

    static async Task LaunchWriteFailure(string destination)
    {
        using var w = new Workspace();
        using var journal = w.Journal();
        int calls = 0;
        var handler = new Handler(CommandKind.DependencyProbe, (_, _, _) =>
        { Interlocked.Increment(ref calls); return Task.FromResult(Success()); });
        await using var core = new TaskOrchestrator([handler], w.Files, journal, new(1, 100, 100, true));
        core.Submit([w.Probe("first"), w.Probe("independent")]);
        var queued = core.Snapshot().Single(x => x.TaskId == "first");
        var path = Path.Combine(w.Root, destination);
        var preserved = path + ".preserved";
        File.Move(path, preserved);
        Directory.CreateDirectory(path);
        try
        {
            await RequireWriteFailure(core.RunUntilIdleAsync());
            var blocked = core.Snapshot().Single(x => x.TaskId == "first");
            Console.WriteLine($"{destination}: handler calls={calls}, state={blocked.State}, reason={blocked.ReasonCode}");
            Check.That(calls == 0 && blocked.State == "blocked" && blocked.Sequence > queued.Sequence &&
                blocked.ReasonCode == "journal-start-failed", "Failed launch retained a running task or ran an adapter");
            Check.That(core.Logs().All(x => x.Event != TaskMessage.Started), "Failed launch published a started event");
            if (destination == "task-logs.json")
                Check.That(journal.Read().SequenceEqual(core.Snapshot()), "Durable task state did not record the blocked launch");
        }
        finally
        {
            Directory.Delete(path);
            File.Move(preserved, path);
        }

        await core.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check.That(calls == 1 && core.Snapshot().Single(x => x.TaskId == "independent").State == "succeeded",
            "Failed launch leaked its reservation or replayed unexpectedly");
        Check.That(core.Snapshot().Single(x => x.TaskId == "first").State == "blocked", "Failed launch was replayed");
        Check.That(journal.Read().SequenceEqual(core.Snapshot()), "Recovered scheduler and durable task state disagree");

        core.Retry("first", "retry");
        await core.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check.That(calls == 2 && core.Snapshot().Single(x => x.TaskId == "retry") is
            { State: "succeeded", RetryOfTaskId: "first" }, "An explicit retry did not run after storage recovered");
    }

    static async Task TerminalWriteFailure()
    {
        using var w = new Workspace();
        using var journal = w.Journal();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var handler = new Handler(CommandKind.DependencyProbe, async (_, _, token) =>
        {
            if (Interlocked.Increment(ref calls) != 1) return Success();
            started.SetResult();
            await release.Task.WaitAsync(token);
            return Success();
        });
        await using var core = new TaskOrchestrator([handler], w.Files, journal, new(1, 100, 100, true));
        core.Submit([w.Probe("first"), w.Probe("dependent", ["first"])]);
        var run = core.RunUntilIdleAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var path = Path.Combine(w.Root, "task-logs.json");
        var preserved = path + ".preserved";
        File.Move(path, preserved);
        Directory.CreateDirectory(path);
        try
        {
            release.SetResult();
            Console.WriteLine("Injecting a log write failure after the first adapter returns exit zero");
            await RequireWriteFailure(run);
            Check.That(calls == 1 && core.Snapshot().Single(x => x.TaskId == "first") is
                { State: "failed", ReasonCode: "journal-result-failed", RawExitCode: 0 }, "Failed terminal publication was not reported accurately");
            Check.That(journal.Read().SequenceEqual(core.Snapshot()), "Durable tasks falsely claim a completed result");
        }
        finally
        {
            Directory.Delete(path);
            File.Move(preserved, path);
        }

        core.Submit([w.Probe("independent")]);
        await core.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check.That(calls == 2 && core.Snapshot().Single(x => x.TaskId == "dependent").State == "blocked",
            "Faulted task remained in the pump or its dependent executed");
    }

    static async Task PumpCancellationWriteFailure()
    {
        using var w = new Workspace();
        using var token = new CancellationTokenSource();
        using var journal = w.Journal();
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int started = 0, cleaned = 0;
        var handler = new Handler(CommandKind.DependencyProbe, async (_, _, cancellation) =>
        {
            if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { Interlocked.Increment(ref cleaned); }
            return new TaskExecutionResult(23);
        });
        await using var core = new TaskOrchestrator([handler], w.Files, journal, new(2, 100, 100, true));
        core.Submit([w.Probe("first"), w.Probe("second"), w.Probe("queued", ["first"])]);
        var run = core.RunUntilIdleAsync(token.Token);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var path = Path.Combine(w.Root, "tasks.json");
        var preserved = path + ".preserved";
        File.Move(path, preserved);
        Directory.CreateDirectory(path);
        try
        {
            token.Cancel();
            Exception? failure = null;
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (AggregateException error) { failure = error; }
            Check.That(failure is AggregateException aggregate && aggregate.InnerExceptions.Count >= 2 &&
                aggregate.InnerExceptions.All(error => error is IOException or UnauthorizedAccessException),
                "Pump cancellation did not report the journal failures");
            Check.That(cleaned == 2 && started == 2, "Cancellation did not wait for both adapters or started queued work");
            Check.That(core.Snapshot().All(row => row.State == "cancelled" && row.RawExitCode == (row.TaskId == "queued" ? null : 23)),
                "Cancellation returned without recording the observed cleanup and original result");
        }
        finally
        {
            Directory.Delete(path);
            File.Move(preserved, path);
        }
        await core.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check.That(journal.Read().SequenceEqual(core.Snapshot()), "The pump did not reconcile durable cancellation after storage recovered");
    }
}
