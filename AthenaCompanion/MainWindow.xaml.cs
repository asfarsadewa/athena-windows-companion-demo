using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using AthenaCompanion.Security;
using AthenaCompanion.Settings;
using AthenaCompanion.TextChat;
using AthenaCompanion.Tools;
using AthenaCompanion.UI;
using AthenaCompanion.Voice;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using WinForms = System.Windows.Forms;

namespace AthenaCompanion;

public partial class MainWindow : Window
{
    private const double MinWalkSpeed = 34;
    private const double MaxWalkSpeed = 46;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly Random _random = new();
    private readonly SpriteAtlas _atlas = SpriteAtlas.Load();
    private readonly AthenaSettings _settings = AthenaSettings.Load();
    private readonly OpenAiKeyProvider _keyProvider = new();
    private readonly AthenaVoiceController _voiceController;

    private WinForms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _trayIcon;
    private WinForms.ToolStripMenuItem? _pauseMenuItem;
    private WinForms.ToolStripMenuItem? _clickThroughMenuItem;
    private WinForms.ToolStripMenuItem? _textChatMenuItem;
    private WinForms.ToolStripMenuItem? _voiceStatusMenuItem;
    private WinForms.ToolStripMenuItem? _voiceMenuItem;
    private WinForms.ToolStripMenuItem? _removeApiKeyMenuItem;

    private BehaviorMode _mode = BehaviorMode.Walk;
    private double _lastSeconds;
    private double _modeStartedSeconds;
    private double _nextPoseSeconds;
    private double _poseDurationSeconds;
    private double _trackMinX;
    private double _trackMaxX;
    private double _walkSpeed;
    private double _x;
    private int _direction = 1;
    private AthenaInteractionMode _interactionMode = AthenaInteractionMode.None;
    private bool _clickThrough;
    private string _voiceStatus = "Voice off";
    private string _busyIndicatorLabel = "Thinking";
    private TextChatWindow? _textChatWindow;

    private bool IsInteractionPaused => _interactionMode != AthenaInteractionMode.None;

    public MainWindow()
    {
        _voiceController = new AthenaVoiceController(() => _settings.Voice, ShowGeneratedImage);
        InitializeComponent();

        _timer.Tick += OnTick;
        _voiceController.StatusChanged += OnVoiceStatusChanged;
        _voiceController.Error += OnVoiceError;
        _nextPoseSeconds = RandomRange(8, 18);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        UpdateInteractionVisuals();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureTrayMenu();
        RefreshTrackBounds(resetPosition: true);
        _walkSpeed = RandomRange(MinWalkSpeed, MaxWalkSpeed);
        _clock.Start();
        _lastSeconds = _clock.Elapsed.TotalSeconds;
        _timer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _voiceController.StatusChanged -= OnVoiceStatusChanged;
        _voiceController.Error -= OnVoiceError;
        _ = _voiceController.DisposeAsync();
        CloseTextChatWindow();

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void OnMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _notifyIcon?.ContextMenuStrip?.Show(WinForms.Cursor.Position);
    }

