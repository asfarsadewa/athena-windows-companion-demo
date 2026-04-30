using System.Diagnostics;
using AthenaCompanion.Music;
using AthenaCompanion.Security;
using AthenaCompanion.Settings;
using AthenaCompanion.TextChat;
using AthenaCompanion.Tools;
using AthenaCompanion.UI;
using AthenaCompanion.UI.Interop;
using AthenaCompanion.Voice;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace AthenaCompanion;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly AthenaSettings _settings = AthenaSettings.Load();
    private readonly OpenAiKeyProvider _keyProvider = new();
    private readonly AthenaVoiceController _voiceController;
    private readonly AmbientSoundPlayer _ambientSoundPlayer;
    private readonly WalkAnimationController _walk = new(SpriteAtlas.Load());
    private readonly TrayMenuController _tray;

    private double _lastSeconds;
    private int _thoughtVariantIndex = -1;
    private AthenaInteractionMode _interactionMode = AthenaInteractionMode.None;
    private bool _clickThrough;
    private string _voiceStatus = "Voice off";
    private string _busyIndicatorLabel = "Thinking";
    private TextChatWindow? _textChatWindow;
    private MusicPlayerWindow? _musicPlayerWindow;
    private OnboardingWindow? _onboardingWindow;

    private bool IsInteractionPaused => _interactionMode != AthenaInteractionMode.None;

    public MainWindow()
    {
        MusicDirectoryBootstrapper.Ensure(_settings);
        _voiceController = new AthenaVoiceController(() => _settings.Voice, ShowGeneratedImage, OpenMusicPlayerFromTool);
        _tray = new TrayMenuController(new TrayMenuStateProvider(
            () => _interactionMode,
            () => _clickThrough,
            () => _voiceStatus,
            () => _settings.Voice,
            () => _voiceController.GetKeyStatus()));
        _tray.PauseRequested += (_, _) => TogglePause();
        _tray.ClickThroughToggled += (_, _) => ToggleClickThrough();
        _tray.TextModeRequested += (_, _) => ToggleTextMode();
        _tray.MusicRequested += (_, _) => ToggleMusicMode();
        _tray.VoiceChanged += (_, voice) => ChangeVoice(voice);
        _tray.ConfigureApiKeyRequested += OnConfigureApiKeyRequested;
        _tray.RemoveApiKeyRequested += OnRemoveApiKeyRequested;
        _tray.OnboardingRequested += OnOnboardingRequested;
        _tray.ExitRequested += (_, _) => Close();

        InitializeComponent();
        Icon = IconLoader.LoadWindowIcon();
        _ambientSoundPlayer = AmbientSoundPlayer.Load(AppContext.BaseDirectory);

        _timer.Tick += OnTick;
        _voiceController.StatusChanged += OnVoiceStatusChanged;
        _voiceController.Error += OnVoiceError;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        UpdateInteractionVisuals();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _tray.Initialize();
        RefreshTrackBounds(resetPosition: true);
        _clock.Start();
        _lastSeconds = _clock.Elapsed.TotalSeconds;
        UpdateAmbientSoundState();
        _timer.Start();
        ShowFirstRunOnboardingIfNeeded();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _voiceController.StatusChanged -= OnVoiceStatusChanged;
        _voiceController.Error -= OnVoiceError;
        _ = _voiceController.DisposeAsync();
        CloseTextChatWindow();
        CloseMusicPlayerWindow();
        _ambientSoundPlayer.Dispose();
        _tray.Dispose();
    }

    private void OnMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _tray.ShowContextMenu(WinForms.Cursor.Position);
    }

    private void OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_clickThrough)
        {
            return;
        }

        if (IsModeBubbleClick(e.OriginalSource))
        {
            e.Handled = true;
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

    private void OnVoiceModeBubbleMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_clickThrough)
        {
            return;
        }

        TogglePause();
        e.Handled = true;
    }

    private bool IsModeBubbleClick(object? source) =>
        IsDescendantOf(TextModeBubble, source) ||
        IsDescendantOf(VoiceModeBubble, source);

    private static bool IsDescendantOf(DependencyObject parent, object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, parent))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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

        _walk.Tick(now, dt, IsInteractionPaused);

        Left = _walk.X;
        SpriteImage.Source = _walk.CurrentFrame(now);
        SpriteImage.RenderTransform = _walk.Direction < 0
            ? new ScaleTransform(-1, 1)
            : Transform.Identity;
        UpdateWalkingThoughtText(now);
        UpdateBusyIndicatorAnimation(now);
    }

    private void RefreshTrackBounds(bool resetPosition)
    {
        var bounds = MonitorGeometry.GetTrackBounds(this, ActualWidth, ActualHeight, sidePadding: 8, bottomOffset: 3);
        Top = bounds.Top;
        _walk.SetTrackBounds(bounds.MinX, bounds.MaxX, resetPosition);
        Left = _walk.X;
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

        _tray.Refresh();
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

        _tray.Refresh();
    }

    private void ToggleMusicMode()
    {
        if (_interactionMode == AthenaInteractionMode.Music)
        {
            _musicPlayerWindow?.Activate();
        }
        else
        {
            EnterMusicMode(MusicPlayerRequest.OpenLibrary);
        }

        _tray.Refresh();
    }

    private void EnterVoiceMode()
    {
        CloseTextChatWindow();
        CloseMusicPlayerWindow();
        _interactionMode = AthenaInteractionMode.Voice;
        _walk.EnterPose(_clock.Elapsed.TotalSeconds, brief: false);
        UpdateInteractionVisuals();
        UpdateAmbientSoundState();
        StartVoiceMode();
    }

    private void EnterTextMode()
    {
        StopVoiceMode();
        CloseMusicPlayerWindow();
        _interactionMode = AthenaInteractionMode.Text;
        _walk.EnterPose(_clock.Elapsed.TotalSeconds, brief: false);
        UpdateBusyIndicatorState("Text ready");
        UpdateInteractionVisuals();
        UpdateAmbientSoundState();
        OpenTextChatWindow();
    }

    private void EnterMusicMode(MusicPlayerRequest request)
    {
        StopVoiceMode();
        CloseTextChatWindow();
        _interactionMode = AthenaInteractionMode.Music;
        _walk.EnterPose(_clock.Elapsed.TotalSeconds, brief: false);
        UpdateBusyIndicatorState("Music mode");
        UpdateInteractionVisuals();
        UpdateAmbientSoundState();
        OpenMusicPlayerWindow(request);
        _tray.Refresh();
    }

    private void ResumeWalking()
    {
        StopVoiceMode();
        CloseTextChatWindow();
        CloseMusicPlayerWindow();
        _interactionMode = AthenaInteractionMode.None;
        _walk.EnterWalk(_clock.Elapsed.TotalSeconds);
        UpdateBusyIndicatorState("Ready");
        UpdateInteractionVisuals();
        UpdateAmbientSoundState();
        _tray.Refresh();
    }

    private void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        ClickThroughInterop.Apply(this, _clickThrough);
        UpdateInteractionVisuals();
        _tray.Refresh();
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

        _tray.Refresh();
    }

    private void OnConfigureApiKeyRequested(object? sender, EventArgs e)
    {
        _voiceController.ConfigureApiKey(this);
        _tray.Refresh();
    }

    private void OnRemoveApiKeyRequested(object? sender, EventArgs e)
    {
        _voiceController.RemoveSavedApiKey();
        _tray.Refresh();
    }

    private void OnOnboardingRequested(object? sender, EventArgs e) => ShowOnboarding(markCompleted: false);

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
            if (_interactionMode is not AthenaInteractionMode.Text and not AthenaInteractionMode.Music)
            {
                UpdateBusyIndicatorState(status);
            }

            _tray.Refresh();
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

            _tray.Refresh();
            _tray.ShowBalloonTip("Athena Voice", error);
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
            status => Dispatcher.Invoke(() => UpdateBusyIndicatorState(status)),
            OpenMusicPlayerFromTool);
        var session = new AthenaTextChatSession(apiKey, tools);
        var chatWindow = new TextChatWindow(session)
        {
            Owner = this
        };

        chatWindow.StatusChanged += OnTextChatStatusChanged;
        chatWindow.Closed += OnTextChatClosed;
        PositionChildWindow(chatWindow);
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

    private void OpenMusicPlayerFromTool(MusicPlayerRequest request) => EnterMusicMode(request);

    private void OpenMusicPlayerWindow(MusicPlayerRequest request)
    {
        if (_musicPlayerWindow is not null)
        {
            _musicPlayerWindow.ApplyRequest(request);
            _musicPlayerWindow.Activate();
            return;
        }

        var musicWindow = new MusicPlayerWindow(_settings.MusicDirectory)
        {
            Owner = this
        };

        musicWindow.Closed += OnMusicPlayerClosed;
        PositionChildWindow(musicWindow);
        _musicPlayerWindow = musicWindow;
        musicWindow.Show();
        musicWindow.ApplyRequest(request);
        musicWindow.Activate();
    }

    private void CloseMusicPlayerWindow()
    {
        var musicWindow = _musicPlayerWindow;
        if (musicWindow is null)
        {
            return;
        }

        _musicPlayerWindow = null;
        musicWindow.Closed -= OnMusicPlayerClosed;
        musicWindow.Close();
    }

    private string? GetOrPromptOpenAiKey()
    {
        var lookup = _keyProvider.TryGetApiKey();
        if (!string.IsNullOrWhiteSpace(lookup.ApiKey))
        {
            return lookup.ApiKey;
        }

        var dialog = new ApiKeySetupWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        _keyProvider.SaveApiKey(dialog.ApiKey);
        _tray.Refresh();
        return dialog.ApiKey;
    }

    private void ShowFirstRunOnboardingIfNeeded()
    {
        if (_settings.HasCompletedOnboarding)
        {
            return;
        }

        ShowOnboarding(markCompleted: true);
    }

    private void ShowOnboarding(bool markCompleted)
    {
        if (_onboardingWindow is not null)
        {
            _onboardingWindow.Activate();
            return;
        }

        var onboardingWindow = new OnboardingWindow(_settings.MusicDirectory, owner =>
        {
            _voiceController.ConfigureApiKey(owner);
            _tray.Refresh();
        })
        {
            Owner = this
        };

        _onboardingWindow = onboardingWindow;
        try
        {
            onboardingWindow.ShowDialog();
        }
        finally
        {
            _onboardingWindow = null;
            if (markCompleted)
            {
                _settings.HasCompletedOnboarding = true;
                _settings.Save();
            }

            _tray.Refresh();
        }
    }

    private void PositionChildWindow(Window childWindow)
    {
        var workingArea = MonitorGeometry.GetPrimaryWorkingAreaDip(this);
        var left = Left + ActualWidth + 8;
        if (left + childWindow.Width > workingArea.Right)
        {
            left = Left - childWindow.Width - 8;
        }

        childWindow.Left = Math.Clamp(left, workingArea.Left + 8, workingArea.Right - childWindow.Width - 8);
        childWindow.Top = Math.Clamp(Top - childWindow.Height + ActualHeight, workingArea.Top + 8, workingArea.Bottom - childWindow.Height - 8);
    }

    private void OnTextChatStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateBusyIndicatorState(status);
            _tray.Refresh();
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

    private void OnMusicPlayerClosed(object? sender, EventArgs e)
    {
        if (sender is MusicPlayerWindow musicWindow)
        {
            musicWindow.Closed -= OnMusicPlayerClosed;
        }

        _musicPlayerWindow = null;
        if (_interactionMode == AthenaInteractionMode.Music)
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
            "Music mode" => _interactionMode == AthenaInteractionMode.Music ? "Music" : string.Empty,
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
        var showWalkingBubbles = !_clickThrough && _interactionMode == AthenaInteractionMode.None;
        TextModeBubble.Visibility = showWalkingBubbles
            ? Visibility.Visible
            : Visibility.Collapsed;
        VoiceModeBubble.Visibility = showWalkingBubbles
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (showWalkingBubbles)
        {
            UpdateWalkingThoughtText(_clock.Elapsed.TotalSeconds);
        }
    }

    private void UpdateWalkingThoughtText(double now)
    {
        if (_interactionMode != AthenaInteractionMode.None)
        {
            return;
        }

        var variantIndex = WalkingThoughtText.SelectIndex(now);
        if (variantIndex == _thoughtVariantIndex)
        {
            return;
        }

        _thoughtVariantIndex = variantIndex;
        TextModeBubbleText.Text = WalkingThoughtText.Variants[variantIndex];
    }

    private void UpdateAmbientSoundState()
    {
        if (_interactionMode == AthenaInteractionMode.None)
        {
            _ambientSoundPlayer.Play();
        }
        else
        {
            _ambientSoundPlayer.Pause();
        }
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
}
