using System.IO;
using System.Text.Json;

namespace Corvite.App.Services;

/// <summary>Persisted user choices (TZ 12): interface language and device type. Stored in %ProgramData%\Corvite\config.json.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "ru";

    /// <summary>"pc", "laptop" or null until the user has confirmed the device type once.</summary>
    public string? Platform { get; set; }

    /// <summary>Memory chip chosen on the RAM tab (id from ram_chips.json), null = averaged table.</summary>
    public string? RamChip { get; set; }

    /// <summary>Ask GitHub once at start whether a newer release exists. Set to false in config.json to switch it off.</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>Download the newest game list for the advisor from GitHub at start (a few KB, no data sent). Set to false to switch it off.</summary>
    public bool UpdateGameList { get; set; } = true;

    /// <summary>Ids of Overview hints the player has closed.</summary>
    public List<string> HiddenHints { get; set; } = new();

    /// <summary>Values of the protection settings (rollback on overheat, threshold, restore point, backup) by tweak id.</summary>
    public Dictionary<string, double> Guard { get; set; } = new();
}

public static class SettingsStore
{
    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Corvite");
    private static string FilePath => Path.Combine(Dir, "config.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    if (s.Language is not ("ru" or "uk" or "en")) s.Language = "ru";
                    if (s.Platform is not (null or "pc" or "laptop")) s.Platform = null;
                    return s;
                }
            }
        }
        catch { /* corrupt or unreadable config: start with defaults */ }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}
