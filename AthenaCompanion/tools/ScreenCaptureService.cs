using System.IO;
using System.Drawing.Imaging;
using WinForms = System.Windows.Forms;

namespace AthenaCompanion.Tools;

internal sealed class ScreenCaptureService
{
    public byte[] CapturePrimaryScreenPng()
    {
        var screen = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
        using var bitmap = new System.Drawing.Bitmap(screen.Bounds.Width, screen.Bounds.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(screen.Bounds.Location, System.Drawing.Point.Empty, screen.Bounds.Size);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
