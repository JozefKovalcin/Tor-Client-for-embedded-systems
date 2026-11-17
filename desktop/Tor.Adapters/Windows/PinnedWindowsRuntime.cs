using System.Security.Cryptography;
using Tor.Core;

namespace Tor.Adapters.Windows;

/// <summary>The existing reviewed static package only; a version change requires a separate provenance review.</summary>
public sealed class PinnedWindowsRuntime
{
    // Existing windows-native/evidence/package-static.json; no source/build pins are altered.
    public static readonly VerifiedFile Executable = new("tor.exe", "3a65024937320fb53a76615273d9cdf7718d9cf90855081dab4b73ae52d2c7f9");
    public static readonly VerifiedFile ThreadLibrary = new("libwinpthread-1.dll", "94334eab25e1a4043a3efdcf88636e722f387fe62fcc57c2533876a019baa04c");
    private readonly VerifiedFileStore files;
    public PinnedWindowsRuntime(string existingPackageDirectory) => files = new(existingPackageDirectory);
    public string ExecutablePath => files.Resolve(Executable.Ref);
    internal IReadOnlyList<FileStream> VerifyAndLock()
    {
        var locked = new List<FileStream>();
        try
        {
            foreach (var input in new[] { Executable, ThreadLibrary })
            {
                var stream = new FileStream(files.Resolve(input.Ref), FileMode.Open, FileAccess.Read, FileShare.Read);
                locked.Add(stream);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(input.Sha256)))
                    throw new InvalidDataException("Existing Windows runtime hash differs from reviewed static package");
            }
            return locked;
        }
        catch { foreach (var stream in locked) stream.Dispose(); throw; }
    }
}
