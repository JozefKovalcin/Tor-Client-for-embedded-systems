using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Tor.Contracts;

namespace Tor.Core;

public sealed record TaskLog(DateTimeOffset TimestampUtc, string TaskId, long Sequence,
    TaskMessage Event, uint? RawExitCode, int? TerminationSignal);

/// <summary>Bounded, dependency-aware task lifecycle; adapters own process cancellation and exit collection.</summary>
public sealed class TaskOrchestrator : ITaskService, IAsyncDisposable
{
    private sealed class Entry(TaskSubmission? submission, TaskProgress progress)
    {
        public TaskSubmission? Submission { get; } = submission;
        public TaskProgress Progress { get; set; } = progress;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Active { get; set; }
        public string CancellationReason { get; set; } = "deadline-exceeded";
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<CommandKind, ITaskHandler> handlers;
    private readonly List<Channel<TaskProgress>> subscribers = [];
    private readonly Queue<TaskLog> logs = new();
    private readonly TaskJournal journal;
    private readonly VerifiedFileStore files;
    private readonly ResourceRequest capacity;
    private readonly int maximumTasks;
    private readonly TimeSpan maximumTaskDuration;
    private readonly SemaphoreSlim pump = new(1, 1);
    private bool disposed;
    private bool stopping;

    public TaskOrchestrator(IEnumerable<ITaskHandler> handlers, VerifiedFileStore files,
        TaskJournal journal, ResourceRequest capacity, int maximumTasks = 256,
        string interruptionReason = "app-crash", TimeSpan? maximumTaskDuration = null)
    {
        capacity.Validate();
        if (maximumTasks is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(maximumTasks));
        if (interruptionReason is not ("app-crash" or "runner-crash" or "runner-lost"))
            throw new ArgumentException("Explicit crash/loss reason required", nameof(interruptionReason));
        this.handlers = handlers.ToDictionary(x => x.Kind);
        this.files = files; this.journal = journal; this.capacity = capacity; this.maximumTasks = maximumTasks;
        this.maximumTaskDuration = maximumTaskDuration ?? TimeSpan.FromMinutes(30);
        if (this.maximumTaskDuration <= TimeSpan.Zero || this.maximumTaskDuration > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(maximumTaskDuration));
        var recovered = journal.Read();
        if (recovered.Count > maximumTasks) throw new InvalidDataException("Recovered task capacity exceeded");
        foreach (var previous in recovered)
        {
            var row = previous;
            if (previous.State is "running" or "cancelling")
                row = previous with { Sequence = checked(previous.Sequence + 1), TimestampUtc = DateTimeOffset.UtcNow,
                    Phase = "recovery", State = "interrupted", Completed = null, Total = null, Unit = null,
                    CancellationSupported = false, ReasonCode = interruptionReason, RawExitCode = null,
                    TerminationSignal = null, Message = "Previous task outcome unknown after runner/application loss" };
            else if (previous.State == "queued")
                row = previous with { Sequence = checked(previous.Sequence + 1), TimestampUtc = DateTimeOffset.UtcNow,
                    State = "blocked", ReasonCode = "recovery-review-required", CancellationSupported = false,
                    Message = "Queued work requires explicit resubmission after recovery" };
            entries.Add(row.TaskId, new Entry(null, row));
        }
        Persist(); // Recovery never reconstructs executable commands or silently starts side effects.
    }

