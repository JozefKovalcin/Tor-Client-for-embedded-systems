using System.Text.Json;

namespace TorManager.Desktop.Services;

internal sealed record UiSettings(int SchemaVersion = 1, string Mode = "Run Only", string Theme = "Dark", bool AnimateSimulation = true);

internal sealed class SettingsStore(string path)
{
    public static SettingsStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TorManager", "ui-settings.json"));

    public UiSettings Load(out string notice)
    {
        notice = "Settings loaded locally. Changes are saved only when requested.";
        try
        {
            if (!File.Exists(path)) return new();
            if (new FileInfo(path).Length > 4096) throw new JsonException("Settings too large");
            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path)) ?? throw new JsonException("Empty settings");
            Validate(settings);
            return settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            notice = "Stored UI settings could not be used; defaults are active. Existing file was preserved.";
            return new();
        }
    }

    public void Save(UiSettings settings)
    {
        Validate(settings);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".ui-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, settings);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // Only this method's uniquely named temporary settings file.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(UiSettings settings)
    {
        if (settings.SchemaVersion != 1 || !new[] { "Run Only", "Developer", "Embedded" }.Contains(settings.Mode) ||
            !new[] { "System", "Dark", "Light" }.Contains(settings.Theme))
            throw new JsonException("Unsupported UI settings");
    }
}
