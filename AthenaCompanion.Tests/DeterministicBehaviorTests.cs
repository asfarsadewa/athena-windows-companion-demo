using System.Text.Json;
using AthenaCompanion;
using AthenaCompanion.Settings;
using AthenaCompanion.TextChat;
using AthenaCompanion.Tools;
using AthenaCompanion.UI;
using AthenaCompanion.Voice;

namespace AthenaCompanion.Tests;

public sealed class RealtimeVoiceOptionsTests
{
    [Fact]
    public void DefaultVoiceIsSupported()
    {
        Assert.True(RealtimeVoiceOptions.IsSupported(RealtimeVoiceOptions.Default));
    }

    [Theory]
    [InlineData("alloy")]
    [InlineData("ALLOY")]
    [InlineData("Marin")]
    public void SupportedVoicesAreCaseInsensitive(string voice)
    {
        Assert.True(RealtimeVoiceOptions.IsSupported(voice));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData(" alloy ")]
    public void UnsupportedVoicesAreRejected(string? voice)
    {
        Assert.False(RealtimeVoiceOptions.IsSupported(voice));
    }
}

public sealed class AthenaRealtimeSessionTests
{
    [Fact]
    public void SessionUpdateUsesRealtimeTwoWithLowReasoningEffort()
    {
        var json = JsonSerializer.Serialize(
            AthenaRealtimeSession.CreateSessionUpdatePayload("test instructions", "marin"));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var session = root.GetProperty("session");

        Assert.Equal("session.update", root.GetProperty("type").GetString());
        Assert.Equal("realtime", session.GetProperty("type").GetString());
        Assert.Equal("gpt-realtime-2", session.GetProperty("model").GetString());
        Assert.Equal("low", session.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("test instructions", session.GetProperty("instructions").GetString());
        Assert.Equal("marin", session.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString());
        Assert.Equal("server_vad", session.GetProperty("audio").GetProperty("input").GetProperty("turn_detection").GetProperty("type").GetString());
        Assert.Equal("auto", session.GetProperty("tool_choice").GetString());
    }
}

public sealed class AthenaSettingsTests
{
    [Fact]
    public void LoadFromPathReturnsDefaultsWhenFileIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");

        var settings = AthenaSettings.LoadFromPath(path);

        Assert.Equal(RealtimeVoiceOptions.Default, settings.Voice);
        Assert.False(settings.HasCompletedOnboarding);
    }

    [Fact]
    public void LoadFromPathReturnsDefaultsWhenJsonIsInvalid()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "not-json");

        var settings = AthenaSettings.LoadFromPath(path);

        Assert.Equal(RealtimeVoiceOptions.Default, settings.Voice);
        Assert.False(settings.HasCompletedOnboarding);
    }

    [Fact]
    public void LoadFromPathNormalizesUnsupportedVoice()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, """{"voice":"unsupported"}""");

        var settings = AthenaSettings.LoadFromPath(path);

        Assert.Equal(RealtimeVoiceOptions.Default, settings.Voice);
    }

    [Fact]
    public void SaveToPathCreatesDirectoryAndNormalizesBeforeWriting()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "nested", "settings.json");
        var settings = new AthenaSettings { Voice = "not-a-voice" };

        settings.SaveToPath(path);
        var saved = AthenaSettings.LoadFromPath(path);

        Assert.True(File.Exists(path));
        Assert.Equal(RealtimeVoiceOptions.Default, saved.Voice);
    }

    [Fact]
    public void SaveToPathPreservesCompletedOnboarding()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var settings = new AthenaSettings { HasCompletedOnboarding = true };

        settings.SaveToPath(path);
        var saved = AthenaSettings.LoadFromPath(path);

        Assert.True(saved.HasCompletedOnboarding);
    }
}

public sealed class AnimationFrameSelectorTests
{
    [Fact]
    public void SelectFrameIndexLoopsWalkFrames()
    {
        var clip = new AnimationClip("Walk", 0, 3, 1, PingPong: false);

        Assert.Equal(0, AnimationFrameSelector.SelectFrameIndex(clip, -1, frameTotal: 5));
        Assert.Equal(0, AnimationFrameSelector.SelectFrameIndex(clip, 0.99, frameTotal: 5));
        Assert.Equal(1, AnimationFrameSelector.SelectFrameIndex(clip, 1, frameTotal: 5));
        Assert.Equal(2, AnimationFrameSelector.SelectFrameIndex(clip, 2, frameTotal: 5));
        Assert.Equal(0, AnimationFrameSelector.SelectFrameIndex(clip, 3, frameTotal: 5));
    }

