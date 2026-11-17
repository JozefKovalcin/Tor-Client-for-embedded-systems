using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Tor.Contracts;

namespace Tor.Core;

public sealed record VerifiedFile(string Ref, string Sha256);

/// <summary>Map opaque references to one trusted root; never accept absolute paths or private-state refs.</summary>
public sealed class VerifiedFileStore
{
    private readonly string root;
    public VerifiedFileStore(string root)
    {
        this.root = Path.GetFullPath(root);
        if (!Directory.Exists(this.root)) throw new DirectoryNotFoundException("Verified input root absent");
        RejectLinks(this.root);
    }
    public string Resolve(string reference)
    {
        ContractReference.Require(reference);
        if (OperatingSystem.IsWindows())
            foreach (var segment in reference.Split('/'))
                if (segment.EndsWith('.') || Regex.IsMatch(segment, @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw new InvalidDataException("Windows device names and trailing-dot aliases are not file references");
        var result = Path.GetFullPath(Path.Combine(root, reference.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, result);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Reference escapes root");
        RejectLinks(result);
        return result;
    }
    public void Verify(VerifiedFile file)
    {
        if (!Regex.IsMatch(file.Sha256, @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Malformed file digest");
        var path = Resolve(file.Ref);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(file.Sha256)))
            throw new InvalidDataException("File digest mismatch");
        RejectLinks(path);
    }
    private static void RejectLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Symbolic links/reparse points are not verified inputs");
            }
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent ?? "";
        }
    }
}
