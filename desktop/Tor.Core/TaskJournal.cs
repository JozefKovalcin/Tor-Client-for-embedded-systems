using System.Text.Json;
using Tor.Contracts;

namespace Tor.Core;

/// <summary>Single-writer durable progress journal. Only sanitized progress and structured log codes are persisted.</summary>
public sealed class TaskJournal : IDisposable
{
    private const int MaximumBytes = 4 * 1024 * 1024;
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
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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
        File.Move(temporary, destination, overwrite: true);
    }
    internal void WriteLogs(IReadOnlyCollection<TaskLog> rows)
    {
        if (rows.Count > 256) throw new InvalidDataException("Structured log capacity exceeded");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(rows, ContractJson.Options);
        if (bytes.Length > 128 * 1024) throw new InvalidDataException("Structured log size limit exceeded");
        var destination = CheckedPath("task-logs.json");
        var temporary = CheckedPath("logs-" + Guid.NewGuid().ToString("N") + ".next");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes); stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, destination, overwrite: true);
    }
    public void Dispose() => ownership.Dispose();
}
