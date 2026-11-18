using System.Security.Cryptography;
using Tor.Core;

namespace Tor.Adapters.Windows;

/// <summary>A verified runtime with bundled pins or an explicitly approved private generation.</summary>
public sealed class PinnedWindowsRuntime
{
    // Existing windows-native/evidence/package-static.json; no source/build pins are altered.
    public static readonly VerifiedFile Executable = new("tor.exe", "3a65024937320fb53a76615273d9cdf7718d9cf90855081dab4b73ae52d2c7f9");
    public static readonly VerifiedFile ThreadLibrary = new("libwinpthread-1.dll", "94334eab25e1a4043a3efdcf88636e722f387fe62fcc57c2533876a019baa04c");
    private VerifiedFileStore Files => new(PackageDirectory);
    private readonly WindowsRuntimeSelection? selection;
    private readonly bool checkSelection;
    private readonly string? generation;
    public string PackageDirectory { get; }
    public VerifiedFile ExecutablePin { get; }
    public IReadOnlyList<VerifiedFile> RuntimePins { get; }
    public PinnedWindowsRuntime(string existingPackageDirectory) : this(existingPackageDirectory, null, null, false, null) { }
    internal PinnedWindowsRuntime(string existingPackageDirectory, RuntimeCandidateFile[]? inputs,
        WindowsRuntimeSelection? selection, bool checkSelection, string? generation)
    {
        this.selection = selection;
        this.checkSelection = checkSelection;
        this.generation = generation;
        PackageDirectory = existingPackageDirectory;
        ExecutablePin = inputs is null ? Executable : new("tor.exe", inputs.Single(input => input.Name == "tor.exe").Sha256);
        RuntimePins = inputs is null ? Array.AsReadOnly(new[] { ThreadLibrary }) :
            Array.AsReadOnly(inputs.Where(input => input.Name != "tor.exe").Select(input => new VerifiedFile(input.Name, input.Sha256)).ToArray());
    }
    public string ExecutablePath => Files.Resolve(ExecutablePin.Ref);
    internal FileStream? Lease() => selection?.Lease(generation, checkSelection);
    internal IReadOnlyList<FileStream> VerifyAndLock()
    {
        var locked = new List<FileStream>();
        try
        {
            var files = Files;
            foreach (var input in new[] { ExecutablePin }.Concat(RuntimePins))
            {
                var stream = new FileStream(files.Resolve(input.Ref), FileMode.Open, FileAccess.Read, FileShare.Read);
                locked.Add(stream);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(input.Sha256)))
                    throw new InvalidDataException("Existing Windows runtime hash differs from its pin");
            }
            return locked;
        }
        catch { foreach (var stream in locked) stream.Dispose(); throw; }
    }
    internal (string ExecutablePath, IReadOnlyList<FileStream> Locks) StageVerifiedFiles(
        IReadOnlyList<FileStream> verifiedSources, string freshDirectory)
    {
        var pins = new[] { ExecutablePin }.Concat(RuntimePins).ToArray();
        if (verifiedSources.Count != pins.Length) throw new InvalidDataException("Runtime staging requires the exact pinned file set");
        PrivateStorage.CreateNewDirectory(freshDirectory);
        var locked = new List<FileStream>();
        try
        {
            for (int index = 0; index < pins.Length; index++)
            {
                var destination = Path.Combine(freshDirectory, pins[index].Ref);
                // Copy only the verified open streams. Never enumerate/copy package
                // neighbors, manifests, .local redirection or other DLLs by pathname.
                verifiedSources[index].Position = 0;
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    verifiedSources[index].CopyTo(output); output.Flush(flushToDisk: true);
                }
                var staged = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
                locked.Add(staged);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(staged), Convert.FromHexString(pins[index].Sha256)))
                    throw new InvalidDataException("Staged runtime hash differs from its pin");
            }
            return (Path.Combine(freshDirectory, ExecutablePin.Ref), locked);
        }
        catch { foreach (var stream in locked) stream.Dispose(); throw; }
    }
}
