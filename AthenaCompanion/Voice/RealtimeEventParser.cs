using System.Text.Json;

namespace AthenaCompanion.Voice;

internal static class RealtimeEventParser
{
    public static bool IsFunctionCallEvent(JsonElement root) =>
        root.TryGetProperty("item", out var item) &&
        item.TryGetProperty("type", out var type) &&
        string.Equals(type.GetString(), "function_call", StringComparison.Ordinal);

    public static bool TryReadFunctionCallFromItemEvent(JsonElement root, out RealtimeFunctionCall call)
    {
        call = default!;
        if (!root.TryGetProperty("item", out var item) ||
            !item.TryGetProperty("type", out var type) ||
            !string.Equals(type.GetString(), "function_call", StringComparison.Ordinal))
        {
            return false;
        }

        return TryReadFunctionCallFromProperties(item, out call);
    }

    public static bool TryReadFunctionCallFromProperties(JsonElement element, out RealtimeFunctionCall call)
    {
        call = default!;
        if (!element.TryGetProperty("call_id", out var callIdElement) ||
            !element.TryGetProperty("name", out var nameElement))
        {
            return false;
        }

        var callId = callIdElement.GetString();
        var name = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var arguments = "{}";
        if (element.TryGetProperty("arguments", out var argumentsElement))
        {
            arguments = argumentsElement.ValueKind == JsonValueKind.String
                ? argumentsElement.GetString() ?? "{}"
                : argumentsElement.GetRawText();
        }

        call = new RealtimeFunctionCall(callId, name, arguments);
        return true;
    }

    public static string ReadError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) &&
            error.TryGetProperty("message", out var message))
        {
            return message.GetString() ?? "Realtime API error.";
        }

        return "Realtime API error.";
    }
}

internal sealed record RealtimeFunctionCall(string CallId, string Name, string Arguments);
