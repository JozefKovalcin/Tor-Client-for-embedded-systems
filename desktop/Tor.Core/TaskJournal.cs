using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tor.Contracts;

namespace Tor.Core;

/// <summary>Single-writer durable progress journal. Only sanitized progress and structured log codes are persisted.</summary>
public sealed class TaskJournal : IDisposable
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private const int MaximumLogBytes = 128 * 1024;
    private readonly string root;
    private readonly FileStream ownership;
    public TaskJournal(string existingPrivateDirectory)
    {
        root = Path.GetFullPath(existingPrivateDirectory);
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Journal requires an existing private regular directory");
        PrivateStorage.RequireOwnerOnly(root);
        ownership = new FileStream(CheckedPath(".task-writer"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
    }
    private string CheckedPath(string name)
    {
        var path = Path.Combine(root, name);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Journal file cannot be a link");
        return path;
    }
    public IReadOnlyList<TaskProgress> Read()
    {
        var path = CheckedPath("tasks.json");
        if (!File.Exists(path)) return Array.Empty<TaskProgress>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Journal size limit exceeded");
        var result = JsonSerializer.Deserialize<TaskProgress[]>(stream, ContractJson.Options)
            ?? throw new InvalidDataException("Empty journal");
        foreach (var row in result) row.Validate();
        if (result.Select(x => x.TaskId).Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new InvalidDataException("Duplicate journal task IDs");
        return result;
    }
    public void Write(IReadOnlyCollection<TaskProgress> rows)
    {
        foreach (var row in rows) row.Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(rows, ContractJson.Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Journal size limit exceeded");
        var destination = CheckedPath("tasks.json");
        var temporary = CheckedPath("journal-" + Guid.NewGuid().ToString("N") + ".next");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        // Retain an interrupted temporary file if publication fails; never remove historical evidence.
        Publish(temporary, destination);
    }

    private string HistoryPath(string id) => CheckedPath(
        "history-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");

    internal bool IsArchived(string id) => File.Exists(HistoryPath(id));

    internal void Archive(TaskProgress row)
    {
        var destination = HistoryPath(row.TaskId);
        if (File.Exists(destination))
        {
            using var existing = File.OpenRead(destination);
            if (existing.Length <= MaximumBytes &&
                JsonSerializer.Deserialize<TaskProgress>(existing, ContractJson.Options) == row) return;
            throw new InvalidDataException("Task history differs from the terminal row");
        }
        var temporary = CheckedPath("history-" + Guid.NewGuid().ToString("N") + ".next");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, row, ContractJson.Options);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, destination);
    }

    internal IReadOnlyList<TaskLog> ReadLogs()
    {
        var path = CheckedPath("task-logs.json");
        if (!File.Exists(path)) return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumLogBytes) throw new InvalidDataException("Structured log size limit exceeded");
        var rows = JsonSerializer.Deserialize<TaskLog[]>(stream, ContractJson.Options)
            ?? throw new InvalidDataException("Empty structured log");
        if (rows.Length > 256 || rows.Any(row => row is null || row.Sequence < 0 || row.TimestampUtc == default ||
            !Enum.IsDefined(row.Event) || row.TerminationSignal is < 1 or > 127 ||
            row.TerminationSignal is not null && row.RawExitCode is not null))
            throw new InvalidDataException("Invalid structured log rows");
        foreach (var row in rows) ContractReference.Require(row.TaskId);
        return rows;
    }

    internal void WriteLogs(IReadOnlyCollection<TaskLog> rows)
    {
        if (rows.Count > 256) throw new InvalidDataException("Structured log capacity exceeded");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(rows, ContractJson.Options);
        if (bytes.Length > MaximumLogBytes) throw new InvalidDataException("Structured log size limit exceeded");
        var destination = CheckedPath("task-logs.json");
        var temporary = CheckedPath("logs-" + Guid.NewGuid().ToString("N") + ".next");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes); stream.Flush(flushToDisk: true);
        }
        Publish(temporary, destination);
    }

    private static void Publish(string temporary, string destination)
    {
        for (int retry = 0; ; retry++)
        {
            try
            {
                File.Move(temporary, destination, overwrite: true);
                return;
            }
            catch (Exception error) when (OperatingSystem.IsWindows() && retry < 3 &&
                error is IOException or UnauthorizedAccessException && (error.HResult & 0xffff) is 5 or 32 or 33)
            {
                // A short-lived Windows file lock can deny the atomic rename. The flushed snapshot
                // stays intact, and permanent denial still reaches the caller with the evidence.
                Thread.Sleep(10 << retry);
            }
        }
    }

    public void Dispose() => ownership.Dispose();
}
