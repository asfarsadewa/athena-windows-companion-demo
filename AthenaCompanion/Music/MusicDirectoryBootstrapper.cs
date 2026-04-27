using AthenaCompanion.Settings;

namespace AthenaCompanion.Music;

internal static class MusicDirectoryBootstrapper
{
    public static void Ensure(AthenaSettings settings)
    {
        try
        {
            settings.EnsureMusicDirectoryExists();
        }
        catch
        {
            settings.MusicDirectory = MusicDirectoryDefaults.GetFallback();
            settings.EnsureMusicDirectoryExists();
            settings.Save();
        }
    }
}