    [Fact]
    public void SelectFrameIndexPingPongsPoseFrames()
    {
        var clip = new AnimationClip("Pose", 10, 4, 1, PingPong: true);

        var selected = Enumerable.Range(0, 7)
            .Select(second => AnimationFrameSelector.SelectFrameIndex(clip, second, frameTotal: 20))
            .ToArray();

        Assert.Equal([10, 11, 12, 13, 12, 11, 10], selected);
    }

    [Fact]
    public void SelectFrameIndexClampsToAvailableFrameTotal()
    {
        var clip = new AnimationClip("Short", 4, 10, 1, PingPong: false);

        Assert.Equal(5, AnimationFrameSelector.SelectFrameIndex(clip, 3, frameTotal: 6));
    }

    [Fact]
    public void SelectFrameIndexRejectsEmptyFrameSets()
    {
        var clip = new AnimationClip("Empty", 0, 1, 1, PingPong: false);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnimationFrameSelector.SelectFrameIndex(clip, 0, frameTotal: 0));
    }
}

public sealed class SpriteAtlasManifestTests
{
    [Fact]
    public void CreateWalkClipClampsAgainstAvailableFrames()
    {
        var manifest = new SpriteAtlasManifest
        {
            WalkStartFrame = 10,
            WalkFrameCount = 5,
            WalkFramesPerSecond = 0
        };

        var clip = manifest.CreateWalkClip(frameTotal: 4);

        Assert.Equal("Walk", clip.Name);
        Assert.Equal(3, clip.StartFrame);
        Assert.Equal(1, clip.FrameCount);
        Assert.Equal(1, clip.FramesPerSecond);
        Assert.False(clip.PingPong);
    }

    [Fact]
    public void CreatePoseClipUsesPingPongPlayback()
    {
        var clip = new SpriteAtlasManifest().CreatePoseClip(frameTotal: 32);

        Assert.Equal("Pose", clip.Name);
        Assert.True(clip.PingPong);
    }

    [Fact]
    public void CreateBarkClipReturnsNullWhenManifestHasNoBarkFrames()
    {
        Assert.Null(new SpriteAtlasManifest().CreateBarkClip(frameTotal: 32));
    }

    [Fact]
    public void CreateBarkClipUsesConfiguredFrameRange()
    {
        var manifest = new SpriteAtlasManifest
        {
            BarkStartFrame = 26,
            BarkFrameCount = 12,
            BarkFramesPerSecond = 0,
            BarkPingPong = true
        };

        var clip = manifest.CreateBarkClip(frameTotal: 32);

        Assert.NotNull(clip);
        Assert.Equal("Bark", clip.Name);
        Assert.Equal(26, clip.StartFrame);
        Assert.Equal(6, clip.FrameCount);
        Assert.Equal(1, clip.FramesPerSecond);
        Assert.True(clip.PingPong);
    }

    [Fact]
    public void LoadReadsCustomPuppyManifest()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "puppy-atlas.json"),
            """
            {
              "atlas": "puppy-atlas.png",
              "columns": 8,
              "rows": 4,
              "barkStartFrame": 26,
              "barkFrameCount": 6
            }
            """);

        var manifest = SpriteAtlasManifest.Load(temp.Path, "puppy-atlas.json", "puppy-atlas.png");
        var bark = manifest.CreateBarkClip(frameTotal: 32);

        Assert.Equal("puppy-atlas.png", manifest.Atlas);
        Assert.Equal(8, manifest.Columns);
        Assert.Equal(4, manifest.Rows);
        Assert.NotNull(bark);
        Assert.Equal(26, bark.StartFrame);
        Assert.Equal(6, bark.FrameCount);
    }

    [Fact]
    public void LoadNormalizesInvalidManifestDimensions()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "athena-atlas.json"),
            """
            {
              "columns": 0,
              "rows": -3,
              "frameWidth": 0,
              "frameHeight": -1,
              "walkFrameCount": 0,
              "poseFrameCount": -5
            }
            """);

        var manifest = SpriteAtlasManifest.Load(temp.Path);

        Assert.Equal(1, manifest.Columns);
        Assert.Equal(1, manifest.Rows);
        Assert.Equal(1, manifest.FrameWidth);
        Assert.Equal(1, manifest.FrameHeight);
        Assert.Equal(1, manifest.WalkFrameCount);
        Assert.Equal(1, manifest.PoseFrameCount);
    }
}

