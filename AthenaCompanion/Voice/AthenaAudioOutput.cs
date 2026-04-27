using NAudio.Wave;

namespace AthenaCompanion.Voice;

internal sealed class AthenaAudioOutput : IDisposable
{
    private readonly WaveOutEvent _waveOut;
    private readonly BufferedWaveProvider _buffer;

    public AthenaAudioOutput()
    {
        _buffer = new BufferedWaveProvider(new WaveFormat(AthenaAudioInput.SampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromSeconds(8),
            DiscardOnBufferOverflow = true
        };

        _waveOut = new WaveOutEvent();
        _waveOut.Init(_buffer);
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

        _buffer.AddSamples(audio, 0, audio.Length);
    }

    public void Clear() => _buffer.ClearBuffer();

    public void Dispose()
    {
        _waveOut.Stop();
        _waveOut.Dispose();
    }
}
