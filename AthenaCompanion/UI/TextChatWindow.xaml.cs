using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AthenaCompanion.TextChat;

namespace AthenaCompanion.UI;

internal partial class TextChatWindow : Window
{
    private readonly AthenaTextChatSession _session;
    private readonly CancellationTokenSource _cts = new();
    private bool _sending;

    public TextChatWindow(AthenaTextChatSession session)
    {
        _session = session;
        _session.StatusChanged += OnSessionStatusChanged;
        InitializeComponent();
        AppendMessage("Athena", "Text mode is ready.");
    }

    public event EventHandler<string>? StatusChanged;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InputBox.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _session.StatusChanged -= OnSessionStatusChanged;
        _session.Dispose();
        _cts.Dispose();
        base.OnClosed(e);
    }

    private async void OnSend(object sender, RoutedEventArgs e) => await SendCurrentMessageAsync();

    private async void OnInputPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        await SendCurrentMessageAsync();
    }

    private async Task SendCurrentMessageAsync()
    {
        if (_sending)
        {
            return;
        }

        var message = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        InputBox.Clear();
        AppendMessage("You", message);
        SetSendingState(true, "Thinking");

        try
        {
            var reply = await _session.SendAsync(message, _cts.Token);
            if (!string.IsNullOrWhiteSpace(reply))
            {
                AppendMessage("Athena", reply);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendMessage("Athena", $"Text mode failed: {ex.Message}");
        }
        finally
        {
            SetSendingState(false, "Ready");
        }
    }

    private void SetSendingState(bool sending, string status)
    {
        _sending = sending;
        SendButton.IsEnabled = !sending;
        InputBox.IsEnabled = !sending;
        StatusText.Text = status;
        StatusChanged?.Invoke(this, sending ? status : "Text ready");
        if (!sending)
        {
            InputBox.Focus();
        }
    }

    private void AppendMessage(string author, string text)
    {
        var bubble = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10),
            MaxWidth = 360,
            HorizontalAlignment = author == "You" ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left,
            Background = author == "You"
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 69, 119))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 38, 61))
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = author,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 198, 238)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = System.Windows.Media.Brushes.WhiteSmoke,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });

        bubble.Child = stack;
        MessagesPanel.Children.Add(bubble);
        MessagesScroll.ScrollToEnd();
    }

    private void OnSessionStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = status;
            StatusChanged?.Invoke(this, status);
        });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
