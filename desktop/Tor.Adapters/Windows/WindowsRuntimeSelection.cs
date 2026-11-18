using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tor.Core;

namespace Tor.Adapters.Windows;

public sealed record RuntimeCandidateFile(string Name, string Sha256);
public sealed record RuntimeCandidate(string Id, string Version, string Profile, string PackagePath,
    string ReportSha256, string SourceSha256, RuntimeCandidateFile[] Files);

/// <summary>Per-user runtime approvals and a cross-process lease shared with managed sessions.</summary>
public sealed class WindowsRuntimeSelection(string root)
{
    private sealed record Selection(int SchemaVersion, Approval? Current, Approval? Previous);
    private sealed record Approval(string Generation, string Id, string Version, string Profile,
        string ReportSha256, string SourceSha256, RuntimeCandidateFile[] Files);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex FileName = new(@"^(?:tor\.exe|[a-z0-9][a-z0-9_.-]{0,95}\.dll)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Digest = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static WindowsRuntimeSelection ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TorManagerRuntimeSelection"));

    public PinnedWindowsRuntime ReadRuntime(string bundled, bool useApproved = true)
    {
        var chosen = useApproved && Directory.Exists(root) ? Read().Current : null;
        return ToRuntime(chosen, bundled, useApproved);
    }

    internal FileStream Lease(string? expectedGeneration, bool checkSelection)
    {
        var lease = Acquire(exclusive: false);
        try
        {
            if (checkSelection && Read().Current?.Generation != expectedGeneration)
                throw new IOException("The approved runtime changed. Restart Tor Manager before opening a session.");
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    public void Activate(RuntimeCandidate candidate)
    {
        using var exclusive = Acquire(exclusive: true);
        var previous = Read();
        Validate(candidate.Id, candidate.Version, candidate.Profile, candidate.ReportSha256, candidate.SourceSha256, candidate.Files);
        var approved = new Approval(Guid.NewGuid().ToString("N"), candidate.Id, candidate.Version, candidate.Profile,
            candidate.ReportSha256, candidate.SourceSha256, candidate.Files.ToArray());
        PrivateStorage.RequireOwnerOnly(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(candidate.PackagePath)!)!)!);
        var source = new VerifiedFileStore(candidate.PackagePath);
        var destination = RuntimePath(approved);
        PrivateStorage.CreateNewDirectory(destination);
        Copy(source, destination, "package-report.json", approved.ReportSha256);
        Copy(source, destination, "source-manifest.json", approved.SourceSha256);
        foreach (var file in approved.Files) Copy(source, destination, file.Name, file.Sha256);
        Verify(approved);
        Write(new(1, approved, previous.Current));
    }

    public void Rollback(string bundled)
    {
        using var exclusive = Acquire(exclusive: true);
        var state = Read();
        if (state.Current is null) throw new InvalidOperationException("There is no active developer build to roll back.");
        if (state.Previous is not null) Verify(state.Previous);
        else
        {
            var original = new PinnedWindowsRuntime(bundled);
            foreach (var file in original.VerifyAndLock()) file.Dispose();
        }
        Approval? stillVerified = state.Current;
        try { Verify(state.Current); }
        catch (IOException) { stillVerified = null; }
        catch (InvalidDataException) { stillVerified = null; }
        catch (UnauthorizedAccessException) { stillVerified = null; }
        Write(new(1, state.Previous, stillVerified));
    }

    private static void Copy(VerifiedFileStore source, string destination, string name, string hash)
    {
        using var input = new FileStream(source.Resolve(name), FileMode.Open, FileAccess.Read, FileShare.Read);
        var target = Path.Combine(destination, name);
        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { input.CopyTo(output); output.Flush(flushToDisk: true); }
        CheckHash(new VerifiedFileStore(destination), name, hash);
    }

    private static void CheckHash(VerifiedFileStore source, string name, string expected)
    {
        using var stream = new FileStream(source.Resolve(name), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(expected)))
            throw new InvalidDataException("Runtime approval no longer matches the file hash.");
    }

    private void Verify(Approval approval)
    {
        var path = RuntimePath(approval);
        PrivateStorage.RequireOwnerOnly(path);
        var source = new VerifiedFileStore(path);
        CheckHash(source, "package-report.json", approval.ReportSha256);
        CheckHash(source, "source-manifest.json", approval.SourceSha256);
        foreach (var file in approval.Files) CheckHash(source, file.Name, file.Sha256);
    }

    private PinnedWindowsRuntime ToRuntime(Approval? approved, string bundled, bool checkSelection)
    {
        if (approved is null) return new(bundled, null, this, checkSelection, null);
        Verify(approved);
        return new(RuntimePath(approved), approved.Files, this, checkSelection, approved.Generation);
    }

    private FileStream Acquire(bool exclusive)
    {
        if (!Directory.Exists(root))
        {
            try { PrivateStorage.CreateNewDirectory(root); }
            catch (IOException) when (Directory.Exists(root)) { }
        }
        PrivateStorage.RequireOwnerOnly(root);
        var path = new VerifiedFileStore(root).Resolve("session.lease");
        if (!File.Exists(path))
        {
            try { using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); }
            catch (IOException) when (File.Exists(path)) { }
        }
        try { return new FileStream(path, FileMode.Open, FileAccess.Read, exclusive ? FileShare.None : FileShare.Read); }
        catch (IOException error) { throw new IOException("A managed Tor session or runtime selection operation currently owns the runtime.", error); }
    }

    private string RuntimePath(Approval approval) => Path.Combine(root, "approved-" + approval.Generation);

    private Selection Read()
    {
        PrivateStorage.RequireOwnerOnly(root);
        var path = new VerifiedFileStore(root).Resolve("selection.json");
        if (!File.Exists(path)) return new(1, null, null);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 64_000) throw new InvalidDataException("Runtime selection metadata is too large.");
        var state = JsonSerializer.Deserialize<Selection>(stream, Json);
        if (state?.SchemaVersion != 1) throw new InvalidDataException("Runtime selection version is unavailable.");
        foreach (var value in new[] { state.Current, state.Previous })
        {
            if (value is null) continue;
            if (!Guid.TryParseExact(value.Generation, "N", out _)) throw new InvalidDataException("Invalid runtime generation.");
            Validate(value.Id, value.Version, value.Profile, value.ReportSha256, value.SourceSha256, value.Files);
        }
        return state;
    }

    private void Write(Selection state)
    {
        var pending = Path.Combine(root, "pending-" + Guid.NewGuid().ToString("N"));
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, state, Json); stream.Flush(flushToDisk: true); }
        File.Move(pending, new VerifiedFileStore(root).Resolve("selection.json"), overwrite: true);
    }

    private static void Validate(string id, string version, string profile, string reportHash, string sourceHash,
        RuntimeCandidateFile[] files)
    {
        if (id is null || !id.StartsWith("run-", StringComparison.Ordinal) || id.Length > 28 ||
            !Version.TryParse(version, out _) || profile is not ("optimized-static" or "optimized-shared") ||
            reportHash is null || sourceHash is null || !Digest.IsMatch(reportHash) || !Digest.IsMatch(sourceHash) ||
            files is null || files.Length is < 1 or > 64 || files.Any(file => file is null || file.Name is null ||
                file.Sha256 is null || !FileName.IsMatch(file.Name) || !Digest.IsMatch(file.Sha256)) ||
            files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length ||
            !files.Any(file => file.Name == "tor.exe"))
            throw new InvalidDataException("Invalid runtime approval metadata.");
    }
}
