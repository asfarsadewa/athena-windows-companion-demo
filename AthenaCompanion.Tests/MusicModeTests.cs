using System.Text.Json;
using AthenaCompanion.Music;
using AthenaCompanion.Settings;
using AthenaCompanion.Tools;
using NAudio.Wave;

namespace AthenaCompanion.Tests;

public sealed class MusicSettingsTests
{
    [Fact]
    public void DefaultMusicDirectoryUsesUserMusicFolder()
    {
        var settings = new AthenaSettings();

        Assert.Equal(MusicDirectoryDefaults.GetDefault(), settings.MusicDirectory);
        Assert.EndsWith(Path.Combine("Athena Companion"), settings.MusicDirectory);
    }

    [Fact]
    public void LoadFromPathNormalizesInvalidMusicDirectory()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, """{"musicDirectory":"relative-folder"}""");

        var settings = AthenaSettings.LoadFromPath(path);

        Assert.Equal(MusicDirectoryDefaults.GetDefault(), settings.MusicDirectory);
    }
}

public sealed class MusicLibraryTests
{
    [Fact]
    public void LoadScansSupportedFilesRecursivelyAndSortsByRelativePath()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "nested"));
        File.WriteAllText(Path.Combine(temp.Path, "b.m4a"), "not real audio");
        File.WriteAllText(Path.Combine(temp.Path, "nested", "a.mp3"), "not real audio");
        File.WriteAllText(Path.Combine(temp.Path, "ignored.wav"), "not real audio");

        var snapshot = MusicLibrary.Load(temp.Path);

        Assert.Equal(["b.m4a", Path.Combine("nested", "a.mp3")], snapshot.Tracks.Select(track => track.RelativePath));
    }

    [Fact]
    public void EmptyMessageTellsUserWhereToAddMusic()
    {
        using var temp = new TempDirectory();

        var snapshot = MusicLibrary.Load(temp.Path);

        Assert.True(snapshot.IsEmpty);
        Assert.Equal($"Add MP3 or M4A files to {temp.Path}.", MusicLibraryMessages.Empty(temp.Path));
    }

    [Fact]
    public void FindBestMatchUsesFilenameAndRelativePath()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Albums"));
        var expected = Path.Combine(temp.Path, "Albums", "night-drive.mp3");
        File.WriteAllText(expected, "not real audio");
        File.WriteAllText(Path.Combine(temp.Path, "other.m4a"), "not real audio");
        var snapshot = MusicLibrary.Load(temp.Path);

        Assert.Equal(expected, snapshot.FindBestMatch("night")?.FilePath);
        Assert.Equal(expected, snapshot.FindBestMatch("Albums")?.FilePath);
    }

    [Fact]
    public void FindBestMatchNormalizesSeparators()
    {
        using var temp = new TempDirectory();
        var expected = Path.Combine(temp.Path, "Onegai_Teacher_-_Shooting_Star.mp3");
        File.WriteAllText(expected, "not real audio");
        File.WriteAllText(Path.Combine(temp.Path, "Saishuheiki_Kanojo_-_Sayonara.mp3"), "not real audio");
        var snapshot = MusicLibrary.Load(temp.Path);

        Assert.Equal(expected, snapshot.FindBestMatch("shooting star")?.FilePath);
        Assert.Equal(expected, snapshot.FindBestMatch("shooting-star")?.FilePath);
        Assert.Equal(expected, snapshot.FindBestMatch("shooting_star")?.FilePath);
    }

    [Fact]
    public void FindBestMatchStripsCommonCommandWords()
    {
        using var temp = new TempDirectory();
        var expected = Path.Combine(temp.Path, "Onegai_Teacher_-_Shooting_Star.mp3");
        File.WriteAllText(expected, "not real audio");
        var snapshot = MusicLibrary.Load(temp.Path);

        Assert.Equal(expected, snapshot.FindBestMatch("please play shooting star song")?.FilePath);
    }

    [Fact]
    public void FindBestMatchUsesPathSegmentsAfterNormalization()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Anime-Radio"));
        var expected = Path.Combine(temp.Path, "Anime-Radio", "Shooting_Star.mp3");
        File.WriteAllText(expected, "not real audio");
        var snapshot = MusicLibrary.Load(temp.Path);

        Assert.Equal(expected, snapshot.FindBestMatch("anime radio shooting star")?.FilePath);
    }

    [Fact]
    public void FindBestMatchAllowsConservativeLongTokenTypo()
    {
        using var temp = new TempDirectory();
        var expected = Path.Combine(temp.Path, "Onegai_Teacher_-_Shooting_Star.mp3");
        File.WriteAllText(expected, "not real audio");
        var snapshot = MusicLibrary.Load(temp.Path);

        Assert.Equal(expected, snapshot.FindBestMatch("shootng star")?.FilePath);
    }

    [Fact]
    public void FindBestMatchDoesNotMatchUnrelatedShortQueries()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "Onegai_Teacher_-_Shooting_Star.mp3"), "not real audio");
        var snapshot = MusicLibrary.Load(temp.Path);

        Assert.Null(snapshot.FindBestMatch("zz"));
    }

    [Fact]
    public void GenericQuerySelectsFirstSortedTrack()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "z.m4a"), "not real audio");
        File.WriteAllText(Path.Combine(temp.Path, "a.mp3"), "not real audio");
        var snapshot = MusicLibrary.Load(temp.Path);

        Assert.Equal("a.mp3", snapshot.FindBestMatch("play music")?.RelativePath);
    }
}

