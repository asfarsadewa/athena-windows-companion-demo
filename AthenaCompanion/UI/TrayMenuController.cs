using AthenaCompanion.Settings;
using WinForms = System.Windows.Forms;

namespace AthenaCompanion.UI;

internal sealed record TrayMenuStateProvider(
    Func<AthenaInteractionMode> GetInteractionMode,
    Func<bool> GetClickThrough,
    Func<string> GetVoiceStatus,
    Func<string> GetCurrentVoice,
    Func<string> GetKeyStatus);

internal sealed class TrayMenuController : IDisposable
{
    private readonly TrayMenuStateProvider _state;

    private WinForms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _trayIcon;
    private WinForms.ToolStripMenuItem? _pauseMenuItem;
    private WinForms.ToolStripMenuItem? _clickThroughMenuItem;
    private WinForms.ToolStripMenuItem? _textChatMenuItem;
    private WinForms.ToolStripMenuItem? _musicMenuItem;
    private WinForms.ToolStripMenuItem? _voiceStatusMenuItem;
    private WinForms.ToolStripMenuItem? _voiceMenuItem;
    private WinForms.ToolStripMenuItem? _removeApiKeyMenuItem;

    public TrayMenuController(TrayMenuStateProvider state)
    {
        _state = state;
    }

    public event EventHandler? PauseRequested;
    public event EventHandler? ClickThroughToggled;
    public event EventHandler? TextModeRequested;
    public event EventHandler? MusicRequested;
    public event EventHandler<string>? VoiceChanged;
    public event EventHandler? ConfigureApiKeyRequested;
    public event EventHandler? RemoveApiKeyRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        _pauseMenuItem = new WinForms.ToolStripMenuItem("Pause walking");
        _pauseMenuItem.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);

        _clickThroughMenuItem = new WinForms.ToolStripMenuItem("Click-through");
        _clickThroughMenuItem.Click += (_, _) => ClickThroughToggled?.Invoke(this, EventArgs.Empty);

        _textChatMenuItem = new WinForms.ToolStripMenuItem("Text chat");
        _textChatMenuItem.Click += (_, _) => TextModeRequested?.Invoke(this, EventArgs.Empty);

        _musicMenuItem = new WinForms.ToolStripMenuItem("Music player");
        _musicMenuItem.Click += (_, _) => MusicRequested?.Invoke(this, EventArgs.Empty);

        _voiceStatusMenuItem = new WinForms.ToolStripMenuItem("Voice off") { Enabled = false };
        _voiceMenuItem = BuildVoiceMenu();

        var configureApiKeyMenuItem = new WinForms.ToolStripMenuItem("OpenAI API Key...");
        configureApiKeyMenuItem.Click += (_, _) => ConfigureApiKeyRequested?.Invoke(this, EventArgs.Empty);

        _removeApiKeyMenuItem = new WinForms.ToolStripMenuItem("Remove saved OpenAI API Key");
        _removeApiKeyMenuItem.Click += (_, _) => RemoveApiKeyRequested?.Invoke(this, EventArgs.Empty);

        var exitMenuItem = new WinForms.ToolStripMenuItem("Exit");
        exitMenuItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add(_textChatMenuItem);
        menu.Items.Add(_musicMenuItem);
        menu.Items.Add(_clickThroughMenuItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_voiceStatusMenuItem);
        menu.Items.Add(_voiceMenuItem);
        menu.Items.Add(configureApiKeyMenuItem);
        menu.Items.Add(_removeApiKeyMenuItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitMenuItem);

        _trayIcon = IconLoader.LoadTrayIcon();
        _notifyIcon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _trayIcon ?? System.Drawing.SystemIcons.Application,
            Text = "Athena Companion",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);

        Refresh();
    }

    public void ShowContextMenu(System.Drawing.Point screenPosition) =>
        _notifyIcon?.ContextMenuStrip?.Show(screenPosition);

    public void ShowBalloonTip(string title, string message) =>
        _notifyIcon?.ShowBalloonTip(4000, title, message, WinForms.ToolTipIcon.Warning);

    public void Refresh()
    {
        var mode = _state.GetInteractionMode();
        var clickThrough = _state.GetClickThrough();
        var voiceStatus = _state.GetVoiceStatus();
        var currentVoice = _state.GetCurrentVoice();
        var keyStatus = _state.GetKeyStatus();

        if (_pauseMenuItem is not null)
        {
            _pauseMenuItem.Checked = mode == AthenaInteractionMode.Voice;
            _pauseMenuItem.Text = mode == AthenaInteractionMode.Voice ? "Resume walking" : "Pause for voice";
        }

        if (_textChatMenuItem is not null)
        {
            _textChatMenuItem.Checked = mode == AthenaInteractionMode.Text;
            _textChatMenuItem.Text = mode == AthenaInteractionMode.Text ? "Focus text chat" : "Text chat";
        }

        if (_musicMenuItem is not null)
        {
            _musicMenuItem.Checked = mode == AthenaInteractionMode.Music;
            _musicMenuItem.Text = mode == AthenaInteractionMode.Music ? "Focus music player" : "Music player";
        }

        if (_clickThroughMenuItem is not null)
        {
            _clickThroughMenuItem.Checked = clickThrough;
        }

        if (_voiceStatusMenuItem is not null)
        {
            _voiceStatusMenuItem.Text = $"Voice: {voiceStatus}, {currentVoice} ({keyStatus})";
        }

        if (_removeApiKeyMenuItem is not null)
        {
            _removeApiKeyMenuItem.Enabled = keyStatus == "Credential Manager";
        }

        if (_voiceMenuItem is not null)
        {
            foreach (WinForms.ToolStripItem item in _voiceMenuItem.DropDownItems)
            {
                if (item is WinForms.ToolStripMenuItem voiceItem && voiceItem.Tag is string voice)
                {
                    voiceItem.Checked = string.Equals(voice, currentVoice, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
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
            item.Click += (_, _) => VoiceChanged?.Invoke(this, voice);
            menu.DropDownItems.Add(item);
        }

        return menu;
    }

    private static string ToTitleCase(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
}
