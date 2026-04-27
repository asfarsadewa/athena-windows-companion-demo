using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AthenaCompanion.UI;

internal static class IconLoader
{
    public static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "athena.ico");
            if (File.Exists(iconPath))
            {
                return new System.Drawing.Icon(iconPath);
            }

            var processPath = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(processPath)
                ? null
                : System.Drawing.Icon.ExtractAssociatedIcon(processPath);
        }
        catch
        {
            return null;
        }
    }

    public static ImageSource? LoadWindowIcon()
    {
        using var icon = LoadTrayIcon();
        if (icon is null)
        {
            return null;
        }

        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