public sealed class MusicToolTests
{
    [Fact]
    public void StrictToolDefinitionsRequireMusicArguments()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(AthenaToolDefinitions.Create(strict: true)));
        var music = FindTool(document.RootElement, "open_music_player");
        var parameters = music.GetProperty("parameters");

        Assert.True(music.GetProperty("strict").GetBoolean());
        Assert.False(parameters.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["query", "autoplay"], parameters.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void RealtimeToolDefinitionsOmitStrictFlagsForMusicTool()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(AthenaToolDefinitions.Create(strict: false)));
        var music = FindTool(document.RootElement, "open_music_player");

        Assert.False(music.TryGetProperty("strict", out _));
        Assert.False(music.GetProperty("parameters").TryGetProperty("additionalProperties", out _));
    }

    [Fact]
    public async Task MusicToolDoesNotRequireOpenAiKeyAndSuppressesVoiceFollowup()
    {
        MusicPlayerRequest? captured = null;
        var executor = new AthenaToolExecutor(
            () => null,
            _ => { },
            _ => { },
            request => captured = request);

        var result = await executor.ExecuteAsync(
            "open_music_player",
            """{"query":"night","autoplay":true}""",
            CancellationToken.None);

        Assert.True(result.StopVoice);
        Assert.False(result.ContinueVoiceResponse);
        Assert.Equal(new MusicPlayerRequest("night", Autoplay: true), captured);
    }

    private static JsonElement FindTool(JsonElement root, string name) =>
        root.EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == name);
}

public sealed class ToolArgumentReaderBoolTests
{
    [Fact]
    public void ReadBoolArgumentReturnsRequestedBoolean()
    {
        Assert.True(ToolArgumentReader.ReadBoolArgument("""{"autoplay":true}""", "autoplay"));
        Assert.False(ToolArgumentReader.ReadBoolArgument("""{"autoplay":false}""", "autoplay"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("""{"autoplay":"true"}""")]
    [InlineData("""{"other":true}""")]
    public void ReadBoolArgumentReturnsNullWhenUnavailable(string json)
    {
        Assert.Null(ToolArgumentReader.ReadBoolArgument(json, "autoplay"));
    }
}

public sealed class RadioEffectSampleProviderTests
{
    [Fact]
    public void OutputIsBoundedMonoRadioFormat()
    {
        var provider = new RadioEffectSampleProvider(new TestSampleProvider(sampleRate: 48000, channels: 2), new Random(7));
        var buffer = new float[1024];

        var read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(1, provider.WaveFormat.Channels);
        Assert.Equal(RadioEffectSampleProvider.OutputSampleRate, provider.WaveFormat.SampleRate);
        Assert.True(read > 0);
        Assert.All(buffer.Take(read), sample => Assert.InRange(sample, -1f, 1f));
    }

    [Fact]
    public void SameSeedProducesDeterministicOutput()
    {
        var first = new RadioEffectSampleProvider(new TestSampleProvider(sampleRate: 24000, channels: 1), new Random(123));
        var second = new RadioEffectSampleProvider(new TestSampleProvider(sampleRate: 24000, channels: 1), new Random(123));
        var firstBuffer = new float[512];
        var secondBuffer = new float[512];

        var firstRead = first.Read(firstBuffer, 0, firstBuffer.Length);
        var secondRead = second.Read(secondBuffer, 0, secondBuffer.Length);

        Assert.Equal(firstRead, secondRead);
        Assert.Equal(firstBuffer.Take(firstRead), secondBuffer.Take(secondRead));
    }

    private sealed class TestSampleProvider : ISampleProvider
    {
        private int _position;

        public TestSampleProvider(int sampleRate, int channels)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                buffer[offset + i] = (float)Math.Sin((_position + i) * 0.031) * 0.75f;
            }

            _position += count;
            return count;
        }
    }
}
