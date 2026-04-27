using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AthenaCompanion.Music;

internal sealed class RadioEffectSampleProvider : ISampleProvider
{
    public const int OutputSampleRate = 24000;
    private readonly ISampleProvider _source;
    private readonly Random _random;
    private readonly float _lowPassAlpha;
    private readonly float _highPassAlpha;
    private float _lowPass;
    private float _highPass;
    private float _previousHighPassInput;
    private double _instabilityPhase;
    private int _crackleRemaining;
    private float _crackleAmplitude;

    public RadioEffectSampleProvider(ISampleProvider source, Random? random = null)
    {
        _source = BuildSource(source);
        _random = random ?? new Random();
        WaveFormat = _source.WaveFormat;
        _lowPassAlpha = LowPassAlpha(3100, WaveFormat.SampleRate);
        _highPassAlpha = HighPassAlpha(170, WaveFormat.SampleRate);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        for (var i = offset; i < offset + read; i++)
        {
            var sample = buffer[i];

            _lowPass += _lowPassAlpha * (sample - _lowPass);
            _highPass = _highPassAlpha * (_highPass + _lowPass - _previousHighPassInput);
            _previousHighPassInput = _lowPass;

            var shaped = _highPass * 1.45f;
            shaped += (float)((_random.NextDouble() * 2.0 - 1.0) * 0.018);

            if (_crackleRemaining <= 0 && _random.NextDouble() < 0.0018)
            {
                _crackleRemaining = 1 + _random.Next(4);
                _crackleAmplitude = (float)((_random.NextDouble() * 2.0 - 1.0) * 0.55);
            }

            if (_crackleRemaining > 0)
            {
                shaped += _crackleAmplitude;
                _crackleAmplitude *= 0.42f;
                _crackleRemaining--;
            }

            _instabilityPhase += (Math.PI * 2.0 * 0.19) / WaveFormat.SampleRate;
            if (_instabilityPhase > Math.PI * 2.0)
            {
                _instabilityPhase -= Math.PI * 2.0;
            }

            var gain = 0.78f + (float)Math.Sin(_instabilityPhase) * 0.045f;
            shaped *= gain;
            shaped = MathF.Tanh(shaped * 1.9f) * 0.82f;
            buffer[i] = Math.Clamp(shaped, -1f, 1f);
        }

        return read;
    }

    private static ISampleProvider BuildSource(ISampleProvider source)
    {
        ISampleProvider mono = source.WaveFormat.Channels == 1
            ? source
            : new MonoDownmixSampleProvider(source);

        return mono.WaveFormat.SampleRate == OutputSampleRate
            ? mono
            : new WdlResamplingSampleProvider(mono, OutputSampleRate);
    }

    private static float LowPassAlpha(double cutoff, double sampleRate)
    {
        var rc = 1.0 / (2.0 * Math.PI * cutoff);
        var dt = 1.0 / sampleRate;
        return (float)(dt / (rc + dt));
    }

    private static float HighPassAlpha(double cutoff, double sampleRate)
    {
        var rc = 1.0 / (2.0 * Math.PI * cutoff);
        var dt = 1.0 / sampleRate;
        return (float)(rc / (rc + dt));
    }
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
