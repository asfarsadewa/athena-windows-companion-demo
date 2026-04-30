using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AthenaCompanion.Settings;

internal partial class OnboardingWindow : Window
{
    private readonly string _musicDirectory;
    private readonly Action<Window> _configureApiKey;
    private int _stepIndex;

    private readonly OnboardingStep[] _steps;

    public OnboardingWindow(string musicDirectory, Action<Window> configureApiKey)
    {
        _musicDirectory = musicDirectory;
        _configureApiKey = configureApiKey;
        _steps =
        [
            new OnboardingStep(
                "Meet Athena",
                "Athena walks above your taskbar by default.\n\nLeft-click Athena to pause for voice. Use the Chat bubble for typed chat. Right-click Athena or use the tray icon for settings, music, and exit."),
            new OnboardingStep(
                "Privacy boundaries",
                "Athena does not listen while she is walking.\n\nThe microphone is active only while you pause for voice. Screen capture only happens after you explicitly ask about your screen or request a screen-based image."),
            new OnboardingStep(
                "Your OpenAI key",
                "Voice, text, screen inspection, and image generation use your own OpenAI API key.\n\nThe key stays on this Windows machine and is stored in Windows Credential Manager. You can skip this now and set it up later from the tray menu."),
            new OnboardingStep(
                "Music folder",
                $"Athena can play local .mp3 and .m4a files through her radio-style music mode.\n\nAdd music under:\n{_musicDirectory}")
        ];

        InitializeComponent();
        UpdateStep();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_stepIndex <= 0)
        {
            return;
        }

        _stepIndex--;
        UpdateStep();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_stepIndex >= _steps.Length - 1)
        {
            DialogResult = true;
            return;
        }

        _stepIndex++;
        UpdateStep();
    }

    private void OnSkip(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnSetUpKey(object sender, RoutedEventArgs e)
    {
        _configureApiKey(this);
        ActionStatusText.Text = "Key setup closed.";
    }

    private void OnOpenMusicFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_musicDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _musicDirectory,
            UseShellExecute = true
        });
    }

    private void UpdateStep()
    {
        var step = _steps[_stepIndex];
        StepText.Text = $"{_stepIndex + 1} of {_steps.Length}";
        TitleText.Text = step.Title;
        BodyText.Text = step.Body;
        BackButton.IsEnabled = _stepIndex > 0;
        NextButton.Content = _stepIndex == _steps.Length - 1 ? "Done" : "Next";
        SetUpKeyButton.Visibility = _stepIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        OpenMusicFolderButton.Visibility = _stepIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        ActionStatusText.Text = string.Empty;
    }

    private sealed record OnboardingStep(string Title, string Body);
}
