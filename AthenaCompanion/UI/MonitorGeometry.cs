using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using WinForms = System.Windows.Forms;

namespace AthenaCompanion.UI;

internal readonly record struct TrackBounds(double MinX, double MaxX, double Top);

internal static class MonitorGeometry
{
    public static TrackBounds GetTrackBounds(Window window, double spriteWidth, double spriteHeight, double sidePadding, double bottomOffset)
    {
        var screen = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
        var workingArea = DeviceRectToDip(window, screen.WorkingArea);

        var minX = workingArea.Left + sidePadding;
        var maxX = Math.Max(minX, workingArea.Right - spriteWidth - sidePadding);
        var top = Math.Max(workingArea.Top, workingArea.Bottom - spriteHeight + bottomOffset);
        return new TrackBounds(minX, maxX, top);
    }

    public static Rect DeviceRectToDip(Window window, System.Drawing.Rectangle rectangle)
    {
        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(rectangle.Left, rectangle.Top));
        var bottomRight = transform.Transform(new Point(rectangle.Right, rectangle.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    public static Rect GetPrimaryWorkingAreaDip(Window window)
    {
        var screen = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
        return DeviceRectToDip(window, screen.WorkingArea);
    }
}
