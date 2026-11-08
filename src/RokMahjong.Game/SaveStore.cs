using System.Text.Json;
using System.Text.Json.Serialization;
using RokMahjong.Core;

namespace RokMahjong.Game;

public sealed class AppSettings
{
    public bool SoundEnabled { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool HighlightFreeTiles { get; set; } = true;
}

public sealed class ProgressState
{
    public HashSet<int> CompletedLevels { get; set; } = new();
    public int BestScore { get; set; }
}

public sealed class SaveStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SaveStore(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RokMahjong");
    }

    public string Root { get; }
    private string SettingsPath => Path.Combine(Root, "settings.json");
    private string ProgressPath => Path.Combine(Root, "progress.json");
    private string SessionPath => Path.Combine(Root, "session.json");

    public AppSettings LoadSettings() => Load<AppSettings>(SettingsPath) ?? new AppSettings();
    public void SaveSettings(AppSettings settings) => Save(SettingsPath, settings);
    public ProgressState LoadProgress() => Load<ProgressState>(ProgressPath) ?? new ProgressState();
    public void SaveProgress(ProgressState progress) => Save(ProgressPath, progress);

    public SessionSnapshot? LoadSession() => Load<SessionSnapshot>(SessionPath);

    public void SaveSession(GameSession session) => Save(SessionPath, session.Snapshot());
    public void ClearSession()
    {
        try { if (File.Exists(SessionPath)) File.Delete(SessionPath); }
        catch { }
    }

    private static T? Load<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch
        {
            return default;
        }
    }

    private static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // A play session must not crash because the save directory is unavailable.
        }
    }
}
