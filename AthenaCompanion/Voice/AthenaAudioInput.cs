using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AthenaCompanion.Voice;

internal sealed class AthenaAudioInput : IDisposable
{
    public const int SampleRate = 24000;
    private const int CaptureBufferMilliseconds = 50;
    private const int MaxDrainReadsPerCaptureBuffer = 8;
    private readonly object _sync = new();
    private WasapiCapture? _capture;
    private BufferedWaveProvider? _sourceBuffer;
    private IWaveProvider? _pcm16Provider;
    private byte[] _conversionBuffer = [];

    public event EventHandler<byte[]>? AudioAvailable;

    public void Start()
    {
        if (_capture is not null)
        {
            return;
        }

        var capture = new WasapiCapture(CreatePreferredCaptureDevice(), useEventSync: true, CaptureBufferMilliseconds);
        var sourceBuffer = new BufferedWaveProvider(NormalizeWaveFormat(capture.WaveFormat))
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            DiscardOnBufferOverflow = true,
            ReadFully = false
        };

        lock (_sync)
        {
            _sourceBuffer = sourceBuffer;
            _pcm16Provider = CreatePcm16Provider(sourceBuffer);
            _conversionBuffer = new byte[SampleRate * 2 * CaptureBufferMilliseconds / 1000 * 4];
        }

        _capture = capture;
        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (_capture is null)
        {
            return;
        }

        var capture = _capture;
        _capture = null;
        capture.DataAvailable -= OnDataAvailable;
        capture.StopRecording();
        capture.Dispose();

        lock (_sync)
        {
            _sourceBuffer = null;
            _pcm16Provider = null;
            _conversionBuffer = [];
        }
    }

    public void Dispose() => Stop();

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
        {
            return;
        }

        List<byte[]> chunks;
        lock (_sync)
        {
            if (_sourceBuffer is null || _pcm16Provider is null)
            {
                return;
            }

            _sourceBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            chunks = DrainConvertedAudio(_pcm16Provider);
        }

        foreach (var chunk in chunks)
        {
            AudioAvailable?.Invoke(this, chunk);
        }
    }

    private List<byte[]> DrainConvertedAudio(IWaveProvider pcm16Provider)
    {
        var chunks = new List<byte[]>();
        for (var i = 0; i < MaxDrainReadsPerCaptureBuffer; i++)
        {
            var bytesRead = pcm16Provider.Read(_conversionBuffer, 0, _conversionBuffer.Length);
            if (bytesRead <= 0)
            {
                break;
            }

            var chunk = new byte[bytesRead];
            Buffer.BlockCopy(_conversionBuffer, 0, chunk, 0, bytesRead);
            chunks.Add(chunk);

            if (bytesRead < _conversionBuffer.Length)
            {
                break;
            }
        }

        return chunks;
    }

    internal static IWaveProvider CreatePcm16Provider(IWaveProvider source)
    {
        ISampleProvider sampleProvider = source.ToSampleProvider();
        if (sampleProvider.WaveFormat.Channels != 1)
        {
            sampleProvider = new MonoDownmixSampleProvider(sampleProvider);
        }

        if (sampleProvider.WaveFormat.SampleRate != SampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, SampleRate);
        }

        return new SampleToWaveProvider16(sampleProvider);
    }

    private static MMDevice CreatePreferredCaptureDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            return WasapiCapture.GetDefaultCaptureDevice();
        }
    }

    private static WaveFormat NormalizeWaveFormat(WaveFormat waveFormat) =>
        waveFormat is WaveFormatExtensible extensible
            ? extensible.ToStandardWaveFormat()
            : waveFormat;
}

internal sealed class MonoDownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _inputBuffer = [];

    public MonoDownmixSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var required = count * _channels;
        if (_inputBuffer.Length < required)
        {
            _inputBuffer = new float[required];
        }

        var read = _source.Read(_inputBuffer, 0, required);
        var frames = read / _channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            var inputOffset = frame * _channels;
            for (var channel = 0; channel < _channels; channel++)
            {
                sum += _inputBuffer[inputOffset + channel];
            }

            buffer[offset + frame] = sum / _channels;
        }

        return frames;
    }
}