    public IReadOnlyList<TaskProgress> Snapshot()
    {
        lock (gate) return entries.Values.Select(x => x.Progress).ToArray();
    }
    public IReadOnlyList<TaskLog> Logs()
    {
        lock (gate) return logs.ToArray();
    }
    public void Submit(IReadOnlyList<TaskSubmission> graph)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed || stopping, this);
            if (graph.Count == 0 || graph.Count > maximumTasks - entries.Count)
                throw new InvalidOperationException("Bounded task queue capacity exceeded");
            // Freeze/recognize every command before mutating queue state. A public
            // unsupported subtype must reject the complete graph, not only its tail.
            var prepared = graph.Select(task =>
            {
                if (task.DependsOn.Count > maximumTasks || task.Command.Inputs.Count > 64 || task.Command.OutputRefs.Count > 32)
                    throw new InvalidDataException("Task edge/input/output capacity exceeded");
                return task with { DependsOn = task.DependsOn.ToArray(), Command = Freeze(task.Command) };
            }).ToArray();
            var incoming = prepared.ToDictionary(x => x.Id, StringComparer.Ordinal);
            foreach (var task in prepared)
            {
                ContractReference.Require(task.Id);
                if (entries.ContainsKey(task.Id)) throw new InvalidDataException("Task ID already used");
                task.Resources.Validate();
                if (task.DependsOn.Count > maximumTasks || task.Command.Inputs.Count > 64 || task.Command.OutputRefs.Count > 32)
                    throw new InvalidDataException("Task edge/input/output capacity exceeded");
                if (!handlers.ContainsKey(task.Command.Kind)) throw new InvalidDataException("No typed command handler registered");
                foreach (var dep in task.DependsOn)
                {
                    ContractReference.Require(dep);
                    if (!incoming.ContainsKey(dep) && !entries.ContainsKey(dep)) throw new InvalidDataException("Unknown dependency");
                }
                if (task.DependsOn.Distinct(StringComparer.Ordinal).Count() != task.DependsOn.Count)
                    throw new InvalidDataException("Duplicate dependency");
                foreach (var output in task.Command.OutputRefs) files.Resolve(output);
                if (task.Command.OutputRefs.Distinct(StringComparer.Ordinal).Count() != task.Command.OutputRefs.Count)
                    throw new InvalidDataException("Duplicate mutable output claim");
                if (task.Command.OutputRefs.Where((value, index) => task.Command.OutputRefs.Skip(index + 1).Any(other => Overlaps(value, other))).Any())
                    throw new InvalidDataException("Overlapping mutable output claims");
                if (task.RetryOfTaskId is { } retry)
                {
                    ContractReference.Require(retry);
                    if (!entries.TryGetValue(retry, out var original) || !Terminal(original.Progress.State) || original.Progress.State == "succeeded")
                        throw new InvalidDataException("Retry lineage must identify an existing unsuccessful terminal task");
                }
            }
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string id)
            {
                if (!incoming.ContainsKey(id) || visited.Contains(id)) return;
                if (!visiting.Add(id)) throw new InvalidDataException("Task dependency cycle");
                foreach (var dep in incoming[id].DependsOn) Visit(dep);
                visiting.Remove(id); visited.Add(id);
            }
            foreach (var id in incoming.Keys) Visit(id);
            var staged = prepared.Select(task =>
            {
                var progress = new TaskProgress(1, task.Id, null, 0, DateTimeOffset.UtcNow, "queued", "queued",
                    null, null, null, "Task queued", true, task.RetryOfTaskId, null, null, null, "task-logs.json");
                return new Entry(task, progress);
            }).ToArray();
            var previousRows = entries.Values.Select(x => x.Progress).ToArray();
            var proposedLogs = logs.Concat(staged.Select(item => new TaskLog(item.Progress.TimestampUtc,
                item.Progress.TaskId, item.Progress.Sequence, TaskMessage.Queued, null, null))).TakeLast(256).ToArray();
            try
            {
                // Both durable writes must succeed before admission is acknowledged,
                // mutable queue/log state changes, or subscribers see a queued event.
                journal.Write(previousRows.Concat(staged.Select(x => x.Progress)).ToArray());
                journal.WriteLogs(proposedLogs);
            }
            catch (Exception admissionError) when (admissionError is not OutOfMemoryException and not StackOverflowException)
            {
                foreach (var item in staged) item.Cancellation.Dispose();
                try { journal.Write(previousRows); }
                catch (Exception rollbackError) when (rollbackError is not OutOfMemoryException and not StackOverflowException)
                { throw new AggregateException("Admission rejected; durable journal rollback also failed", admissionError, rollbackError); }
                throw;
            }
            foreach (var item in staged) entries.Add(item.Progress.TaskId, item);
            logs.Clear(); foreach (var row in proposedLogs) logs.Enqueue(row);
            foreach (var item in staged)
                foreach (var subscriber in subscribers) subscriber.Writer.TryWrite(item.Progress);
        }
    }

    private static TaskCommand Freeze(TaskCommand command) => command switch
    {
        DependencyProbeCommand p => new DependencyProbeCommand(p.Inputs.ToArray()),
        NativeLinuxBuildCommand p => new NativeLinuxBuildCommand(p.Request, p.BuildInputs.ToArray(), p.OutputRefs.ToArray()),
        WindowsTorSessionCommand p => new WindowsTorSessionCommand(p.Executable, p.RuntimeInputs.ToArray()),
        _ => throw new InvalidDataException("Unrecognized typed command")
    };

    public void Cancel(string taskId)
    {
        lock (gate)
        {
            var item = entries[taskId];
            if (item.Progress.State == "queued")
                Set(item, "cancelled", TaskPhase.Complete, TaskMessage.Cancelled, reason: "user-cancelled");
            else if (item.Progress.State == "running")
            {
                item.CancellationReason = "user-cancelled";
                try { Set(item, "cancelling", TaskPhase.Stopping, TaskMessage.Cancelling, reason: "user-cancelled"); }
                finally { item.Cancellation.Cancel(); } // Disk/log failure must never prevent process cancellation.
            }
            else if (item.Progress.State == "cancelling") item.Cancellation.Cancel();
        }
    }

    public void Retry(string originalId, string newId)
    {
        lock (gate)
        {
            var old = entries[originalId];
            if (!Terminal(old.Progress.State) || old.Progress.State == "succeeded" || old.Submission is null)
                throw new InvalidOperationException("Retry requires an explicit resubmittable unsuccessful task");
            Submit([old.Submission with { Id = newId, RetryOfTaskId = originalId }]);
        }
    }

    public async Task RunUntilIdleAsync(CancellationToken cancellationToken = default)
    {
        await pump.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var cancellation = cancellationToken.Register(() =>
        {
            lock (gate)
                foreach (var row in entries.Values.Where(x => x.Progress.State is "running" or "queued").ToArray()) Cancel(row.Progress.TaskId);
        });
        try
        {
            while (true)
            {
                List<Task> running;
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(disposed || stopping, this);
                    if (cancellationToken.IsCancellationRequested)
                        foreach (var row in entries.Values.Where(x => x.Progress.State is "running" or "queued").ToArray()) Cancel(row.Progress.TaskId);
                    foreach (var row in entries.Values.Where(x => x.Progress.State == "queued").ToArray())
                    {
                        var task = row.Submission!;
                        if (task.DependsOn.Any(id => Terminal(entries[id].Progress.State) && entries[id].Progress.State != "succeeded"))
                            Set(row, "blocked", TaskPhase.Complete, TaskMessage.Blocked, reason: "dependency-unsuccessful");
                        else if (Exceeds(task.Resources, capacity))
                            Set(row, "blocked", TaskPhase.Complete, TaskMessage.Blocked, reason: "resource-budget-exceeded");
                        else if (!cancellationToken.IsCancellationRequested && task.DependsOn.All(id => entries[id].Progress.State == "succeeded") && CanAdmit(task))
                        {
                            row.Active = true;
                            Set(row, "running", TaskPhase.Preparing, TaskMessage.Started);
                            active.Add(ExecuteAsync(row));
                        }
                    }
                    foreach (var completedTask in active.Where(t => t.IsCompleted)) completedTask.GetAwaiter().GetResult();
                    active.RemoveAll(t => t.IsCompleted);
                    running = active.ToList();
                    if (running.Count == 0) break;
                }
                // Cancellation enters the task state machine; wait for adapter cleanup, never abandon its task.
                await Task.WhenAny(running).ConfigureAwait(false);
            }
        }
        finally { pump.Release(); }
    }
    private readonly List<Task> active = [];
    private static bool Exceeds(ResourceRequest request, ResourceRequest available) =>
        request.Slots > available.Slots || request.MemoryBytes > available.MemoryBytes || request.DiskBytes > available.DiskBytes;
    private bool CanAdmit(TaskSubmission task)
    {
        var running = entries.Values.Where(x => x.Active).Select(x => x.Submission!).ToArray();
        if (task.Resources.Slots > capacity.Slots - running.Sum(x => (long)x.Resources.Slots) ||
            task.Resources.MemoryBytes > capacity.MemoryBytes - running.Sum(x => x.Resources.MemoryBytes) ||
            task.Resources.DiskBytes > capacity.DiskBytes - running.Sum(x => x.Resources.DiskBytes) ||
            (task.Resources.Heavy && running.Any(x => x.Resources.Heavy))) return false;
        var outputs = running.SelectMany(x => x.Command.OutputRefs).ToArray();
        return !task.Command.OutputRefs.Any(value => outputs.Any(other => Overlaps(value, other)));
    }
    private static bool Overlaps(string first, string second) => first.Equals(second, StringComparison.OrdinalIgnoreCase) ||
        first.StartsWith(second + "/", StringComparison.OrdinalIgnoreCase) || second.StartsWith(first + "/", StringComparison.OrdinalIgnoreCase);
    private async Task ExecuteAsync(Entry item)
    {
        // Ensure adapter work cannot run synchronously while the admission lock is held.
        await Task.Yield();
        try
        {
            foreach (var input in item.Submission!.Command.Inputs) files.Verify(input);
            item.Cancellation.CancelAfter(maximumTaskDuration);
            item.Cancellation.Token.ThrowIfCancellationRequested();
            var context = new TaskExecutionContext((phase, done, total, unit) =>
            {
                lock (gate)
                {
                    if (item.Progress.State == "running") Set(item, "running", phase, TaskMessage.Progress, done: done, total: total, unit: unit);
                }
            });
            var result = await handlers[item.Submission.Command.Kind].ExecuteAsync(item.Submission.Command, context, item.Cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (result.Signal is < 1 or > 127 || (result.Signal is not null && result.ExitCode is not null))
                    throw new InvalidDataException("Invalid adapter process result");
                if (item.Cancellation.IsCancellationRequested)
                    CompleteCancellation(item, result.ExitCode, result.Signal);
                else if (result.BlockedReason is not null)
                    Set(item, "blocked", TaskPhase.Complete, TaskMessage.Blocked, result.ExitCode, result.Signal, "adapter-blocked");
                else if (result.FailureReason is not null)
                    Set(item, "failed", TaskPhase.Complete, TaskMessage.Failed, result.ExitCode, result.Signal, "adapter-failed");
                else if (result.ExitCode != 0 || result.Signal is not null)
                    Set(item, "failed", TaskPhase.Complete, TaskMessage.Failed, result.ExitCode, result.Signal, "process-failed");
                else
                {
                    var outputs = result.Outputs ?? Array.Empty<VerifiedFile>();
                    if (outputs.Select(x => x.Ref).Distinct(StringComparer.Ordinal).Count() != outputs.Count ||
                        !outputs.Select(x => x.Ref).Order().SequenceEqual(item.Submission.Command.OutputRefs.Order()))
                        throw new InvalidDataException("Produced outputs differ from verified claims");
                    foreach (var output in outputs) files.Verify(output);
                    Set(item, "succeeded", TaskPhase.Complete, TaskMessage.Succeeded, result.ExitCode);
                }
            }
        }
        catch (OperationCanceledException) when (item.Cancellation.IsCancellationRequested)
        {
            lock (gate) CompleteCancellation(item);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Exception messages may contain argv, paths, credentials or raw control replies.
            lock (gate) Set(item, "failed", TaskPhase.Complete, TaskMessage.Failed, reason: "adapter-or-verification-failed");
        }
        finally { lock (gate) item.Active = false; }
    }

    private void CompleteCancellation(Entry item, uint? exit = null, int? signal = null)
    {
        if (item.Progress.State == "running") Set(item, "cancelling", TaskPhase.Stopping, TaskMessage.Cancelling, reason: item.CancellationReason);
        Set(item, "cancelled", TaskPhase.Complete, TaskMessage.Cancelled, exit, signal, item.CancellationReason);
    }

    private void Set(Entry item, string state, TaskPhase phase, TaskMessage message, uint? exit = null,
        int? signal = null, string? reason = null, double? done = null, double? total = null, string? unit = null)
    {
        if (!Allowed(item.Progress.State, state)) throw new InvalidOperationException("Illegal task transition");
        var row = item.Progress with { Sequence = checked(item.Progress.Sequence + 1), TimestampUtc = DateTimeOffset.UtcNow,
            State = state, Phase = phase.ToString().ToLowerInvariant(), Message = message.ToString(), Completed = done,
            Total = total, Unit = unit, ReasonCode = reason, RawExitCode = exit, TerminationSignal = signal,
            CancellationSupported = state is "running" or "queued" };
        row.Validate();
        item.Progress = row;
        Persist(); // Durable state always precedes event publication/adapter launch.
        Publish(row, message);
    }
    private void Persist() => journal.Write(entries.Values.Select(x => x.Progress).ToArray());
    private void Publish(TaskProgress row, TaskMessage message)
    {
        logs.Enqueue(new TaskLog(row.TimestampUtc, row.TaskId, row.Sequence, message, row.RawExitCode, row.TerminationSignal));
        while (logs.Count > 256) logs.Dequeue();
        journal.WriteLogs(logs.ToArray());
        foreach (var subscriber in subscribers) subscriber.Writer.TryWrite(row);
    }
    private static bool Terminal(string state) => state is "succeeded" or "failed" or "blocked" or "cancelled" or "interrupted";
    public static bool Allowed(string from, string to) => from switch
    {
        "queued" => to is "running" or "cancelled" or "blocked",
        "running" => to is "running" or "cancelling" or "succeeded" or "failed" or "blocked" or "interrupted",
        "cancelling" => to is "cancelled" or "failed" or "interrupted",
        _ => false
    };
    public async IAsyncEnumerable<TaskProgress> WatchProgressAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<TaskProgress>(new BoundedChannelOptions(64)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed || stopping, this);
            if (subscribers.Count >= 16) throw new InvalidOperationException("Subscriber capacity exceeded");
            subscribers.Add(channel);
        }
        try { await foreach (var row in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return row; }
        finally { lock (gate) subscribers.Remove(channel); }
        // A sequence gap means resynchronize with Snapshot(); durable latest states never depend on the stream.
    }
    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        lock (gate)
        {
            if (disposed || stopping) return;
            stopping = true;
            foreach (var item in entries.Values.Where(x => x.Progress.State is "queued" or "running" or "cancelling").ToArray())
            {
                try { Cancel(item.Progress.TaskId); }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { failures.Add(error); }
            }
            foreach (var item in entries.Values.Where(x => x.Active && !x.Cancellation.IsCancellationRequested))
            {
                try { item.Cancellation.Cancel(); }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { failures.Add(error); }
            }
        }
        await pump.WaitAsync().ConfigureAwait(false);
        try
        {
            try { await Task.WhenAll(active).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { failures.Add(error); }
        }
        finally
        {
            lock (gate)
            {
                foreach (var channel in subscribers) channel.Writer.TryComplete();
                foreach (var item in entries.Values) item.Cancellation.Dispose();
                disposed = true;
                journal.Dispose();
            }
            pump.Release();
        }
        if (failures.Count > 0) throw new AggregateException("Task cleanup completed with journal/adapter failures", failures);
    }
}
