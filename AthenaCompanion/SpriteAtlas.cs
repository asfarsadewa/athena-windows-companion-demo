using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AthenaCompanion;

internal enum BehaviorMode
{
    Walk,
    Pose
}

internal enum AthenaInteractionMode
{
    None,
    Voice,
    Text,
    Music
}

internal static class WalkingThoughtText
{
    private static readonly string[] VariantValues = ["Hmm ...", "Ah ...", "...", ". . . ."];

    public static IReadOnlyList<string> Variants => VariantValues;

    public static int SelectIndex(double elapsedSeconds)
    {
        var safeSeconds = Math.Max(0, elapsedSeconds);
        return ((int)(safeSeconds / RotationSeconds)) % VariantValues.Length;
    }

    private const double RotationSeconds = 4.0;
}

internal sealed class AmbientSoundPlayer : IDisposable
{
    private const string AmbientSoundFileName = "on-a-day-like-today.mp3";
    private readonly MediaPlayer? _player;
    private bool _shouldPlay;

    private AmbientSoundPlayer(MediaPlayer? player)
    {
        _player = player;
    }

    public static AmbientSoundPlayer Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "Assets", "Sounds", AmbientSoundFileName);
        if (!File.Exists(path))
        {
            return new AmbientSoundPlayer(null);
        }

        try
        {
            var player = new MediaPlayer
            {
                Volume = 0.16
            };
            var sound = new AmbientSoundPlayer(player);
            player.MediaEnded += sound.OnMediaEnded;
            player.MediaFailed += sound.OnMediaFailed;
            player.Open(new Uri(path, UriKind.Absolute));
            return sound;
        }
        catch
        {
            return new AmbientSoundPlayer(null);
        }
    }

    public void Play()
    {
        if (_player is null)
        {
            return;
        }

        _shouldPlay = true;
        try
        {
            _player.Play();
        }
        catch
        {
            _shouldPlay = false;
        }
    }

    public void Pause()
    {
        _shouldPlay = false;
        try
        {
            _player?.Pause();
        }
        catch
        {
            // Ambient audio should never interrupt Athena's main interaction paths.
        }
    }

    public void Dispose()
    {
        if (_player is null)
        {
            return;
        }

        _shouldPlay = false;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.Close();
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (_player is null || !_shouldPlay)
        {
            return;
        }

        _player.Position = TimeSpan.Zero;
        _player.Play();
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        _shouldPlay = false;
    }
}

internal sealed record AnimationClip(string Name, int StartFrame, int FrameCount, double FramesPerSecond, bool PingPong);

internal sealed class SpriteAtlas
{
    private readonly IReadOnlyList<ImageSource> _frames;

    private SpriteAtlas(IReadOnlyList<ImageSource> frames, SpriteAtlasManifest manifest)
    {
        _frames = frames;
        WalkClip = manifest.CreateWalkClip(_frames.Count);
        PoseClip = manifest.CreatePoseClip(_frames.Count);
    }

    public AnimationClip WalkClip { get; }

    public AnimationClip PoseClip { get; }

