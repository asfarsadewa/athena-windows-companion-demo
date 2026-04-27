using System.Windows;
using AthenaCompanion.Security;
using AthenaCompanion.Settings;
using AthenaCompanion.Tools;

namespace AthenaCompanion.Voice;

internal sealed class AthenaVoiceController : IAsyncDisposable
{
    private readonly OpenAiKeyProvider _keyProvider = new();
    private readonly Func<string> _getVoice;
    private readonly Action<string> _showImage;
    private AthenaRealtimeSession? _session;
    private bool _starting;

    public AthenaVoiceController(Func<string> getVoice, Action<string> showImage)
    {
        _getVoice = getVoice;
        _showImage = showImage;
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? Error;

    public bool IsActive => _session is not null;

    public async Task StartAsync(Window owner, CancellationToken cancellationToken = default)
    {
        if (_session is not null || _starting)
        {
            return;
        }

        _starting = true;
        try
        {
            var lookup = _keyProvider.TryGetApiKey();
            var apiKey = lookup.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = PromptForApiKey(owner);
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                StatusChanged?.Invoke(this, "Voice disabled");
                return;
            }

            var tools = new AthenaToolExecutor(
                () => apiKey,
                _showImage,
                status => StatusChanged?.Invoke(this, status));

            var session = new AthenaRealtimeSession(apiKey, AthenaVoicePrompt.Text, _getVoice(), tools);
            session.StatusChanged += OnSessionStatusChanged;
            session.Error += OnSessionError;

            _session = session;
            await session.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, $"Voice failed to start: {ex.Message}");
            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }
        }
        finally
        {
            _starting = false;
        }
    }

    public async Task StopAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        _session = null;
        session.StatusChanged -= OnSessionStatusChanged;
        session.Error -= OnSessionError;
        await session.DisposeAsync();
        StatusChanged?.Invoke(this, "Voice off");
    }

    public bool ConfigureApiKey(Window owner)
    {
        var key = PromptForApiKey(owner);
        return !string.IsNullOrWhiteSpace(key);
    }

    public void RemoveSavedApiKey()
    {
        _keyProvider.DeleteSavedApiKey();
        StatusChanged?.Invoke(this, "Saved API key removed");
    }

    public string GetKeyStatus()
    {
        var lookup = _keyProvider.TryGetApiKey();
        return lookup.Source switch
        {
            OpenAiKeySource.WindowsCredentialManager => "Credential Manager",
            OpenAiKeySource.EnvironmentVariable => "OPENAI_API_KEY",
            _ => "Not configured"
        };
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private string? PromptForApiKey(Window owner)
    {
        var dialog = new ApiKeySetupWindow { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        _keyProvider.SaveApiKey(dialog.ApiKey);
        StatusChanged?.Invoke(this, "API key saved");
        return dialog.ApiKey;
    }

    private void OnSessionStatusChanged(object? sender, string status) => StatusChanged?.Invoke(this, status);

    private void OnSessionError(object? sender, string error) => Error?.Invoke(this, error);
}