public sealed class DogCompanionControllerTests
{
    private static readonly DogCompanionFrame DefaultFrame = new(
        AthenaLeft: 460,
        AthenaTop: 700,
        AthenaWidth: 190,
        AthenaHeight: 176,
        WorkAreaLeft: 0,
        WorkAreaRight: 1200,
        DogWidth: 130,
        DogHeight: 116);

    [Fact]
    public void TickKeepsPuppyNearAthenaAndInsideWorkingArea()
    {
        var controller = new DogCompanionController(new Random(8));
        DogCompanionSnapshot snapshot = default;

        for (var step = 0; step < 300; step++)
        {
            var now = step / 10.0;
            snapshot = controller.Tick(now, dt: 0.1, DefaultFrame);
        }

        var dogCenter = snapshot.X + DefaultFrame.DogWidth / 2;
        Assert.InRange(dogCenter, DefaultFrame.AthenaCenterX - 142.01, DefaultFrame.AthenaCenterX + 142.01);
        Assert.InRange(snapshot.X, DefaultFrame.WorkAreaLeft + 6, DefaultFrame.WorkAreaRight - DefaultFrame.DogWidth - 6);
        Assert.Equal(DefaultFrame.DogTop, snapshot.Top);
    }

    [Fact]
    public void SeededControllersProduceMatchingBehavior()
    {
        var first = new DogCompanionController(new Random(12));
        var second = new DogCompanionController(new Random(12));

        for (var step = 0; step < 120; step++)
        {
            var now = step / 12.0;
            var firstSnapshot = first.Tick(now, dt: 1.0 / 12.0, DefaultFrame);
            var secondSnapshot = second.Tick(now, dt: 1.0 / 12.0, DefaultFrame);

            Assert.Equal(firstSnapshot.X, secondSnapshot.X, precision: 8);
            Assert.Equal(firstSnapshot.Top, secondSnapshot.Top, precision: 8);
            Assert.Equal(firstSnapshot.Direction, secondSnapshot.Direction);
            Assert.Equal(firstSnapshot.Mode, secondSnapshot.Mode);
            Assert.Equal(firstSnapshot.BarkText, secondSnapshot.BarkText);
        }
    }

    [Fact]
    public void BarkTextAppearsBrieflyAndThenExpires()
    {
        var controller = new DogCompanionController(new Random(3));
        var sawBark = false;
        var sawBarkExpire = false;

        for (var step = 0; step < 240; step++)
        {
            var now = step / 10.0;
            var snapshot = controller.Tick(now, dt: 0.1, DefaultFrame);

            if (!string.IsNullOrWhiteSpace(snapshot.BarkText))
            {
                sawBark = true;
                Assert.Contains(snapshot.BarkText, new[] { "woof", "arf", "yip", "ruff" });
            }
            else if (sawBark)
            {
                sawBarkExpire = true;
                break;
            }
        }

        Assert.True(sawBark);
        Assert.True(sawBarkExpire);
    }
}

