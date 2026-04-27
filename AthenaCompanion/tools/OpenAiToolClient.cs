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
                            image_url = OpenAiToolResponseParser.ToDataUrl(screenshotPng)
                        }
                    }
                }
            }
        };

        using var response = await PostJsonAsync("https://api.openai.com/v1/responses", payload, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return OpenAiToolResponseParser.ExtractResponseText(json);
    }

    public async Task<ScreenImageResult> GenerateScreenInfographicAsync(
        byte[] screenshotPng,
        string request,
        CancellationToken cancellationToken)
    {
        var analysis = await AnalyzeScreenForImagePromptAsync(screenshotPng, request, cancellationToken);
        var imagePrompt = OpenAiToolResponseParser.BuildImagePrompt(analysis, request);

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

        var imageBytes = OpenAiToolResponseParser.ExtractImageBytes(json);
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

    private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _httpClient.PostAsync(url, content, cancellationToken);
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
}

internal sealed record ScreenImageResult(string ImagePath, string Analysis);
