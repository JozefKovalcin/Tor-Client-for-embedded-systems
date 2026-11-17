using Tor.Contracts;
using Tor.Core;

namespace Tor.Application;

public enum RuntimePackageStatus { Unchecked, Available, Missing, Invalid, Blocked }
public enum TorSessionStatus { Stopped, Starting, RunningOffline, Stopping, Failed, Interrupted }
public enum TorControlStatus { Disconnected, Authenticating, Authenticated }

public sealed record OfflineTorSnapshot(RuntimePackageStatus PackageStatus, TorSessionStatus SessionStatus,
    TorControlStatus ControlStatus, int? ProcessId, int? BootstrapPercent, string? BootstrapTag,
    string? LastError, TaskProgress? CurrentTask, IReadOnlyList<TaskProgress> Tasks,
    IReadOnlyList<TaskLog> Logs, bool CanStart, bool CanStop);

public interface IOfflineTorService : IAsyncDisposable
{
    OfflineTorSnapshot Snapshot { get; }
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<OfflineTorSnapshot> WatchAsync(CancellationToken cancellationToken = default);
}
