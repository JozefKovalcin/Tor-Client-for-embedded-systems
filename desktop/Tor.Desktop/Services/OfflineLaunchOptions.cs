namespace TorManager.Desktop.Services;

internal sealed record OfflineLaunchOptions(string PackageDirectory, string PrivateDirectory, bool ExplicitPackage)
{
    public static OfflineLaunchOptions Read(string[] args)
    {
        string? package = null;
        string? state = null;
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (++i >= args.Length || !Path.IsPathFullyQualified(args[i]))
                throw new ArgumentException("Use an absolute folder with each launch option.");
            if (option == "--tor-package" && package is null) package = args[i];
            else if (option == "--state-dir" && state is null) state = args[i];
            else throw new ArgumentException("Unknown or repeated launch option.");
        }
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (state is null && !Path.IsPathFullyQualified(localData))
            throw new IOException("Per-user application data is unavailable.");
        var install = Path.GetFullPath(AppContext.BaseDirectory);
        var resolvedPackage = Path.GetFullPath(package ?? Path.Combine(install, "tor-runtime"));
        var resolvedState = Path.GetFullPath(state ?? Path.Combine(localData, "TorManagerOffline"));
        if (Overlaps(install, resolvedState) || Overlaps(resolvedPackage, resolvedState))
            throw new ArgumentException("Private state must be separate from the installation and runtime folders.");
        return new(resolvedPackage, resolvedState, package is not null);
    }

    private static bool Overlaps(string first, string second) =>
        Contains(first, second) || Contains(second, first);

    private static bool Contains(string parent, string child)
    {
        parent = Path.TrimEndingDirectorySeparator(parent);
        child = Path.TrimEndingDirectorySeparator(child);
        var prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
