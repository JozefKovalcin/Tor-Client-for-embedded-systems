using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Tor.Application;

public static class LinuxDeveloperRunner
{
    public static async Task<JsonElement> RunAsync(string action, string source, string workspace, string cache,
        string run, string approval, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("/usr/bin/python3")
        {
            WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "developer"),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";
        start.Environment["LC_ALL"] = "C.UTF-8";
        foreach (var arg in new[] { "-IB", "-c", "import os, runpy, sys; sys.path.insert(0, os.getcwd()); " +
            "runpy.run_module('scripts.native_linux.manager', run_name='__main__')", action,
            "--source", source, "--workspace", workspace, "--cache", cache, "--run", run, "--approval", approval })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the Linux Developer adapter.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = ReadLogAsync(process.StandardError, log);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25)); }
            catch (TimeoutException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            throw;
        }
        finally { await Task.WhenAll(output, errors); }
        if (process.ExitCode != 0) throw new IOException(await errors);
        using var document = JsonDocument.Parse(await output);
        return document.RootElement.Clone();
    }

    private static async Task<string> ReadLogAsync(StreamReader reader, Action<string> log)
    {
        var buffer = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (buffer.Length < 4096) buffer.AppendLine(line);
            log(line);
        }
        return buffer.ToString();
    }
}
