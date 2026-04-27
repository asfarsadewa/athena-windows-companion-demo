using NAudio.Wave;

namespace AthenaCompanion.Voice;

internal sealed class AthenaAudioInput : IDisposable
{
    public const int SampleRate = 24000;
    private WaveInEvent? _waveIn;

    public event EventHandler<byte[]>? AudioAvailable;

    public void Start()
    {
        if (_waveIn is not null)
        {
            return;
        }

        _waveIn = new WaveInEvent
        {
            DeviceNumber = 0,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 100,
            NumberOfBuffers = 3
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
    }

    public void Stop()
    {
        if (_waveIn is null)
        {
            return;
        }

        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;
    }

    public void Dispose() => Stop();

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
        {
            return;
        }

        var buffer = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
        AudioAvailable?.Invoke(this, buffer);
    }
}