public sealed class WalkingThoughtTextTests
{
    [Fact]
    public void VariantsUseApprovedWalkingThoughtLabels()
    {
        Assert.Equal(["Hmm ...", "Ah ...", "...", ". . . ."], WalkingThoughtText.Variants);
        Assert.DoesNotContain(WalkingThoughtText.Variants, variant =>
            string.Equals(variant, "Chat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectIndexCyclesThroughVariantsEveryFewSeconds()
    {
        Assert.Equal(0, WalkingThoughtText.SelectIndex(-1));
        Assert.Equal(0, WalkingThoughtText.SelectIndex(3.99));
        Assert.Equal(1, WalkingThoughtText.SelectIndex(4));
        Assert.Equal(2, WalkingThoughtText.SelectIndex(8));
        Assert.Equal(3, WalkingThoughtText.SelectIndex(12));
        Assert.Equal(0, WalkingThoughtText.SelectIndex(16));
    }
}

public sealed class ToolArgumentReaderTests
{
    [Fact]
    public void ReadStringArgumentReturnsRequestedString()
    {
        var value = ToolArgumentReader.ReadStringArgument("""{"question":"What is visible?"}""", "question");

        Assert.Equal("What is visible?", value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("""{"question":42}""")]
    [InlineData("""{"other":"value"}""")]
    public void ReadStringArgumentReturnsNullWhenArgumentIsUnavailable(string json)
    {
        Assert.Null(ToolArgumentReader.ReadStringArgument(json, "question"));
    }
}

public sealed class AthenaToolDefinitionsTests
{
    [Fact]
    public void StrictToolDefinitionsRequireKnownArgumentsOnly()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(AthenaToolDefinitions.Create(strict: true)));
        var inspect = FindTool(document.RootElement, "inspect_screen");
        var image = FindTool(document.RootElement, "create_screen_image");

        Assert.True(inspect.GetProperty("strict").GetBoolean());
        Assert.False(inspect.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("question", inspect.GetProperty("parameters").GetProperty("required")[0].GetString());

        Assert.True(image.GetProperty("strict").GetBoolean());
        Assert.False(image.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("prompt", image.GetProperty("parameters").GetProperty("required")[0].GetString());
    }

    [Fact]
    public void RealtimeToolDefinitionsOmitStrictResponseSchemaFlags()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(AthenaToolDefinitions.Create(strict: false)));
        var inspect = FindTool(document.RootElement, "inspect_screen");

        Assert.False(inspect.TryGetProperty("strict", out _));
        Assert.False(inspect.GetProperty("parameters").TryGetProperty("additionalProperties", out _));
        Assert.Equal("question", inspect.GetProperty("parameters").GetProperty("required")[0].GetString());
    }

    private static JsonElement FindTool(JsonElement root, string name) =>
        root.EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == name);
}

public sealed class OpenAiToolResponseParserTests
{
    [Fact]
    public void ExtractResponseTextPrefersOutputText()
    {
        var text = OpenAiToolResponseParser.ExtractResponseText("""{"output_text":"Done"}""");

        Assert.Equal("Done", text);
    }

    [Fact]
    public void ExtractResponseTextReadsNestedOutputContent()
    {
        var text = OpenAiToolResponseParser.ExtractResponseText(
            """{"output":[{"content":[{"text":"One"},{"text":"Two"}]}]}""");

        Assert.Equal($"One{Environment.NewLine}Two", text);
    }

    [Fact]
    public void ExtractResponseTextReturnsFallbackWhenNoTextExists()
    {
        var text = OpenAiToolResponseParser.ExtractResponseText("""{"output":[]}""");

        Assert.Equal("I inspected the screen, but I could not extract a text answer.", text);
    }

    [Fact]
    public void ExtractImageBytesDecodesBase64ImageData()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        var json = $$"""{"data":[{"b64_json":"{{Convert.ToBase64String(expected)}}"}]}""";

        var actual = OpenAiToolResponseParser.ExtractImageBytes(json);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToDataUrlWrapsPngBytes()
    {
        var dataUrl = OpenAiToolResponseParser.ToDataUrl([0x89, 0x50, 0x4E, 0x47]);

        Assert.Equal("data:image/png;base64,iVBORw==", dataUrl);
    }

    [Fact]
    public void BuildImagePromptIncludesRequestAnalysisAndPrivacyConstraints()
    {
        var prompt = OpenAiToolResponseParser.BuildImagePrompt("analysis brief", "make an infographic");

        Assert.Contains("make an infographic", prompt);
        Assert.Contains("analysis brief", prompt);
        Assert.Contains("do not include API keys", prompt);
        Assert.Contains("private chat text", prompt);
    }
}

public sealed class AthenaTextResponseParserTests
{
    [Fact]
    public void ParseReadsIdAndOutputText()
    {
        var response = AthenaTextResponseParser.Parse("""{"id":"resp_1","output_text":"Hello"}""");

        Assert.Equal("resp_1", response.Id);
        Assert.Equal("Hello", response.Text);
        Assert.Empty(response.ToolCalls);
    }

