using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AthenaCompanion.Voice;

internal interface IAthenaAudioOutput : IDisposable
{
    event EventHandler? PlaybackDrained;

    TimeSpan QueuedDuration { get; }

    void Start();

    void BeginResponse();

    void FinishResponse();

    bool AddPcm16(byte[] audio);

    void Clear();

    AthenaAudioOutputStats SnapshotStats();
}

internal sealed record AthenaAudioOutputStats(
    long TotalBytesAccepted,
    TimeSpan QueuedDuration,
    TimeSpan MaxQueuedDuration,
    int UnderrunCount,
    int RejectedOverflowCount);

internal sealed class AthenaAudioOutput : IAthenaAudioOutput
{
    private const int PlaybackLatencyMilliseconds = 100;
    private static readonly TimeSpan MaxQueuedAudioDuration = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan PlaybackDrainThreshold = TimeSpan.FromMilliseconds(150);
    private readonly WasapiOut _waveOut;
    private readonly QueuedPcm16WaveProvider _queue;

    public AthenaAudioOutput()
    {
        _queue = new QueuedPcm16WaveProvider(
            new WaveFormat(AthenaAudioInput.SampleRate, 16, 1),
            MaxQueuedAudioDuration,
            PlaybackDrainThreshold);
        _queue.PlaybackDrained += OnPlaybackDrained;

        var outputDevice = CreatePreferredRenderDevice();
        var playbackFormat = GetPlaybackFormat(outputDevice);
        _waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, PlaybackLatencyMilliseconds);
        _waveOut.Init(CreatePlaybackProvider(_queue, playbackFormat));
    }

    public event EventHandler? PlaybackDrained;

    public TimeSpan QueuedDuration => _queue.QueuedDuration;

    public void Start()
    {
        if (_waveOut.PlaybackState != PlaybackState.Playing)
        {
            _waveOut.Play();
        }
    }

    public void BeginResponse() => _queue.BeginResponse();

    public void FinishResponse() => _queue.FinishResponse();

    public bool AddPcm16(byte[] audio)
    {
        if (audio.Length == 0)
        {
            return true;
        }

        var accepted = _queue.AddPcm16(audio);
        if (!accepted)
        {
            Debug.WriteLine($"Athena response audio overflow: {SnapshotStats()}");
        }

        return accepted;
    }

    public void Clear() => _queue.Clear();

    public AthenaAudioOutputStats SnapshotStats() => _queue.SnapshotStats();

    public void Dispose()
    {
        _queue.PlaybackDrained -= OnPlaybackDrained;
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

    private void OnPlaybackDrained(object? sender, EventArgs e)
    {
        Debug.WriteLine($"Athena response audio drained: {SnapshotStats()}");
        PlaybackDrained?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class QueuedPcm16WaveProvider : IWaveProvider
{
    private readonly object _sync = new();
    private readonly Queue<byte[]> _chunks = new();
    private readonly long _maxQueuedBytes;
    private readonly long _drainThresholdBytes;
    private int _headOffset;
    private long _queuedBytes;
    private long _totalBytesAccepted;
    private long _maxQueuedBytesSeen;
    private int _underrunCount;
    private int _rejectedOverflowCount;
    private byte? _pendingPcmByte;
    private bool _responseActive;
    private bool _finishRequested;
    private bool _drainReported;

    public QueuedPcm16WaveProvider(WaveFormat waveFormat, TimeSpan maxQueuedAudioDuration, TimeSpan playbackDrainThreshold)
    {
        if (waveFormat.Encoding != WaveFormatEncoding.Pcm || waveFormat.BitsPerSample != 16 || waveFormat.Channels != 1)
        {
            throw new ArgumentException("The response queue only supports mono PCM16 input.", nameof(waveFormat));
        }

        WaveFormat = waveFormat;
        _maxQueuedBytes = DurationToAlignedBytes(maxQueuedAudioDuration);
        _drainThresholdBytes = DurationToAlignedBytes(playbackDrainThreshold);
    }

    public event EventHandler? PlaybackDrained;

    public WaveFormat WaveFormat { get; }

    public TimeSpan QueuedDuration
    {
        get
        {
            lock (_sync)
            {
                return BytesToDuration(_queuedBytes);
            }
        }
    }

    public void BeginResponse()
    {
        lock (_sync)
        {
            ClearQueuedLocked();
            _totalBytesAccepted = 0;
            _maxQueuedBytesSeen = 0;
            _underrunCount = 0;
            _rejectedOverflowCount = 0;
            _responseActive = true;
            _finishRequested = false;
            _drainReported = false;
        }
    }

    public void FinishResponse()
    {
        var shouldNotify = false;
        lock (_sync)
        {
            if (!_responseActive || _finishRequested)
            {
                return;
            }

            _finishRequested = true;
            shouldNotify = MarkDrainedIfReadyLocked();
        }

        NotifyPlaybackDrained(shouldNotify);
    }

    public bool AddPcm16(byte[] audio)
    {
        if (audio.Length == 0)
        {
            return true;
        }

        lock (_sync)
        {
            var offset = 0;
            var completedSampleLength = _pendingPcmByte is null ? 0 : WaveFormat.BlockAlign;
            if (_pendingPcmByte is not null)
            {
                offset = 1;
            }

            var available = audio.Length - offset;
            var evenLength = available - available % WaveFormat.BlockAlign;
            var bytesToQueue = completedSampleLength + evenLength;
            if (_queuedBytes + bytesToQueue > _maxQueuedBytes)
            {
                _rejectedOverflowCount++;
                ClearQueuedLocked();
                _responseActive = false;
                _finishRequested = false;
                _drainReported = false;
                return false;
            }

            if (_pendingPcmByte is byte pending)
            {
                EnqueueLocked([pending, audio[0]]);
                _pendingPcmByte = null;
            }

            if (evenLength > 0)
            {
                var chunk = new byte[evenLength];
                Buffer.BlockCopy(audio, offset, chunk, 0, evenLength);
                EnqueueLocked(chunk);
            }

            if (evenLength != available)
            {
                _pendingPcmByte = audio[^1];
            }

            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            ClearQueuedLocked();
            _responseActive = false;
            _finishRequested = false;
            _drainReported = true;
        }
    }

    public AthenaAudioOutputStats SnapshotStats()
    {
        lock (_sync)
        {
            return new AthenaAudioOutputStats(
                _totalBytesAccepted,
                BytesToDuration(_queuedBytes),
                BytesToDuration(_maxQueuedBytesSeen),
                _underrunCount,
                _rejectedOverflowCount);
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var remaining = count;
        var writeOffset = offset;
        var shouldNotify = false;

        lock (_sync)
        {
            while (remaining > 0 && _chunks.Count > 0)
            {
                var chunk = _chunks.Peek();
                var available = chunk.Length - _headOffset;
                var toCopy = Math.Min(available, remaining);
                Buffer.BlockCopy(chunk, _headOffset, buffer, writeOffset, toCopy);

                _headOffset += toCopy;
                writeOffset += toCopy;
                remaining -= toCopy;
                _queuedBytes -= toCopy;

                if (_headOffset == chunk.Length)
                {
                    _chunks.Dequeue();
                    _headOffset = 0;
                }
            }

            if (remaining > 0)
            {
                Array.Clear(buffer, writeOffset, remaining);
                if (_responseActive && !_drainReported)
                {
                    _underrunCount++;
                }
            }

            shouldNotify = MarkDrainedIfReadyLocked();
        }

        NotifyPlaybackDrained(shouldNotify);
        return count;
    }

    private void EnqueueLocked(byte[] chunk)
    {
        _chunks.Enqueue(chunk);
        _queuedBytes += chunk.Length;
        _totalBytesAccepted += chunk.Length;
        _maxQueuedBytesSeen = Math.Max(_maxQueuedBytesSeen, _queuedBytes);
    }

    private void ClearQueuedLocked()
    {
        _chunks.Clear();
        _headOffset = 0;
        _queuedBytes = 0;
        _pendingPcmByte = null;
    }

    private bool MarkDrainedIfReadyLocked()
    {
        if (!_responseActive || !_finishRequested || _drainReported || _queuedBytes > _drainThresholdBytes)
        {
            return false;
        }

        _drainReported = true;
        _responseActive = false;
        return true;
    }

    private void NotifyPlaybackDrained(bool shouldNotify)
    {
        if (shouldNotify)
        {
            PlaybackDrained?.Invoke(this, EventArgs.Empty);
        }
    }

    private long DurationToAlignedBytes(TimeSpan duration)
    {
        var bytes = Math.Max(WaveFormat.BlockAlign, (long)Math.Ceiling(duration.TotalSeconds * WaveFormat.AverageBytesPerSecond));
        return bytes - bytes % WaveFormat.BlockAlign;
    }

    private TimeSpan BytesToDuration(long bytes) =>
        TimeSpan.FromSeconds((double)bytes / WaveFormat.AverageBytesPerSecond);
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
