using System.Text.Json;
using System.Windows;

namespace AthenaCompanion.Tools;

internal sealed class AthenaToolExecutor
{
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly Func<string?> _getApiKey;
    private readonly Action<string> _showImage;
    private readonly Action<string> _setStatus;

    public AthenaToolExecutor(Func<string?> getApiKey, Action<string> showImage, Action<string> setStatus)
    {
        _getApiKey = getApiKey;
        _showImage = showImage;
        _setStatus = setStatus;
    }

    public async Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        var apiKey = _getApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "I need an OpenAI API key before I can inspect or transform the screen.";
        }

        try
        {
            return name switch
            {
                "inspect_screen" => await InspectScreenAsync(apiKey, argumentsJson, cancellationToken),
                "create_screen_image" => await CreateScreenImageAsync(apiKey, argumentsJson, cancellationToken),
                _ => $"Unknown tool: {name}"
            };
        }
        catch (Exception ex)
        {
            return $"The screen tool failed: {ex.Message}";
        }
    }

    private async Task<string> InspectScreenAsync(string apiKey, string argumentsJson, CancellationToken cancellationToken)
    {
        _setStatus("Looking at screen");
        var request = ReadStringArgument(argumentsJson, "question") ?? "What is on my screen right now?";
        var screenshot = _screenCapture.CapturePrimaryScreenPng();
        var client = new OpenAiToolClient(apiKey);
        var answer = await client.AnalyzeScreenAsync(screenshot, request, cancellationToken);
        return answer;
    }

    private async Task<string> CreateScreenImageAsync(string apiKey, string argumentsJson, CancellationToken cancellationToken)
    {
        _setStatus("Creating image");
        var request = ReadStringArgument(argumentsJson, "prompt") ??
            ReadStringArgument(argumentsJson, "request") ??
            "Generate an infographic from what is visible on my screen.";

        var screenshot = _screenCapture.CapturePrimaryScreenPng();
        var client = new OpenAiToolClient(apiKey);
        var result = await client.GenerateScreenInfographicAsync(screenshot, request, cancellationToken);

        System.Windows.Application.Current.Dispatcher.Invoke(() => _showImage(result.ImagePath));

        return $"Done. I created an image from your screen and opened it in a lightbox. Saved image: {result.ImagePath}";
    }

    private static string? ReadStringArgument(string json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
