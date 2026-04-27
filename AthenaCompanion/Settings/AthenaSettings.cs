using System.IO;
using System.Text.Json;

namespace AthenaCompanion.Settings;

internal sealed class AthenaSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AthenaCompanion");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public string Voice { get; set; } = RealtimeVoiceOptions.Default;

    public static AthenaSettings Load() => LoadFromPath(SettingsPath);

    internal static AthenaSettings LoadFromPath(string path)
    {
        if (!File.Exists(path))
        {
            return new AthenaSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AthenaSettings>(File.ReadAllText(path));
            settings ??= new AthenaSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AthenaSettings();
        }
    }

    public void Save() => SaveToPath(SettingsPath);

    internal void SaveToPath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Normalize();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void Normalize()
    {
        if (!RealtimeVoiceOptions.IsSupported(Voice))
        {
            Voice = RealtimeVoiceOptions.Default;
        }
    }
}

internal static class RealtimeVoiceOptions
{
    public const string Default = "alloy";

    public static readonly IReadOnlyList<string> BuiltIn =
    [
        "marin",
        "cedar",
        "coral",
        "shimmer",
        "verse",
        "sage",
        "alloy",
        "ash",
        "ballad",
        "echo"
    ];

    public static bool IsSupported(string? voice) =>
        !string.IsNullOrWhiteSpace(voice) &&
        BuiltIn.Contains(voice, StringComparer.OrdinalIgnoreCase);
}
