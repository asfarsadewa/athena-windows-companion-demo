using AthenaCompanion.Music;

namespace AthenaCompanion.Tools;

internal sealed class AthenaToolExecutor
{
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly Func<string?> _getApiKey;
    private readonly Action<string> _showImage;
    private readonly Action<string> _setStatus;
    private readonly Action<MusicPlayerRequest>? _openMusicPlayer;

    public AthenaToolExecutor(
        Func<string?> getApiKey,
        Action<string> showImage,
        Action<string> setStatus,
        Action<MusicPlayerRequest>? openMusicPlayer = null)
    {
        _getApiKey = getApiKey;
        _showImage = showImage;
        _setStatus = setStatus;
        _openMusicPlayer = openMusicPlayer;
    }

    public async Task<AthenaToolResult> ExecuteAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        if (string.Equals(name, "open_music_player", StringComparison.Ordinal))
        {
            return OpenMusicPlayer(argumentsJson);
        }

        var apiKey = _getApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AthenaToolResult.Continue("I need an OpenAI API key before I can inspect or transform the screen.");
        }

        try
        {
            return name switch
            {
                "inspect_screen" => await InspectScreenAsync(apiKey, argumentsJson, cancellationToken),
                "create_screen_image" => await CreateScreenImageAsync(apiKey, argumentsJson, cancellationToken),
                _ => AthenaToolResult.Continue($"Unknown tool: {name}")
            };
        }
        catch (Exception ex)
        {
            return AthenaToolResult.Continue($"The screen tool failed: {ex.Message}");
        }
    }

    private AthenaToolResult OpenMusicPlayer(string argumentsJson)
    {
        if (_openMusicPlayer is null)
        {
            return AthenaToolResult.Continue("The music player is unavailable in this context.");
        }

        try
        {
            _setStatus("Music mode");
            var query = ToolArgumentReader.ReadStringArgument(argumentsJson, "query") ?? string.Empty;
            var autoplay = ToolArgumentReader.ReadBoolArgument(argumentsJson, "autoplay") ?? !string.IsNullOrWhiteSpace(query);
            var request = new MusicPlayerRequest(query, autoplay);
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                _openMusicPlayer(request);
            }
            else
            {
                dispatcher.InvokeAsync(
                    () => _openMusicPlayer(request),
                    System.Windows.Threading.DispatcherPriority.Background);
            }

            return AthenaToolResult.StopVoiceWithoutResponse("Opening music mode. Voice mode is stopping while music plays.");
        }
        catch (Exception ex)
        {
            return AthenaToolResult.Continue($"The music player failed: {ex.Message}");
        }
    }

    private async Task<AthenaToolResult> InspectScreenAsync(string apiKey, string argumentsJson, CancellationToken cancellationToken)
    {
        _setStatus("Looking at screen");
        var request = ToolArgumentReader.ReadStringArgument(argumentsJson, "question") ?? "What is on my screen right now?";
        var screenshot = _screenCapture.CapturePrimaryScreenPng();
        var client = new OpenAiToolClient(apiKey);
        var answer = await client.AnalyzeScreenAsync(screenshot, request, cancellationToken);
        return AthenaToolResult.Continue(answer);
    }

    private async Task<AthenaToolResult> CreateScreenImageAsync(string apiKey, string argumentsJson, CancellationToken cancellationToken)
    {
        _setStatus("Creating image");
        var request = ToolArgumentReader.ReadStringArgument(argumentsJson, "prompt") ??
            ToolArgumentReader.ReadStringArgument(argumentsJson, "request") ??
            "Generate an infographic from what is visible on my screen.";

        var screenshot = _screenCapture.CapturePrimaryScreenPng();
        var client = new OpenAiToolClient(apiKey);
        var result = await client.GenerateScreenInfographicAsync(screenshot, request, cancellationToken);

        System.Windows.Application.Current.Dispatcher.Invoke(() => _showImage(result.ImagePath));

        return AthenaToolResult.Continue($"Done. I created an image from your screen and opened it in a lightbox. Saved image: {result.ImagePath}");
    }
}
