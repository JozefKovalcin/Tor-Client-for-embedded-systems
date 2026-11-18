using Tor.Contracts;

namespace Tor.Core;

public enum CommandKind { DependencyProbe, NativeLinuxBuild, WindowsTorSession, WindowsDeveloper }
public enum WindowsDeveloperAction { Scan, PlanInstall, Install, Build, List, Smoke, Candidate }
public enum TaskPhase { Queued, Preparing, Executing, Verifying, Stopping, Complete, Recovery }
public enum TaskMessage { Queued, Started, Progress, Succeeded, Failed, Blocked, Cancelling, Cancelled, Interrupted }

/// <summary>Only registered typed handlers can execute work. There is no shell command field.</summary>
public abstract record TaskCommand(CommandKind Kind, IReadOnlyList<VerifiedFile> Inputs,
    IReadOnlyList<string> OutputRefs);
public sealed record DependencyProbeCommand(IReadOnlyList<VerifiedFile> ProbeInputs)
    : TaskCommand(CommandKind.DependencyProbe, ProbeInputs, Array.Empty<string>());
public sealed record NativeLinuxBuildCommand(VerifiedFile Request, IReadOnlyList<VerifiedFile> BuildInputs,
    IReadOnlyList<string> BuildOutputs)
    : TaskCommand(CommandKind.NativeLinuxBuild, new[] { Request }.Concat(BuildInputs).ToArray(), BuildOutputs);
public sealed record WindowsTorSessionCommand(VerifiedFile Executable, IReadOnlyList<VerifiedFile> RuntimeInputs,
    int? OnlineSocksPort = null)
    : TaskCommand(CommandKind.WindowsTorSession, new[] { Executable }.Concat(RuntimeInputs).ToArray(), Array.Empty<string>());
public sealed record WindowsDeveloperCommand(WindowsDeveloperAction Action, string Profile, string Sources,
    string Workspace, string? Approval = null, string? BuildId = null)
    : TaskCommand(CommandKind.WindowsDeveloper, Array.Empty<VerifiedFile>(), Array.Empty<string>());

public sealed record ResourceRequest(int Slots, long MemoryBytes, long DiskBytes, bool Heavy)
{
    public void Validate()
    {
        if (Slots < 1 || MemoryBytes < 0 || DiskBytes < 0) throw new ArgumentOutOfRangeException(nameof(Slots));
    }
}
public sealed record TaskSubmission(string Id, TaskCommand Command, IReadOnlyList<string> DependsOn,
    ResourceRequest Resources, string? RetryOfTaskId = null);
public enum WindowsTorFailure { SocksPortInUse, SocksPortUnavailable, UnverifiedSocksListener, BootstrapTimeout, ControlLost, UnexpectedExit }
public sealed record TaskExecutionResult(uint? ExitCode, int? Signal = null,
    IReadOnlyList<VerifiedFile>? Outputs = null, string? BlockedReason = null, string? FailureReason = null,
    WindowsTorFailure? WindowsFailure = null)
{
    public static TaskExecutionResult Success => new(0);
}
public interface ITaskHandler
{
    CommandKind Kind { get; }
    Task<TaskExecutionResult> ExecuteAsync(TaskCommand command, TaskExecutionContext context,
        CancellationToken cancellationToken);
}
public interface ITaskService
{
    IReadOnlyList<TaskProgress> Snapshot();
    void Submit(IReadOnlyList<TaskSubmission> graph);
    Task RunUntilIdleAsync(CancellationToken cancellationToken = default);
    void Cancel(string taskId);
    void Retry(string originalId, string newId);
    IAsyncEnumerable<TaskProgress> WatchProgressAsync(CancellationToken cancellationToken = default);
}

public sealed class TaskExecutionContext
{
    private readonly Action<TaskPhase, double?, double?, string?> report;
    internal TaskExecutionContext(Action<TaskPhase, double?, double?, string?> report) => this.report = report;
    public void Report(TaskPhase phase, double? completed = null, double? total = null, string? unit = null)
        => report(phase, completed, total, unit);
}