    public static SpriteAtlas Load()
    {
        var spriteDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");
        var manifest = SpriteAtlasManifest.Load(spriteDirectory);
        var atlasPath = Path.Combine(spriteDirectory, manifest.Atlas);

        if (File.Exists(atlasPath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(atlasPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            if (bitmap.PixelWidth >= manifest.Columns * manifest.FrameWidth &&
                bitmap.PixelHeight >= manifest.Rows * manifest.FrameHeight)
            {
                return new SpriteAtlas(SliceAtlas(bitmap, manifest), manifest);
            }
        }

        return new SpriteAtlas(BuildFallbackFrames(manifest), manifest);
    }

    public ImageSource GetFrame(AnimationClip clip, double clipSeconds)
    {
        if (_frames.Count == 0)
        {
            throw new InvalidOperationException("Sprite atlas contains no frames.");
        }

        return _frames[AnimationFrameSelector.SelectFrameIndex(clip, clipSeconds, _frames.Count)];
    }

    private static IReadOnlyList<ImageSource> SliceAtlas(BitmapSource bitmap, SpriteAtlasManifest manifest)
    {
        var frames = new List<ImageSource>(manifest.Columns * manifest.Rows);

        for (var row = 0; row < manifest.Rows; row++)
        {
            for (var column = 0; column < manifest.Columns; column++)
            {
                var crop = new CroppedBitmap(bitmap, new Int32Rect(
                    column * manifest.FrameWidth,
                    row * manifest.FrameHeight,
                    manifest.FrameWidth,
                    manifest.FrameHeight));
                crop.Freeze();
                frames.Add(crop);
            }
        }

        return frames;
    }

    private static IReadOnlyList<ImageSource> BuildFallbackFrames(SpriteAtlasManifest manifest)
    {
        var frames = new List<ImageSource>(manifest.Columns * manifest.Rows);
        for (var i = 0; i < manifest.Columns * manifest.Rows; i++)
        {
            frames.Add(RenderFallbackFrame(i));
        }

        return frames;
    }

    private static ImageSource RenderFallbackFrame(int frame)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bob = Math.Sin(frame / 32.0 * Math.PI * 2) * 3;
            var step = Math.Sin(frame / 24.0 * Math.PI * 2) * 8;

            drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, 30, 18, 52)), null, new Point(128, 222), 38, 7);
            drawing.DrawEllipse(Brushes.MediumPurple, null, new Point(128, 82 + bob), 38, 52);
            drawing.DrawEllipse(Brushes.LavenderBlush, null, new Point(128, 72 + bob), 24, 28);
            drawing.DrawGeometry(Brushes.WhiteSmoke, new Pen(Brushes.Gainsboro, 2), BuildRobeGeometry(128, 126 + bob, step));
            drawing.DrawLine(new Pen(Brushes.WhiteSmoke, 9), new Point(106, 144 + bob), new Point(94 - step * 0.25, 198));
            drawing.DrawLine(new Pen(Brushes.WhiteSmoke, 9), new Point(150, 144 + bob), new Point(164 + step * 0.25, 198));
            drawing.DrawLine(new Pen(Brushes.Plum, 5), new Point(111, 66 + bob), new Point(99, 75 + bob));
            drawing.DrawLine(new Pen(Brushes.Plum, 5), new Point(145, 66 + bob), new Point(157, 75 + bob));
        }

        var bitmap = new RenderTargetBitmap(SpriteAtlasManifest.DefaultFrameWidth, SpriteAtlasManifest.DefaultFrameHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Geometry BuildRobeGeometry(double x, double y, double step)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(x, y - 32), isFilled: true, isClosed: true);
            context.LineTo(new Point(x - 46, y + 82), isStroked: true, isSmoothJoin: true);
            context.QuadraticBezierTo(new Point(x - 18 - step * 0.2, y + 96), new Point(x, y + 86), isStroked: true, isSmoothJoin: true);
            context.QuadraticBezierTo(new Point(x + 18 + step * 0.2, y + 96), new Point(x + 46, y + 82), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }
}

internal sealed class SpriteAtlasManifest
{
    public const int DefaultFrameWidth = 256;
    public const int DefaultFrameHeight = 256;

    public string Atlas { get; set; } = "athena-atlas.png";
    public int Columns { get; set; } = 8;
    public int Rows { get; set; } = 4;
    public int FrameWidth { get; set; } = DefaultFrameWidth;
    public int FrameHeight { get; set; } = DefaultFrameHeight;
    public int WalkStartFrame { get; set; }
    public int WalkFrameCount { get; set; } = 24;
    public double WalkFramesPerSecond { get; set; } = 24;
    public int PoseStartFrame { get; set; } = 24;
    public int PoseFrameCount { get; set; } = 8;
    public double PoseFramesPerSecond { get; set; } = 8;

    public static SpriteAtlasManifest Load(string spriteDirectory)
    {
        var path = Path.Combine(spriteDirectory, "athena-atlas.json");
        if (!File.Exists(path))
        {
            return new SpriteAtlasManifest();
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var manifest = JsonSerializer.Deserialize<SpriteAtlasManifest>(File.ReadAllText(path), options) ?? new SpriteAtlasManifest();
        manifest.Normalize();
        return manifest;
    }

    public AnimationClip CreateWalkClip(int frameTotal) =>
        CreateClip("Walk", WalkStartFrame, WalkFrameCount, WalkFramesPerSecond, pingPong: false, frameTotal);

    public AnimationClip CreatePoseClip(int frameTotal) =>
        CreateClip("Pose", PoseStartFrame, PoseFrameCount, PoseFramesPerSecond, pingPong: true, frameTotal);

    private static AnimationClip CreateClip(
        string name,
        int startFrame,
        int frameCount,
        double framesPerSecond,
        bool pingPong,
        int frameTotal)
    {
        startFrame = Math.Clamp(startFrame, 0, Math.Max(0, frameTotal - 1));
        frameCount = Math.Clamp(frameCount, 1, Math.Max(1, frameTotal - startFrame));
        framesPerSecond = Math.Max(1, framesPerSecond);
        return new AnimationClip(name, startFrame, frameCount, framesPerSecond, pingPong);
    }

    private void Normalize()
    {
        Columns = Math.Max(1, Columns);
        Rows = Math.Max(1, Rows);
        FrameWidth = Math.Max(1, FrameWidth);
        FrameHeight = Math.Max(1, FrameHeight);
        WalkFrameCount = Math.Max(1, WalkFrameCount);
        PoseFrameCount = Math.Max(1, PoseFrameCount);
    }
}
