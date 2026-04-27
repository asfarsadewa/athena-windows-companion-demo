using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AthenaCompanion.Music;

internal sealed class MusicPlaybackEngine : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public event EventHandler? PlaybackStopped;

    public bool HasTrack => _reader is not null;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;

    public void Play(string filePath)
    {
        CloseCurrent();

        _reader = new AudioFileReader(filePath);
        var radio = new RadioEffectSampleProvider(_reader);
        _output = new WaveOutEvent();
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(new SampleToWaveProvider16(radio));
        _output.Play();
    }

    public void Resume()
    {
        if (_output?.PlaybackState == PlaybackState.Paused)
        {
            _output.Play();
        }
    }

    public void Pause()
    {
        if (_output?.PlaybackState == PlaybackState.Playing)
        {
            _output.Pause();
        }
    }

    public void Stop() => CloseCurrent();

    public void Seek(TimeSpan position)
    {
        if (_reader is null)
        {
            return;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }
        else if (position > _reader.TotalTime)
        {
            position = _reader.TotalTime;
        }

        _reader.CurrentTime = position;
    }

    public void Dispose() => CloseCurrent();

    private void CloseCurrent()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        _reader?.Dispose();
        _reader = null;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
}