    private void OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_clickThrough)
        {
            return;
        }

        TogglePause();
        e.Handled = true;
    }

    private void OnTextModeBubbleMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_clickThrough)
        {
            return;
        }

        ToggleTextMode();
        e.Handled = true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => RefreshTrackBounds(resetPosition: false));
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _lastSeconds, 0, 0.08);
        _lastSeconds = now;

        if (IsInteractionPaused)
        {
            EnterPoseIfNeeded(now);
        }
        else
        {
            UpdateMovement(now, dt);
        }

        Left = _x;
        SpriteImage.Source = _atlas.GetFrame(CurrentClip, now - _modeStartedSeconds);
        SpriteImage.RenderTransform = _direction < 0
            ? new ScaleTransform(-1, 1)
            : Transform.Identity;
        UpdateBusyIndicatorAnimation(now);
    }

    private void UpdateMovement(double now, double dt)
    {
        if (_mode == BehaviorMode.Pose)
        {
            if (now - _modeStartedSeconds >= _poseDurationSeconds)
            {
                EnterWalk(now);
            }

            return;
        }

        _x += _direction * _walkSpeed * dt;

        if (_x <= _trackMinX)
        {
            _x = _trackMinX;
            _direction = 1;
            EnterPose(now, brief: true);
        }
        else if (_x >= _trackMaxX)
        {
            _x = _trackMaxX;
            _direction = -1;
            EnterPose(now, brief: true);
        }
        else if (now >= _nextPoseSeconds)
        {
            EnterPose(now, brief: false);
        }
    }

    private void EnterPoseIfNeeded(double now)
    {
        if (_mode != BehaviorMode.Pose)
        {
            EnterPose(now, brief: false);
        }
    }

    private void EnterWalk(double now)
    {
        _mode = BehaviorMode.Walk;
        _modeStartedSeconds = now;
        _walkSpeed = RandomRange(MinWalkSpeed, MaxWalkSpeed);
        _nextPoseSeconds = now + RandomRange(8, 18);
    }

    private void EnterPose(double now, bool brief)
    {
        _mode = BehaviorMode.Pose;
        _modeStartedSeconds = now;
        _poseDurationSeconds = brief ? RandomRange(0.75, 1.35) : RandomRange(2.2, 4.8);
    }

    private AnimationClip CurrentClip => _mode == BehaviorMode.Walk ? _atlas.WalkClip : _atlas.PoseClip;

    private void RefreshTrackBounds(bool resetPosition)
    {
        var screen = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
        var workingArea = DeviceRectToDip(screen.WorkingArea);

        _trackMinX = workingArea.Left + 8;
        _trackMaxX = Math.Max(_trackMinX, workingArea.Right - ActualWidth - 8);
        Top = Math.Max(workingArea.Top, workingArea.Bottom - ActualHeight + 3);

        if (resetPosition || _x < _trackMinX || _x > _trackMaxX)
        {
            _x = RandomRange(_trackMinX, _trackMaxX);
        }

        Left = _x;
    }

    private Rect DeviceRectToDip(System.Drawing.Rectangle rectangle)
    {
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(rectangle.Left, rectangle.Top));
        var bottomRight = transform.Transform(new Point(rectangle.Right, rectangle.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void ConfigureTrayMenu()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        _pauseMenuItem = new WinForms.ToolStripMenuItem("Pause walking");
        _pauseMenuItem.Click += (_, _) => TogglePause();

        _clickThroughMenuItem = new WinForms.ToolStripMenuItem("Click-through");
        _clickThroughMenuItem.Click += (_, _) => ToggleClickThrough();

        _textChatMenuItem = new WinForms.ToolStripMenuItem("Text chat");
        _textChatMenuItem.Click += (_, _) => ToggleTextMode();

        _voiceStatusMenuItem = new WinForms.ToolStripMenuItem("Voice off") { Enabled = false };
        _voiceMenuItem = BuildVoiceMenu();

        var configureApiKeyMenuItem = new WinForms.ToolStripMenuItem("OpenAI API Key...");
        configureApiKeyMenuItem.Click += (_, _) =>
        {
            _voiceController.ConfigureApiKey(this);
            UpdateMenuState();
        };

        _removeApiKeyMenuItem = new WinForms.ToolStripMenuItem("Remove saved OpenAI API Key");
        _removeApiKeyMenuItem.Click += (_, _) =>
        {
            _voiceController.RemoveSavedApiKey();
            UpdateMenuState();
        };

        var exitMenuItem = new WinForms.ToolStripMenuItem("Exit");
        exitMenuItem.Click += (_, _) => Close();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(_textChatMenuItem);
        menu.Items.Add(_clickThroughMenuItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_voiceStatusMenuItem);
        menu.Items.Add(_voiceMenuItem);
        menu.Items.Add(configureApiKeyMenuItem);
        menu.Items.Add(_removeApiKeyMenuItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitMenuItem);

        _trayIcon = LoadTrayIcon();
        _notifyIcon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _trayIcon ?? System.Drawing.SystemIcons.Application,
            Text = "Athena Companion",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => TogglePause();

        UpdateMenuState();
    }

    private void TogglePause()
    {
        if (_interactionMode == AthenaInteractionMode.Voice)
        {
            ResumeWalking();
        }
        else
        {
            EnterVoiceMode();
        }

        UpdateMenuState();
    }

    private void ToggleTextMode()
    {
        if (_interactionMode == AthenaInteractionMode.Text)
        {
            _textChatWindow?.Activate();
        }
        else
        {
            EnterTextMode();
        }

        UpdateMenuState();
    }

    private void EnterVoiceMode()
    {
        CloseTextChatWindow();
        _interactionMode = AthenaInteractionMode.Voice;
        EnterPose(_clock.Elapsed.TotalSeconds, brief: false);
        UpdateInteractionVisuals();
        StartVoiceMode();
    }

    private void EnterTextMode()
    {
        StopVoiceMode();
        _interactionMode = AthenaInteractionMode.Text;
        EnterPose(_clock.Elapsed.TotalSeconds, brief: false);
        UpdateBusyIndicatorState("Text ready");
        UpdateInteractionVisuals();
        OpenTextChatWindow();
    }

    private void ResumeWalking()
    {
        StopVoiceMode();
        CloseTextChatWindow();
        _interactionMode = AthenaInteractionMode.None;
        EnterWalk(_clock.Elapsed.TotalSeconds);
        UpdateBusyIndicatorState("Ready");
        UpdateInteractionVisuals();
        UpdateMenuState();
    }

    private void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        ApplyClickThroughStyle(_clickThrough);
        UpdateInteractionVisuals();
        UpdateMenuState();
    }

    private WinForms.ToolStripMenuItem BuildVoiceMenu()
    {
        var menu = new WinForms.ToolStripMenuItem("Voice");
        foreach (var voice in RealtimeVoiceOptions.BuiltIn)
        {
            var item = new WinForms.ToolStripMenuItem(ToTitleCase(voice))
            {
                Tag = voice,
                CheckOnClick = false
            };
            item.Click += (_, _) => ChangeVoice(voice);
            menu.DropDownItems.Add(item);
        }

        return menu;
    }

    private async void ChangeVoice(string voice)
    {
        if (!RealtimeVoiceOptions.IsSupported(voice) ||
            string.Equals(_settings.Voice, voice, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Voice = voice;
        _settings.Save();

        if (_interactionMode == AthenaInteractionMode.Voice)
        {
            await _voiceController.StopAsync();
            await _voiceController.StartAsync(this);
        }

        UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        if (_pauseMenuItem is not null)
        {
            _pauseMenuItem.Checked = _interactionMode == AthenaInteractionMode.Voice;
            _pauseMenuItem.Text = _interactionMode == AthenaInteractionMode.Voice ? "Resume walking" : "Pause for voice";
        }

        if (_textChatMenuItem is not null)
        {
            _textChatMenuItem.Checked = _interactionMode == AthenaInteractionMode.Text;
            _textChatMenuItem.Text = _interactionMode == AthenaInteractionMode.Text ? "Focus text chat" : "Text chat";
        }

        if (_clickThroughMenuItem is not null)
        {
            _clickThroughMenuItem.Checked = _clickThrough;
        }

        if (_voiceStatusMenuItem is not null)
        {
            var keyStatus = _voiceController.GetKeyStatus();
            _voiceStatusMenuItem.Text = $"Voice: {_voiceStatus}, {_settings.Voice} ({keyStatus})";
        }

        if (_removeApiKeyMenuItem is not null)
        {
            _removeApiKeyMenuItem.Enabled = _voiceController.GetKeyStatus() == "Credential Manager";
        }

        if (_voiceMenuItem is not null)
        {
            foreach (WinForms.ToolStripItem item in _voiceMenuItem.DropDownItems)
            {
                if (item is WinForms.ToolStripMenuItem voiceItem && voiceItem.Tag is string voice)
                {
                    voiceItem.Checked = string.Equals(voice, _settings.Voice, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    private async void StartVoiceMode()
    {
        await _voiceController.StartAsync(this);
    }

    private async void StopVoiceMode()
    {
        await _voiceController.StopAsync();
    }

    private void OnVoiceStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() =>
        {
            _voiceStatus = status;
            if (_interactionMode != AthenaInteractionMode.Text)
            {
                UpdateBusyIndicatorState(status);
            }

            UpdateMenuState();
        });
    }

    private void OnVoiceError(object? sender, string error)
    {
        Dispatcher.Invoke(() =>
        {
            _voiceStatus = "Voice error";
            if (_interactionMode == AthenaInteractionMode.Voice)
            {
                UpdateBusyIndicatorState(_voiceStatus);
            }

            UpdateMenuState();
            _notifyIcon?.ShowBalloonTip(4000, "Athena Voice", error, WinForms.ToolTipIcon.Warning);
        });
    }

    private void ShowGeneratedImage(string imagePath)
    {
        var lightbox = new ImageLightboxWindow(imagePath)
        {
            Owner = this
        };

        lightbox.Show();
        lightbox.Activate();
    }

    private void OpenTextChatWindow()
    {
        if (_textChatWindow is not null)
        {
            _textChatWindow.Activate();
            return;
        }

        var apiKey = GetOrPromptOpenAiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ResumeWalking();
            return;
        }

        var tools = new AthenaToolExecutor(
            () => apiKey,
            ShowGeneratedImage,
            status => Dispatcher.Invoke(() => UpdateBusyIndicatorState(status)));
        var session = new AthenaTextChatSession(apiKey, tools);
        var chatWindow = new TextChatWindow(session)
        {
            Owner = this
        };

        chatWindow.StatusChanged += OnTextChatStatusChanged;
        chatWindow.Closed += OnTextChatClosed;
        PositionTextChatWindow(chatWindow);
        _textChatWindow = chatWindow;
        chatWindow.Show();
        chatWindow.Activate();
    }

    private void CloseTextChatWindow()
    {
        var chatWindow = _textChatWindow;
        if (chatWindow is null)
        {
            return;
        }

        _textChatWindow = null;
        chatWindow.StatusChanged -= OnTextChatStatusChanged;
        chatWindow.Closed -= OnTextChatClosed;
        chatWindow.Close();
    }

    private string? GetOrPromptOpenAiKey()
    {
        var lookup = _keyProvider.TryGetApiKey();
        if (!string.IsNullOrWhiteSpace(lookup.ApiKey))
        {
            return lookup.ApiKey;
        }

        var dialog = new Settings.ApiKeySetupWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        _keyProvider.SaveApiKey(dialog.ApiKey);
        UpdateMenuState();
        return dialog.ApiKey;
    }

    private void PositionTextChatWindow(Window chatWindow)
    {
        var screen = WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
        var workingArea = DeviceRectToDip(screen.WorkingArea);
        var left = Left + ActualWidth + 8;
        if (left + chatWindow.Width > workingArea.Right)
        {
            left = Left - chatWindow.Width - 8;
        }

        chatWindow.Left = Math.Clamp(left, workingArea.Left + 8, workingArea.Right - chatWindow.Width - 8);
        chatWindow.Top = Math.Clamp(Top - chatWindow.Height + ActualHeight, workingArea.Top + 8, workingArea.Bottom - chatWindow.Height - 8);
    }

    private void OnTextChatStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateBusyIndicatorState(status);
            UpdateMenuState();
        });
    }

    private void OnTextChatClosed(object? sender, EventArgs e)
    {
        if (sender is TextChatWindow chatWindow)
        {
            chatWindow.StatusChanged -= OnTextChatStatusChanged;
            chatWindow.Closed -= OnTextChatClosed;
        }

        _textChatWindow = null;
        if (_interactionMode == AthenaInteractionMode.Text)
        {
            ResumeWalking();
        }
    }

    private void UpdateBusyIndicatorState(string status)
    {
        _busyIndicatorLabel = status switch
        {
            "Connecting..." => "Connecting",
            "Thinking" => "Thinking",
            "Using tool" => "Thinking",
            "Looking at screen" => "Looking",
            "Creating image" => "Drawing",
            "Text ready" => _interactionMode == AthenaInteractionMode.Text ? "Chat" : string.Empty,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(_busyIndicatorLabel))
        {
            VoiceBusyIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        VoiceBusyText.Text = _busyIndicatorLabel;
        VoiceBusyIndicator.Visibility = Visibility.Visible;
    }

    private void UpdateInteractionVisuals()
    {
        TextModeBubble.Visibility = !_clickThrough && _interactionMode == AthenaInteractionMode.None
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateBusyIndicatorAnimation(double now)
    {
        if (VoiceBusyIndicator.Visibility != Visibility.Visible)
        {
            return;
        }

        var dotCount = (int)(now * 2.6) % 4;
        VoiceBusyDots.Text = new string('.', dotCount).PadRight(3);
        VoiceBusyIndicator.Opacity = 0.82 + Math.Sin(now * 5.0) * 0.12;
    }

    private static string ToTitleCase(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];

    private static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
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

    private void ApplyClickThroughStyle(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle);
        var next = enabled
            ? style | WsExTransparent
            : style & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, next);
    }

    private double RandomRange(double min, double max) => min + _random.NextDouble() * (max - min);

    private const int GwlExStyle = -20;
    private const nint WsExTransparent = 0x00000020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static nint GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    private static nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
}

internal enum BehaviorMode
{
    Walk,
    Pose
}

internal enum AthenaInteractionMode
{
    None,
    Voice,
    Text
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

        var count = Math.Min(clip.FrameCount, Math.Max(1, _frames.Count - clip.StartFrame));
        var localFrame = (int)Math.Floor(Math.Max(0, clipSeconds) * clip.FramesPerSecond);

        if (clip.PingPong && count > 1)
        {
            var period = count * 2 - 2;
            localFrame %= period;
            if (localFrame >= count)
            {
                localFrame = period - localFrame;
            }
        }
        else
        {
            localFrame %= count;
        }

        var frameIndex = Math.Clamp(clip.StartFrame + localFrame, 0, _frames.Count - 1);
        return _frames[frameIndex];
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
