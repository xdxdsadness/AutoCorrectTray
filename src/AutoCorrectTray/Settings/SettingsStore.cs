using System.IO;
using System.Text.Json;
using AutoCorrectTray.Logging;

namespace AutoCorrectTray.Settings;

/// <summary>Stores settings in %APPDATA%\AutoCorrectTray\settings.json.</summary>
public sealed class SettingsStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoCorrectTray");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public AppSettings Current { get; private set; }
    public bool IsFirstRun { get; }

    private readonly Logger _log;

    public SettingsStore(Logger log)
    {
        _log = log;
        IsFirstRun = !File.Exists(FilePath);
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts);
                if (settings != null) return settings;
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to read settings, using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts));
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save settings", ex);
        }
    }
}
