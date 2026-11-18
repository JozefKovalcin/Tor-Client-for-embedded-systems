using Tor.Contracts;
using Tor.Control;
using Tor.Core;

namespace Tor.Application;

public enum RuntimePackageStatus { Unchecked, Available, Missing, Invalid, Blocked }
public enum TorSessionStatus { Stopped, Starting, RunningOffline, Stopping, Failed, Interrupted, BootstrappingOnline, RunningOnline }
public enum TorControlStatus { Disconnected, Authenticating, Authenticated }

public sealed record OfflineTorSnapshot(RuntimePackageStatus PackageStatus, TorSessionStatus SessionStatus,
    TorControlStatus ControlStatus, int? ProcessId, int? BootstrapPercent, string? BootstrapTag,
    string? LastError, TaskProgress? CurrentTask, IReadOnlyList<TaskProgress> Tasks,
    IReadOnlyList<TaskLog> Logs, bool CanStart, bool CanStop, OnlineTorSnapshot? Online = null,
    string? VerifiedRuntimeSha256 = null);

public interface IOfflineTorService : IAsyncDisposable
{
    OfflineTorSnapshot Snapshot { get; }
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<OfflineTorSnapshot> WatchAsync(CancellationToken cancellationToken = default);
}

public sealed record OnlineTorSnapshot(int RequestedSocksPort, int? SocksPort, string? TorVersion,
    bool? CircuitEstablished, bool CanUseOnlineActions);

public enum OnlineActionStatus { Succeeded, Submitted, RateLimited, Unavailable, Failed }
public sealed record OnlineActionResult(OnlineActionStatus Status, string Message);

public interface IOnlineTorService : IOfflineTorService
{
    const int DefaultSocksPort = 9150;
    Task StartOnlineAsync(int socksPort, CancellationToken cancellationToken = default);
    Task RestartOnlineAsync(int socksPort, CancellationToken cancellationToken = default);
    Task<OnlineActionResult> RequestNewIdentityAsync(CancellationToken cancellationToken = default);
    Task<OnlineActionResult> CheckConnectivityAsync(CancellationToken cancellationToken = default);
}

public interface IPeerTorService : IOnlineTorService
{
    IOnionPeerTransport? GetPeerTransport();
}