    [Fact]
    public void ParseReadsNestedTextAndFunctionCalls()
    {
        var response = AthenaTextResponseParser.Parse(
            """
            {
              "id": "resp_2",
              "output": [
                {"content": [{"text": "Line one"}, {"text": "Line two"}]},
                {"type": "function_call", "call_id": "call_1", "name": "inspect_screen", "arguments": "{\"question\":\"what\"}"},
                {"type": "function_call", "call_id": "call_2", "name": "create_screen_image", "arguments": {"prompt":"draw"}},
                {"type": "function_call", "call_id": "", "name": "ignored", "arguments": "{}"}
              ]
            }
            """);

        Assert.Equal($"Line one{Environment.NewLine}Line two", response.Text);
        Assert.Equal(2, response.ToolCalls.Count);
        Assert.Equal(new AthenaTextToolCall("call_1", "inspect_screen", "{\"question\":\"what\"}"), response.ToolCalls[0]);
        Assert.Equal(new AthenaTextToolCall("call_2", "create_screen_image", """{"prompt":"draw"}"""), response.ToolCalls[1]);
    }

    [Fact]
    public void ReadApiErrorExtractsMessage()
    {
        var message = AthenaTextResponseParser.ReadApiError("""{"error":{"message":"bad request"}}""");

        Assert.Equal("bad request", message);
    }

    [Fact]
    public void ReadApiErrorFallsBackForInvalidJson()
    {
        var message = AthenaTextResponseParser.ReadApiError("not-json");

        Assert.Equal("OpenAI API error.", message);
    }
}

public sealed class RealtimeEventParserTests
{
    [Fact]
    public void IsFunctionCallEventDetectsFunctionCallItems()
    {
        using var document = JsonDocument.Parse("""{"item":{"type":"function_call"}}""");

        Assert.True(RealtimeEventParser.IsFunctionCallEvent(document.RootElement));
    }

    [Fact]
    public void TryReadFunctionCallFromItemEventReadsStringArguments()
    {
        using var document = JsonDocument.Parse(
            """{"item":{"type":"function_call","call_id":"call_1","name":"inspect_screen","arguments":"{\"question\":\"what\"}"}}""");

        var parsed = RealtimeEventParser.TryReadFunctionCallFromItemEvent(document.RootElement, out var call);

        Assert.True(parsed);
        Assert.Equal(new RealtimeFunctionCall("call_1", "inspect_screen", "{\"question\":\"what\"}"), call);
    }

    [Fact]
    public void TryReadFunctionCallFromPropertiesReadsObjectArguments()
    {
        using var document = JsonDocument.Parse(
            """{"call_id":"call_2","name":"create_screen_image","arguments":{"prompt":"draw"}}""");

        var parsed = RealtimeEventParser.TryReadFunctionCallFromProperties(document.RootElement, out var call);

        Assert.True(parsed);
        Assert.Equal(new RealtimeFunctionCall("call_2", "create_screen_image", """{"prompt":"draw"}"""), call);
    }

    [Theory]
    [InlineData("""{"item":{"type":"message","call_id":"call_1","name":"inspect_screen"}}""")]
    [InlineData("""{"item":{"type":"function_call","call_id":"","name":"inspect_screen"}}""")]
    [InlineData("""{"item":{"type":"function_call","call_id":"call_1"}}""")]
    public void TryReadFunctionCallFromItemEventRejectsIncompleteEvents(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.False(RealtimeEventParser.TryReadFunctionCallFromItemEvent(document.RootElement, out _));
    }

    [Fact]
    public void ReadErrorExtractsRealtimeErrorMessage()
    {
        using var document = JsonDocument.Parse("""{"error":{"message":"socket failed"}}""");

        Assert.Equal("socket failed", RealtimeEventParser.ReadError(document.RootElement));
    }

    [Fact]
    public void ReadErrorFallsBackWhenMessageIsMissing()
    {
        using var document = JsonDocument.Parse("""{"error":{}}""");

        Assert.Equal("Realtime API error.", RealtimeEventParser.ReadError(document.RootElement));
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
