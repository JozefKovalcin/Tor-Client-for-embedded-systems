using System.Diagnostics;
using System.Runtime.Versioning;
using Tor.Core;

namespace Tor.Adapters.Windows;

public sealed record WindowsDeveloperResult(uint ExitCode, string Output, string Errors);

[SupportedOSPlatform("windows")]
public sealed class WindowsDeveloperRunner(Action<string> log) : ITaskHandler
{
    private static readonly string[] PythonPaths = [
        @"C:\Python314\python.exe", @"C:\Python313\python.exe", @"C:\Python312\python.exe",
        @"C:\Program Files\Python314\python.exe", @"C:\Program Files\Python313\python.exe",
        @"C:\Program Files\Python312\python.exe", @"C:\msys64\ucrt64\bin\python.exe"];
    public CommandKind Kind => CommandKind.WindowsDeveloper;

    public async Task<TaskExecutionResult> ExecuteAsync(TaskCommand command, TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (command is not WindowsDeveloperCommand task ||
            task.Action is not (WindowsDeveloperAction.Install or WindowsDeveloperAction.Build or WindowsDeveloperAction.Smoke))
            return new(null, BlockedReason: "unsupported-developer-operation");
        var result = await RunAsync(task, log, cancellationToken, phase => context.Report(phase)).ConfigureAwait(false);
        return new(result.ExitCode);
    }

    public static async Task<WindowsDeveloperResult> RunAsync(WindowsDeveloperCommand task, Action<string>? log,
        CancellationToken cancellationToken, Action<TaskPhase>? progress = null)
    {
        var launcher = OrdinaryFile(Path.Combine(AppContext.BaseDirectory, "developer", "scripts", "windows_developer", "launch.py"));
        var python = PythonPaths.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Developer mode needs an approved native Windows Python 3.12 or newer.");
        OrdinaryFile(python);
        RequireLocal(task.Sources);
        RequireLocal(task.Workspace);
        var action = task.Action switch
        {
            WindowsDeveloperAction.Scan => "scan",
            WindowsDeveloperAction.PlanInstall => "plan-install",
            WindowsDeveloperAction.Install => "install",
            WindowsDeveloperAction.Build => "build",
            WindowsDeveloperAction.List => "list",
            WindowsDeveloperAction.Smoke => "smoke",
            WindowsDeveloperAction.Candidate => "candidate",
            _ => throw new ArgumentOutOfRangeException(nameof(task))
        };
        var system = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(launcher)!,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        start.Environment.Clear();
        start.Environment["SystemRoot"] = system;
        start.Environment["WINDIR"] = system;
        start.Environment["PATH"] = Path.Combine(system, "System32");
        start.Environment["ProgramFiles"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var arg in new[] { "-I", "-B", "-X", "utf8", launcher, action, "--profile", task.Profile,
            "--sources", task.Sources, "--workspace", task.Workspace, "--wait-for-job" })
            start.ArgumentList.Add(arg);
        if (task.Approval is not null) { start.ArgumentList.Add("--approve-plan"); start.ArgumentList.Add(task.Approval); }
        if (task.BuildId is not null) { start.ArgumentList.Add("--build-id"); start.ArgumentList.Add(task.BuildId); }
        progress?.Invoke(TaskPhase.Preparing);
        using var job = new WindowsJob();
        using var process = Process.Start(start) ?? throw new IOException("Developer worker could not start.");
        try { job.Assign(process); }
        catch
        {
            process.StandardInput.Close();
            if (!process.HasExited) await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        bool install = task.Action == WindowsDeveloperAction.Install;
        bool committing = false;
        var gate = new object();
        using var registration = cancellationToken.Register(() =>
        {
            lock (gate) if (!committing) job.Dispose();
        });
        void OnOutputLine(string line)
        {
            if (install && line == "[gate] request package commit")
            {
                lock (gate)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    // Once we allow pacman to write its database, wait for its real result.
                    // Closing the job remains protection for a terminated owning process.
                    committing = true;
                    process.StandardInput.WriteLine("commit-approved-packages");
                    process.StandardInput.Close();
                }
                return;
            }
            log?.Invoke(line);
        }
        Task<string>? output = null, errors = null;
        try
        {
            await process.StandardInput.WriteLineAsync("owned-windows-job").ConfigureAwait(false);
            if (install) await process.StandardInput.FlushAsync().ConfigureAwait(false);
            else process.StandardInput.Close();
            output = DrainAsync(process.StandardOutput, install || log is not null ? OnOutputLine : null, progress,
                log is null ? 4 * 1024 * 1024 : 32 * 1024);
            errors = DrainAsync(process.StandardError, log, null, 32 * 1024);
            var finished = process.WaitForExitAsync(install ? CancellationToken.None : cancellationToken);
            var first = await Task.WhenAny(finished, output, errors).ConfigureAwait(false);
            if (first.IsFaulted)
            {
                job.Dispose();
                await first.ConfigureAwait(false);
            }
            await finished.ConfigureAwait(false);
            job.Dispose();
            await Task.WhenAll(output, errors).ConfigureAwait(false);
            if (!committing) cancellationToken.ThrowIfCancellationRequested();
            return new(unchecked((uint)process.ExitCode), await output, await errors);
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException(cancellationToken); }
        finally
        {
            job.Dispose();
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (output is not null && errors is not null)
                await Task.WhenAll(output, errors).ConfigureAwait(false);
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader, Action<string>? log,
        Action<TaskPhase>? progress, int limit)
    {
        var buffer = new char[4096];
        var data = new System.Text.StringBuilder();
        var pending = new System.Text.StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            if (data.Length < limit) data.Append(buffer, 0, Math.Min(read, limit - data.Length));
            if (log is null) continue;
            foreach (var character in buffer.AsSpan(0, read))
            {
                if (character == '\n' || pending.Length >= 8192)
                {
                    var line = pending.ToString();
                    pending.Clear();
                    if (line.StartsWith("[phase] compiling", StringComparison.Ordinal)) progress?.Invoke(TaskPhase.Executing);
                    if (line.StartsWith("[phase] installing", StringComparison.Ordinal)) progress?.Invoke(TaskPhase.Executing);
                    if (line.StartsWith("[phase] verifying", StringComparison.Ordinal)) progress?.Invoke(TaskPhase.Verifying);
                    log(line);
                }
                if (character >= ' ' && character != '\u007f') pending.Append(character);
            }
        }
        if (pending.Length != 0) log?.Invoke(pending.ToString());
        return data.ToString();
    }

    private static string OrdinaryFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The required developer payload or tool is missing.");
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Developer executable and payload paths cannot contain links.");
        return path;
    }

    public static void RequireLocal(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
            path[2] is not ('/' or '\\') || path.AsSpan(2).Contains(':') ||
            path.Split('/', '\\').Any(part => part is "." or "..") ||
            new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
            throw new ArgumentException("Choose an absolute path on a fixed local Windows drive.");
    }
}
