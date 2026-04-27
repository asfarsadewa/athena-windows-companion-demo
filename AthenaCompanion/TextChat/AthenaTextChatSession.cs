using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AthenaCompanion.Tools;

namespace AthenaCompanion.TextChat;

internal sealed class AthenaTextChatSession : IDisposable
{
    private const string Model = "gpt-5.5";
    private const int MaxToolRounds = 6;
    private readonly HttpClient _httpClient = new();
    private readonly AthenaToolExecutor _toolExecutor;
    private string? _previousResponseId;

    public AthenaTextChatSession(string apiKey, AthenaToolExecutor toolExecutor)
    {
        _toolExecutor = toolExecutor;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public event EventHandler<string>? StatusChanged;

    public async Task<string> SendAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        object[] input =
        [
            new
            {
                role = "user",
                content = message.Trim()
            }
        ];

        var previousResponseId = _previousResponseId;
        for (var round = 0; round < MaxToolRounds; round++)
        {
            StatusChanged?.Invoke(this, "Thinking");
            var response = await CreateResponseAsync(input, previousResponseId, cancellationToken);
            _previousResponseId = response.Id;

            if (response.ToolCalls.Count == 0)
            {
                StatusChanged?.Invoke(this, "Text ready");
                return string.IsNullOrWhiteSpace(response.Text)
                    ? "I finished, but I did not receive a text answer."
                    : response.Text;
            }

            var outputs = new List<object>(response.ToolCalls.Count);
            foreach (var call in response.ToolCalls)
            {
                StatusChanged?.Invoke(this, call.Name switch
                {
                    "inspect_screen" => "Looking at screen",
                    "create_screen_image" => "Creating image",
                    "open_music_player" => "Music mode",
                    _ => "Using tool"
                });

                var result = await _toolExecutor.ExecuteAsync(call.Name, call.Arguments, cancellationToken);
                if (!result.ContinueVoiceResponse)
                {
                    StatusChanged?.Invoke(this, "Music mode");
                    return result.Output;
                }

                outputs.Add(new
                {
                    type = "function_call_output",
                    call_id = call.CallId,
                    output = result.Output
                });
            }

            input = outputs.ToArray();
            previousResponseId = response.Id;
        }

        StatusChanged?.Invoke(this, "Text ready");
        return "I used several tools, but I need a fresh request before continuing.";
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<AthenaTextResponse> CreateResponseAsync(
        object[] input,
        string? previousResponseId,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["instructions"] = AthenaTextPrompt.Text,
            ["input"] = input,
            ["tools"] = AthenaToolDefinitions.Create(strict: true),
            ["tool_choice"] = "auto",
            ["parallel_tool_calls"] = false
        };

        if (!string.IsNullOrWhiteSpace(previousResponseId))
        {
            payload["previous_response_id"] = previousResponseId;
        }

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("https://api.openai.com/v1/responses", content, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(AthenaTextResponseParser.ReadApiError(responseJson));
        }

        return AthenaTextResponseParser.Parse(responseJson);
    }
}
