using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AthenaCompanion.Tools;

internal sealed class OpenAiToolClient
{
    private const string VisionModel = "gpt-5.5";
    private const string ImageModel = "gpt-image-2";
    private readonly HttpClient _httpClient = new();
    private readonly string _apiKey;

    public OpenAiToolClient(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<string> AnalyzeScreenAsync(byte[] screenshotPng, string question, CancellationToken cancellationToken)
    {
        var prompt = string.IsNullOrWhiteSpace(question)
            ? "Describe what is visible on this screen in a concise, useful way."
            : question;

        var payload = new
        {
            model = VisionModel,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = """
                                You are Athena's screen inspection specialist.
                                Answer concisely for spoken delivery.
                                Do not mention private or sensitive details unless they are clearly relevant to the user's request.
                                User request:
                                """ + "\n" + prompt
                        },
                        new
                        {
                            type = "input_image",
                            image_url = ToDataUrl(screenshotPng)
                        }
                    }
                }
            }
        };

        using var response = await PostJsonAsync("https://api.openai.com/v1/responses", payload, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return ExtractResponseText(json);
    }

    public async Task<ScreenImageResult> GenerateScreenInfographicAsync(
        byte[] screenshotPng,
        string request,
        CancellationToken cancellationToken)
    {
        var analysis = await AnalyzeScreenForImagePromptAsync(screenshotPng, request, cancellationToken);
        var imagePrompt = BuildImagePrompt(analysis, request);

        var payload = new
        {
            model = ImageModel,
            prompt = imagePrompt,
            size = "1536x1024",
            quality = "medium",
            output_format = "png",
            n = 1
        };

        using var response = await PostJsonAsync("https://api.openai.com/v1/images/generations", payload, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var imageBytes = ExtractImageBytes(json);
        var path = SaveGeneratedImage(imageBytes);
        return new ScreenImageResult(path, analysis);
    }

    private async Task<string> AnalyzeScreenForImagePromptAsync(
        byte[] screenshotPng,
        string request,
        CancellationToken cancellationToken)
    {
        var prompt = string.IsNullOrWhiteSpace(request)
            ? "Create an infographic from the currently visible screen."
            : request;

        return await AnalyzeScreenAsync(
            screenshotPng,
            """
            Inspect this screen and produce source content for a clean infographic.
            Return a compact brief with: title, 3-6 key observations, visual hierarchy, and any important labels.
            Avoid including secrets, account numbers, full emails, API keys, or private messages.
            User request:
            """ + "\n" + prompt,
            cancellationToken);
    }

    private static string BuildImagePrompt(string analysis, string request) =>
        $"""
        Create a polished, readable infographic based on the user's current screen.
        Style: elegant modern desktop-companion presentation, clear hierarchy, soft white/lavender accents, restrained 90s anime UI influence, no copyrighted logos unless already described generically.
        Layout: landscape 1536x1024, title at top, 3-6 organized sections, clean icon-like visual metaphors, readable labels.
        User request: {request}

        Source content from screen analysis:
        {analysis}

        Constraints: do not reproduce private data verbatim; do not include API keys, emails, passwords, account numbers, or private chat text; summarize sensitive-looking information generically.
        """;

    private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _httpClient.PostAsync(url, content, cancellationToken);
    }

    private static string ExtractResponseText(string json)
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

    private static byte[] ExtractImageBytes(string json)
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

    private static string SaveGeneratedImage(byte[] imageBytes)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Athena Companion");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"athena-screen-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        File.WriteAllBytes(path, imageBytes);
        return path;
    }

    private static string ToDataUrl(byte[] png) => $"data:image/png;base64,{Convert.ToBase64String(png)}";
}

internal sealed record ScreenImageResult(string ImagePath, string Analysis);
