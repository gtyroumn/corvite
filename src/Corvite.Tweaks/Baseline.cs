using System.Text.Json;

namespace Corvite.Tweaks;

public sealed class BaselineData
{
    public DateTime Created { get; set; }
    public string Machine { get; set; } = Environment.MachineName;
    public Dictionary<string, string> Snapshots { get; set; } = new();
    public Dictionary<string, double> Values { get; set; } = new();
}

/// <summary>
/// The one-time backup of the ORIGINAL settings (TZ 13). Written before the first change and never overwritten
/// unless the user explicitly creates a new copy.
/// </summary>
public static class BaselineStore
{
    public static string FilePath => Path.Combine(AppPaths.BackupDir, "baseline.json");
    public static bool Exists => File.Exists(FilePath);

    public static BaselineData? Load()
    {
        try { return Exists ? JsonSerializer.Deserialize<BaselineData>(File.ReadAllText(FilePath)) : null; }
        catch { return null; }
    }

    public static void Save(BaselineData d)
    {
        AppPaths.Ensure(AppPaths.BackupDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Delete()
    {
        try { File.Delete(FilePath); } catch { /* ignore */ }
    }
}

/// <summary>System restore point through PowerShell (Checkpoint-Computer). Windows allows one per 24 h by default, so failure is non-fatal.</summary>
public static class RestorePoint
{
    public static TweakResult Create(string description)
    {
        var r = Proc.Run("powershell.exe",
            $"-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType MODIFY_SETTINGS\"", 90000);
        Log.Info($"restore point: code={r.Code} {(r.Err + r.Out).Trim()}");
        return r.Ok ? TweakResult.Success : TweakResult.Fail((r.Err + r.Out).Trim());
    }
}
