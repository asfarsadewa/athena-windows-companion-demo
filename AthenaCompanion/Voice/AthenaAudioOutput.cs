using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AthenaCompanion.Voice;

internal sealed class AthenaAudioOutput : IDisposable
{
    private const int PlaybackLatencyMilliseconds = 100;
    private readonly object _sync = new();
    private readonly WasapiOut _waveOut;
    private readonly BufferedWaveProvider _buffer;
    private byte? _pendingPcmByte;

    public AthenaAudioOutput()
    {
        _buffer = new BufferedWaveProvider(new WaveFormat(AthenaAudioInput.SampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromSeconds(8),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

        var outputDevice = CreatePreferredRenderDevice();
        var playbackFormat = GetPlaybackFormat(outputDevice);
        _waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, PlaybackLatencyMilliseconds);
        _waveOut.Init(CreatePlaybackProvider(_buffer, playbackFormat));
    }

    public void Start()
    {
        if (_waveOut.PlaybackState != PlaybackState.Playing)
        {
            _waveOut.Play();
        }
    }

    public void AddPcm16(byte[] audio)
    {
        if (audio.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            _pendingPcmByte = AddAlignedPcm16(_buffer, audio, _pendingPcmByte);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _pendingPcmByte = null;
            _buffer.ClearBuffer();
        }
    }

    public void Dispose()
    {
        _waveOut.Stop();
        _waveOut.Dispose();
    }

    internal static IWaveProvider CreatePlaybackProvider(IWaveProvider source, WaveFormat outputFormat)
    {
        ISampleProvider sampleProvider = source.ToSampleProvider();
        if (sampleProvider.WaveFormat.SampleRate != outputFormat.SampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, outputFormat.SampleRate);
        }

        if (sampleProvider.WaveFormat.Channels != outputFormat.Channels)
        {
            sampleProvider = new MonoToChannelsSampleProvider(sampleProvider, outputFormat.Channels);
        }

        return outputFormat.Encoding switch
        {
            WaveFormatEncoding.Pcm when outputFormat.BitsPerSample == 16 => new SampleToWaveProvider16(sampleProvider),
            WaveFormatEncoding.Pcm when outputFormat.BitsPerSample == 24 => new SampleToWaveProvider24(sampleProvider),
            _ => new SampleToWaveProvider(sampleProvider)
        };
    }

    internal static byte? AddAlignedPcm16(BufferedWaveProvider buffer, byte[] audio, byte? pendingPcmByte)
    {
        var offset = 0;
        if (pendingPcmByte is byte pending)
        {
            var completedSample = new[] { pending, audio[0] };
            buffer.AddSamples(completedSample, 0, completedSample.Length);
            pendingPcmByte = null;
            offset = 1;
        }

        var available = audio.Length - offset;
        var evenLength = available - available % 2;
        if (evenLength > 0)
        {
            buffer.AddSamples(audio, offset, evenLength);
        }

        if (evenLength != available)
        {
            pendingPcmByte = audio[^1];
        }

        return pendingPcmByte;
    }

    private static MMDevice CreatePreferredRenderDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private static WaveFormat GetPlaybackFormat(MMDevice outputDevice)
    {
        using var audioClient = outputDevice.AudioClient;
        return NormalizeWaveFormat(audioClient.MixFormat);
    }

    private static WaveFormat NormalizeWaveFormat(WaveFormat waveFormat) =>
        waveFormat is WaveFormatExtensible extensible
            ? extensible.ToStandardWaveFormat()
            : waveFormat;
}

internal sealed class MonoToChannelsSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _monoBuffer = [];

    public MonoToChannelsSampleProvider(ISampleProvider source, int channels)
    {
        if (source.WaveFormat.Channels != 1)
        {
            throw new ArgumentException("Only mono input can be expanded to multiple output channels.", nameof(source));
        }

        _source = source;
        _channels = Math.Max(1, channels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, _channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var framesRequested = count / _channels;
        if (framesRequested <= 0)
        {
            return 0;
        }

        if (_monoBuffer.Length < framesRequested)
        {
            _monoBuffer = new float[framesRequested];
        }

        var framesRead = _source.Read(_monoBuffer, 0, framesRequested);
        for (var frame = 0; frame < framesRead; frame++)
        {
            var sample = _monoBuffer[frame];
            var outputOffset = offset + frame * _channels;
            for (var channel = 0; channel < _channels; channel++)
            {
                buffer[outputOffset + channel] = sample;
            }
        }

        return framesRead * _channels;
    }
}
