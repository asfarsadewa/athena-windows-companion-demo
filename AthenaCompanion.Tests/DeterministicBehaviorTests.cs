using System.Text.Json;
using AthenaCompanion;
using AthenaCompanion.Settings;
using AthenaCompanion.TextChat;
using AthenaCompanion.Tools;
using AthenaCompanion.UI;
using AthenaCompanion.Voice;
using NAudio.Wave;

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
        var inputAudio = session.GetProperty("audio").GetProperty("input");
        Assert.Equal("audio/pcm", inputAudio.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal(AthenaAudioInput.SampleRate, inputAudio.GetProperty("format").GetProperty("rate").GetInt32());
        Assert.Equal("far_field", inputAudio.GetProperty("noise_reduction").GetProperty("type").GetString());
        Assert.Equal("server_vad", inputAudio.GetProperty("turn_detection").GetProperty("type").GetString());
        var outputAudio = session.GetProperty("audio").GetProperty("output");
        Assert.Equal("audio/pcm", outputAudio.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal(AthenaAudioInput.SampleRate, outputAudio.GetProperty("format").GetProperty("rate").GetInt32());
        Assert.Equal("marin", outputAudio.GetProperty("voice").GetString());
        Assert.Equal("auto", session.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task ResponseDoneBeforePlaybackDrainKeepsSpeaking()
    {
        var audioOutput = new FakeAudioOutput();
        var session = CreateRealtimeSession(audioOutput);
        var statuses = new List<string>();
        session.StatusChanged += (_, status) => statuses.Add(status);

        await session.HandleServerEventAsync(AudioDeltaJson([1, 2]), CancellationToken.None);
        await session.HandleServerEventAsync("""{"type":"response.done","response":{"status":"completed"}}""", CancellationToken.None);

        Assert.Equal("Speaking", statuses.Last());
        Assert.Equal(1, audioOutput.FinishResponseCount);

        audioOutput.Drain();

        Assert.Equal("Listening", statuses.Last());
    }

    [Fact]
    public async Task OutputAudioDoneAloneDoesNotReturnToListening()
    {
        var audioOutput = new FakeAudioOutput();
        var session = CreateRealtimeSession(audioOutput);
        var statuses = new List<string>();
        session.StatusChanged += (_, status) => statuses.Add(status);

        await session.HandleServerEventAsync(AudioDeltaJson([1, 2]), CancellationToken.None);
        statuses.Clear();
        await session.HandleServerEventAsync("""{"type":"response.output_audio.done"}""", CancellationToken.None);

        Assert.Equal(1, audioOutput.FinishResponseCount);
        Assert.DoesNotContain("Listening", statuses);
    }

    [Fact]
    public async Task SpeechStartedDuringPlaybackDoesNotClearResponseTail()
    {
        var audioOutput = new FakeAudioOutput();
        var session = CreateRealtimeSession(audioOutput);

        await session.HandleServerEventAsync(AudioDeltaJson([1, 2]), CancellationToken.None);
        await session.HandleServerEventAsync("""{"type":"input_audio_buffer.speech_started"}""", CancellationToken.None);

        Assert.Equal(0, audioOutput.ClearCount);
        Assert.Equal(1, audioOutput.BeginResponseCount);
    }

    [Fact]
    public async Task CancelledResponseClearsOutputAndReturnsToListening()
    {
        var audioOutput = new FakeAudioOutput();
        var session = CreateRealtimeSession(audioOutput);
        var statuses = new List<string>();
        session.StatusChanged += (_, status) => statuses.Add(status);

        await session.HandleServerEventAsync(AudioDeltaJson([1, 2]), CancellationToken.None);
        await session.HandleServerEventAsync("""{"type":"response.done","response":{"status":"cancelled"}}""", CancellationToken.None);

        Assert.Equal(1, audioOutput.ClearCount);
        Assert.Equal("Listening", statuses.Last());
    }

    [Fact]
    public async Task FailedResponseClearsOutputAndRaisesError()
    {
        var audioOutput = new FakeAudioOutput();
        var session = CreateRealtimeSession(audioOutput);
        var errors = new List<string>();
        session.Error += (_, error) => errors.Add(error);

        await session.HandleServerEventAsync(AudioDeltaJson([1, 2]), CancellationToken.None);
        await session.HandleServerEventAsync(
            """{"type":"response.done","response":{"status":"failed","status_details":{"error":{"message":"audio failed"}}}}""",
            CancellationToken.None);

        Assert.Equal(1, audioOutput.ClearCount);
        Assert.Equal("audio failed", errors.Single());
    }

    private static AthenaRealtimeSession CreateRealtimeSession(IAthenaAudioOutput audioOutput) =>
        new(
            "key",
            "instructions",
            "marin",
            new AthenaToolExecutor(() => "key", _ => { }, _ => { }),
            new AthenaAudioInput(),
            audioOutput);

    private static string AudioDeltaJson(byte[] audio) =>
        $$"""{"type":"response.output_audio.delta","delta":"{{Convert.ToBase64String(audio)}}"}""";
}

public sealed class AthenaAudioInputTests
{
    [Fact]
    public void CreatePcm16ProviderDownmixesAndResamplesToRealtimeInputFormat()
    {
        var sourceFormat = new WaveFormat(48000, 16, 2);
        var sourceAudio = new byte[sourceFormat.AverageBytesPerSecond / 10];
        using var sourceStream = new RawSourceWaveStream(sourceAudio, 0, sourceAudio.Length, sourceFormat);

        var provider = AthenaAudioInput.CreatePcm16Provider(sourceStream);
        var output = new byte[AthenaAudioInput.SampleRate * 2 / 5];
        var bytesRead = provider.Read(output, 0, output.Length);

        Assert.Equal(AthenaAudioInput.SampleRate, provider.WaveFormat.SampleRate);
        Assert.Equal(1, provider.WaveFormat.Channels);
        Assert.Equal(16, provider.WaveFormat.BitsPerSample);
        Assert.True(bytesRead > 0);
        Assert.Equal(0, bytesRead % 2);
    }
}

public sealed class AthenaAudioOutputTests
{
    [Fact]
    public void QueuedProviderPreservesMoreThanEightSecondsOfPcm()
    {
        var provider = CreateResponseQueue();
        var audio = Enumerable.Range(0, AthenaAudioInput.SampleRate * 2 * 9)
            .Select(index => (byte)(index % 251))
            .ToArray();

        provider.BeginResponse();
        var accepted = provider.AddPcm16(audio);
        var actual = new byte[audio.Length];
        var read = provider.Read(actual, 0, actual.Length);

        Assert.True(accepted);
        Assert.Equal(audio.Length, read);
        Assert.Equal(audio, actual);
        Assert.InRange(
            Math.Abs((provider.SnapshotStats().MaxQueuedDuration - TimeSpan.FromSeconds(9)).TotalMilliseconds),
            0,
            1);
    }

    [Fact]
    public void QueuedProviderCarriesOddByteAcrossDeltas()
    {
        var provider = CreateResponseQueue();
        provider.BeginResponse();

        Assert.True(provider.AddPcm16([1, 2, 3]));
        Assert.True(provider.AddPcm16([4, 5]));
        Assert.True(provider.AddPcm16([6]));
        var actual = new byte[6];
        var read = provider.Read(actual, 0, actual.Length);

        Assert.Equal(6, read);
        Assert.Equal([1, 2, 3, 4, 5, 6], actual);
    }

    [Fact]
    public void QueuedProviderZeroFillsUnderrunWithoutCorruptingLaterAudio()
    {
        var provider = CreateResponseQueue();
        provider.BeginResponse();
        Assert.True(provider.AddPcm16([1, 2]));

        var firstRead = new byte[4];
        provider.Read(firstRead, 0, firstRead.Length);
        Assert.Equal([1, 2, 0, 0], firstRead);

        Assert.True(provider.AddPcm16([3, 4]));
        var secondRead = new byte[2];
        provider.Read(secondRead, 0, secondRead.Length);

        Assert.Equal([3, 4], secondRead);
        Assert.Equal(1, provider.SnapshotStats().UnderrunCount);
    }

    [Fact]
    public void QueuedProviderRejectsOnlyWhenMaxDurationIsExceeded()
    {
        var provider = new QueuedPcm16WaveProvider(
            new WaveFormat(AthenaAudioInput.SampleRate, 16, 1),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(1));
        var maxAudio = new byte[AthenaAudioInput.SampleRate * 2 / 100];

        provider.BeginResponse();
        Assert.True(provider.AddPcm16(maxAudio));
        Assert.False(provider.AddPcm16([1, 2]));

        var stats = provider.SnapshotStats();
        Assert.Equal(1, stats.RejectedOverflowCount);
        Assert.Equal(TimeSpan.Zero, stats.QueuedDuration);
    }

    [Fact]
    public void CreatePlaybackProviderResamplesAndExpandsToRenderFormat()
    {
        var sourceFormat = new WaveFormat(AthenaAudioInput.SampleRate, 16, 1);
        var sourceAudio = new byte[sourceFormat.AverageBytesPerSecond / 10];
        using var sourceStream = new RawSourceWaveStream(sourceAudio, 0, sourceAudio.Length, sourceFormat);
        var renderFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        var provider = AthenaAudioOutput.CreatePlaybackProvider(sourceStream, renderFormat);

        Assert.Equal(48000, provider.WaveFormat.SampleRate);
        Assert.Equal(2, provider.WaveFormat.Channels);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, provider.WaveFormat.Encoding);
    }

    private static QueuedPcm16WaveProvider CreateResponseQueue() =>
        new(
            new WaveFormat(AthenaAudioInput.SampleRate, 16, 1),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMilliseconds(150));
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

internal sealed class FakeAudioOutput : IAthenaAudioOutput
{
    public event EventHandler? PlaybackDrained;

    public TimeSpan QueuedDuration { get; private set; } = TimeSpan.Zero;

    public int BeginResponseCount { get; private set; }

    public int FinishResponseCount { get; private set; }

    public int ClearCount { get; private set; }

    public bool AcceptAudio { get; set; } = true;

    public void Start()
    {
    }

    public void BeginResponse()
    {
        BeginResponseCount++;
        QueuedDuration = TimeSpan.FromSeconds(1);
    }

    public void FinishResponse()
    {
        FinishResponseCount++;
    }

    public bool AddPcm16(byte[] audio) => AcceptAudio;

    public void Clear()
    {
        ClearCount++;
        QueuedDuration = TimeSpan.Zero;
    }

    public AthenaAudioOutputStats SnapshotStats() =>
        new(
            TotalBytesAccepted: 0,
            QueuedDuration,
            MaxQueuedDuration: TimeSpan.FromSeconds(120),
            UnderrunCount: 0,
            RejectedOverflowCount: AcceptAudio ? 0 : 1);

    public void Drain()
    {
        QueuedDuration = TimeSpan.Zero;
        PlaybackDrained?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
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
