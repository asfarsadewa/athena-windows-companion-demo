using System.Text;
using System.Text.Json;

namespace AthenaCompanion.Tools;

internal static class OpenAiToolResponseParser
{
    public static string BuildImagePrompt(string analysis, string request) =>
        $"""
        Create a polished, readable infographic based on the user's current screen.
        Style: elegant modern desktop-companion presentation, clear hierarchy, soft white/lavender accents, restrained 90s anime UI influence, no copyrighted logos unless already described generically.
        Layout: landscape 1536x1024, title at top, 3-6 organized sections, clean icon-like visual metaphors, readable labels.
        User request: {request}

        Source content from screen analysis:
        {analysis}

        Constraints: do not reproduce private data verbatim; do not include API keys, emails, passwords, account numbers, or private chat text; summarize sensitive-looking information generically.
        """;

    public static string ExtractResponseText(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("output", out var output) &&
            output.ValueKind == JsonValueKind.Array)
        {
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

            var extracted = builder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted;
            }
        }

        return "I inspected the screen, but I could not extract a text answer.";
    }

    public static byte[] ExtractImageBytes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var b64 = root.GetProperty("data")[0].GetProperty("b64_json").GetString();
        if (string.IsNullOrWhiteSpace(b64))
        {
            throw new InvalidOperationException("Image response did not include image data.");
        }

        return Convert.FromBase64String(b64);
    }

    public static string ToDataUrl(byte[] png) => $"data:image/png;base64,{Convert.ToBase64String(png)}";
}
