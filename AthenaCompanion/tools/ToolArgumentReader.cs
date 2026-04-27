using System.Text.Json;

namespace AthenaCompanion.Tools;

internal static class ToolArgumentReader
{
    public static string? ReadStringArgument(string? json, string name)
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
