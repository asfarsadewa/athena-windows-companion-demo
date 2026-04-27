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
                    _ => "Using tool"
                });

                var output = await _toolExecutor.ExecuteAsync(call.Name, call.Arguments, cancellationToken);
                outputs.Add(new
                {
                    type = "function_call_output",
                    call_id = call.CallId,
                    output
                });
            }

            input = outputs.ToArray();
            previousResponseId = response.Id;
        }

        StatusChanged?.Invoke(this, "Text ready");
        return "I used several tools, but I need a fresh request before continuing.";
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<TextResponse> CreateResponseAsync(
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
            throw new InvalidOperationException(ReadApiError(responseJson));
        }

        return ParseResponse(responseJson);
    }

    private static TextResponse ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idElement)
            ? idElement.GetString() ?? string.Empty
            : string.Empty;

        var text = ExtractResponseText(root);
        var toolCalls = ExtractToolCalls(root);
        return new TextResponse(id, text, toolCalls);
    }

    private static string ExtractResponseText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    builder.AppendLine(text.GetString());
                }
            }
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<TextToolCall> ExtractToolCalls(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var calls = new List<TextToolCall>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "function_call", StringComparison.Ordinal) ||
                !item.TryGetProperty("call_id", out var callIdElement) ||
                !item.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var callId = callIdElement.GetString();
            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var arguments = "{}";
            if (item.TryGetProperty("arguments", out var argumentsElement))
            {
                arguments = argumentsElement.ValueKind == JsonValueKind.String
                    ? argumentsElement.GetString() ?? "{}"
                    : argumentsElement.GetRawText();
            }

            calls.Add(new TextToolCall(callId, name, arguments));
        }

        return calls;
    }

    private static string ReadApiError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "OpenAI API error.";
            }
        }
        catch
        {
            return "OpenAI API error.";
        }

        return "OpenAI API error.";
    }

    private sealed record TextResponse(string Id, string Text, IReadOnlyList<TextToolCall> ToolCalls);

    private sealed record TextToolCall(string CallId, string Name, string Arguments);
}
