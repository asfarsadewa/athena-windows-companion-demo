using System.Text;
using System.Text.Json;

namespace AthenaCompanion.TextChat;

internal static class AthenaTextResponseParser
{
    public static AthenaTextResponse Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idElement)
            ? idElement.GetString() ?? string.Empty
            : string.Empty;

        var text = ExtractResponseText(root);
        var toolCalls = ExtractToolCalls(root);
        return new AthenaTextResponse(id, text, toolCalls);
    }

    public static string ReadApiError(string json)
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

    private static IReadOnlyList<AthenaTextToolCall> ExtractToolCalls(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var calls = new List<AthenaTextToolCall>();
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

            calls.Add(new AthenaTextToolCall(callId, name, arguments));
        }

        return calls;
    }
}

internal sealed record AthenaTextResponse(string Id, string Text, IReadOnlyList<AthenaTextToolCall> ToolCalls);

internal sealed record AthenaTextToolCall(string CallId, string Name, string Arguments);
