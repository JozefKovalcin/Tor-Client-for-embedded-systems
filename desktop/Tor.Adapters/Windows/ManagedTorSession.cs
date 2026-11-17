using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Tor.Control;
using Tor.Core;

namespace Tor.Adapters.Windows;

public sealed record TorProcessOutcome(uint RawExitCode, bool StopRequested);
public sealed class TorProcessStartException(uint? rawExitCode, string phase, bool cancelled)
    : IOException("Managed Tor startup failed during " + phase)
{
    public uint? RawExitCode { get; } = rawExitCode;
    public bool Cancelled { get; } = cancelled;
}

/// <summary>Own one verified Tor process and fresh private state, with networking disabled for this foundation.</summary>
[SupportedOSPlatform("windows")]
public sealed class ManagedTorSession : IAsyncDisposable
{
    private readonly Process process;
    private readonly WindowsJob job;
    private readonly IReadOnlyList<FileStream> runtimeLocks;
    private readonly Task drainOutput, drainError;
    private readonly SemaphoreSlim stopGate = new(1, 1);
    private TorControlClient? control;
    private bool stopRequested;
    private bool disposed;
    public int ProcessId => process.Id;
    public TorControlClient Control => control ?? throw new InvalidOperationException("Control authentication not complete");
    public Task<TorProcessOutcome> Completion { get; }
    private ManagedTorSession(Process process, WindowsJob job, IReadOnlyList<FileStream> runtimeLocks)
    {
        this.process = process; this.job = job; this.runtimeLocks = runtimeLocks;
        drainOutput = DiscardAsync(process.StandardOutput); drainError = DiscardAsync(process.StandardError);
        Completion = ObserveExitAsync();
    }
    private async Task<TorProcessOutcome> ObserveExitAsync()
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        return new(unchecked((uint)process.ExitCode), stopRequested);
    }
    private static async Task DiscardAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        try { while (await reader.ReadAsync(buffer).ConfigureAwait(false) > 0) { } }
        catch (IOException) { }
        // Raw Tor output can contain sensitive paths/identifiers; it is neither retained nor exported.
    }
    public static async Task<ManagedTorSession> StartOfflineAsync(PinnedWindowsRuntime runtime,
        string existingPrivateParent, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows runtime adapter");
        PrivateStorage.RequireOwnerOnly(existingPrivateParent);
        var locks = runtime.VerifyAndLock();
        WindowsJob? job = null; Process? process = null; ManagedTorSession? session = null;
        bool processStarted = false; string phase = "preparation";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = Path.Combine(existingPrivateParent, "session-" + Guid.NewGuid().ToString("N"));
            PrivateStorage.CreateNewDirectory(root);
            phase = "runtime-staging";
            var staged = PinnedWindowsRuntime.StageVerifiedFiles(locks, Path.Combine(root, "runtime"));
            locks = locks.Concat(staged.Locks).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            var data = Path.Combine(root, "state"); PrivateStorage.CreateNewDirectory(data);
            var temporary = Path.Combine(root, "temporary"); PrivateStorage.CreateNewDirectory(temporary);
            var cookiePath = Path.Combine(root, "control-cookie");
            var portPath = Path.Combine(root, "control-port");
            var defaults = Path.Combine(root, "defaults-torrc"); var config = Path.Combine(root, "torrc");
            await File.WriteAllTextAsync(defaults, "# Intentionally empty managed defaults\n", new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            string Quote(string value)
            {
                if (value.Any(c => c < 32 || c == 127)) throw new InvalidDataException("Configuration path contains control characters");
                return "\"" + value.Replace("\\", "/").Replace("\"", "\\\"") + "\"";
            }
            var torrc = $"DataDirectory {Quote(data)}\nCookieAuthFile {Quote(cookiePath)}\n" +
                $"ControlPortWriteToFile {Quote(portPath)}\nControlPort 127.0.0.1:auto\n" +
                "CookieAuthentication 1\nCookieAuthFileGroupReadable 0\nClientOnly 1\nSocksPort 0\n" +
                "DisableNetwork 1\nSafeLogging 1\nLog warn stdout\n" +
                $"__OwningControllerProcess {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}\n";
            await File.WriteAllTextAsync(config, torrc, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            var start = new ProcessStartInfo(staged.ExecutablePath)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "--defaults-torrc", defaults, "-f", config }) start.ArgumentList.Add(argument);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            start.Environment.Clear();
            start.Environment["SystemRoot"] = windows; start.Environment["WINDIR"] = windows;
            start.Environment["PATH"] = Path.GetDirectoryName(staged.ExecutablePath) + Path.PathSeparator + Path.Combine(windows, "System32");
            start.Environment["TEMP"] = temporary; start.Environment["TMP"] = temporary;
            job = new WindowsJob(); process = new Process { StartInfo = start };
            phase = "process-start";
            if (!process.Start()) throw new IOException("Managed Tor did not start");
            processStarted = true;
            job.Assign(process);
            session = new ManagedTorSession(process, job, locks);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(TimeSpan.FromSeconds(15));
            int port;
            phase = "control-startup";
            while (true)
            {
                startup.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException("Managed Tor exited before authenticated control became ready");
                if (File.Exists(portPath) && File.Exists(cookiePath))
                {
                    if ((File.GetAttributes(portPath) & FileAttributes.ReparsePoint) != 0 ||
                        (File.GetAttributes(cookiePath) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Managed control files must not be links");
                    var text = await File.ReadAllTextAsync(portPath, startup.Token).ConfigureAwait(false);
                    var match = Regex.Match(text.Trim(), @"\APORT=127\.0\.0\.1:(\d{1,5})\z", RegexOptions.CultureInvariant);
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out port) || port is < 1 or > 65535)
                        throw new InvalidDataException("Managed Tor published an unsafe control endpoint");
                    if (new FileInfo(cookiePath).Length == 32) break;
                }
                await Task.Delay(40, startup.Token).ConfigureAwait(false);
            }
            session.control = await TorControlClient.ConnectAsync(IPAddress.Loopback, port, startup.Token).ConfigureAwait(false);
            phase = "authentication";
            var cookie = await File.ReadAllBytesAsync(cookiePath, startup.Token).ConfigureAwait(false);
            try { await session.control.AuthenticateAsync(cookie, startup.Token).ConfigureAwait(false); }
            finally { CryptographicOperations.ZeroMemory(cookie); }
            await session.control.TakeOwnershipAsync(startup.Token).ConfigureAwait(false);
            await session.control.SubscribeBootstrapAsync(startup.Token).ConfigureAwait(false);
            await session.control.GetBootstrapAsync(startup.Token).ConfigureAwait(false);
            return session;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            uint? observedExit = null;
            if (session is not null)
            {
                try { observedExit = (await session.StopAsync().ConfigureAwait(false)).RawExitCode; }
                finally { await session.DisposeAsync().ConfigureAwait(false); }
            }
            else
            {
                try
                {
                    if (process is not null && processStarted)
                    {
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        observedExit = unchecked((uint)process.ExitCode);
                    }
                }
                finally { job?.Dispose(); process?.Dispose(); foreach (var item in locks) item.Dispose(); }
            }
            throw new TorProcessStartException(observedExit, phase, cancellationToken.IsCancellationRequested);
        }
    }
    public async Task<TorProcessOutcome> StopAsync()
    {
        await stopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return await Completion.ConfigureAwait(false);
            if (!process.HasExited)
            {
                stopRequested = true;
                if (control is not null)
                {
                    using var graceful = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try { await control.ShutdownAsync(graceful.Token).ConfigureAwait(false); }
                    catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or System.Threading.Channels.ChannelClosedException) { }
                }
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (TimeoutException) { job.Dispose(); if (!process.HasExited) process.Kill(entireProcessTree: true); }
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return await Completion.ConfigureAwait(false);
        }
        finally { stopGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try { await StopAsync().ConfigureAwait(false); }
        finally
        {
            disposed = true; job.Dispose();
            try
            {
                if (control is not null) await control.DisposeAsync().ConfigureAwait(false);
                await Task.WhenAll(drainOutput, drainError).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally { foreach (var stream in runtimeLocks) stream.Dispose(); process.Dispose(); }
        }
        // Private state remains owned by the endpoint/session; no automatic deletion or export.
    }
}
