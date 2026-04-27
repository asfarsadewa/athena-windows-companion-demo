using System.Windows;

namespace AthenaCompanion.Settings;

public partial class ApiKeySetupWindow : Window
{
    public ApiKeySetupWindow()
    {
        InitializeComponent();
    }

    public string ApiKey => ApiKeyBox.Password.Trim();

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(ApiKeyBox.Password);
        ValidationText.Text = string.Empty;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ValidationText.Text = "Enter an OpenAI API key.";
            return;
        }

        DialogResult = true;
    }
}
