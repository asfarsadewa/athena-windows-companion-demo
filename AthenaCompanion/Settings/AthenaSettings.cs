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

    public static AthenaSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AthenaSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AthenaSettings>(File.ReadAllText(SettingsPath));
            settings ??= new AthenaSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AthenaSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        Normalize();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
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
