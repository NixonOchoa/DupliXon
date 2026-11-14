using System.Diagnostics;
using Concentus;
using Concentus.Enums;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DupliXon.Media;

/// <summary>
/// Captures everything the PC is playing (WASAPI loopback), converts it to 48 kHz stereo and encodes
/// 20 ms Opus frames. Silence is filled in so the RTP clock never drifts from wall time.
/// </summary>
public sealed class SystemAudioCapture : IDisposable
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int FrameSamples = 960; // 20 ms

    private readonly object _gate = new();
    private readonly IOpusEncoder _encoder;
    private readonly List<short> _pcm = new(FrameSamples * Channels * 8);
    private readonly List<float> _src = new(); // interleaved stereo at the device rate
    private readonly Stopwatch _clock = new();
    private readonly byte[] _packet = new byte[4000];
    private WasapiLoopbackCapture? _capture;
    private Timer? _silenceTimer;
    private double _srcPos;
    private long _samplesSent;

    public event Action<byte[]>? OpusFrame;

    public SystemAudioCapture()
    {
        _encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        _encoder.Bitrate = 160_000;
        _encoder.Complexity = 5;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_MUSIC;
    }

    public bool IsRunning => _capture is not null;

    public void Start()
    {
        if (_capture is not null) return;
        _capture = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnData;
        _capture.StartRecording();
        _samplesSent = 0;
        _clock.Restart();
        _silenceTimer = new Timer(_ => PadSilence(), null, 20, 20);
    }

    public void Stop()
    {
        _silenceTimer?.Dispose();
        _silenceTimer = null;
        if (_capture is null) return;
        _capture.DataAvailable -= OnData;
        try { _capture.StopRecording(); } catch { }
        _capture.Dispose();
        _capture = null;
        lock (_gate) { _pcm.Clear(); _src.Clear(); _srcPos = 0; }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var fmt = _capture!.WaveFormat;
        var ch = fmt.Channels;
        var bytesPerSample = fmt.BitsPerSample / 8;
        var frames = e.BytesRecorded / (bytesPerSample * ch);

        lock (_gate)
        {
            for (var i = 0; i < frames; i++)
            {
                var offset = i * ch * bytesPerSample;
                var l = Sample(e.Buffer, offset, fmt);
                var r = ch > 1 ? Sample(e.Buffer, offset + bytesPerSample, fmt) : l;
                _src.Add(l);
                _src.Add(r);
            }
            Resample(fmt.SampleRate);
            Drain();
        }
    }

    private static float Sample(byte[] buf, int offset, WaveFormat fmt) => fmt.BitsPerSample switch
    {
        32 when fmt.Encoding != WaveFormatEncoding.Pcm => BitConverter.ToSingle(buf, offset),
        32 => BitConverter.ToInt32(buf, offset) / 2147483648f,
        24 => ((buf[offset] << 8 | buf[offset + 1] << 16 | buf[offset + 2] << 24) >> 8) / 8388608f,
        _ => BitConverter.ToInt16(buf, offset) / 32768f,
    };

    private void Resample(int rate)
    {
        var step = (double)rate / SampleRate;
        var count = _src.Count / 2;
        while (_srcPos + 1 < count)
        {
            var i = (int)_srcPos;
            var t = (float)(_srcPos - i);
            for (var c = 0; c < 2; c++)
            {
                var v = _src[i * 2 + c] * (1 - t) + _src[(i + 1) * 2 + c] * t;
                _pcm.Add((short)Math.Clamp(v * 32767f, short.MinValue, short.MaxValue));
            }
            _srcPos += step;
        }
        var consumed = Math.Min((int)_srcPos, count);
        _src.RemoveRange(0, consumed * 2);
        _srcPos -= consumed;
    }

    private void PadSilence()
    {
        lock (_gate)
        {
            // If playback is silent WASAPI delivers nothing; keep the stream ticking with zeros.
            var expected = _clock.ElapsedTicks * SampleRate / Stopwatch.Frequency;
            var buffered = _pcm.Count / Channels;
            var missing = expected - (_samplesSent + buffered) - FrameSamples * 3;
            if (missing <= 0) return;
            for (long i = 0; i < missing * Channels; i++) _pcm.Add(0);
            Drain();
        }
    }

    private void Drain()
    {
        const int frameValues = FrameSamples * Channels;
        Span<short> frame = stackalloc short[frameValues];
        while (_pcm.Count >= frameValues)
        {
            for (var i = 0; i < frameValues; i++) frame[i] = _pcm[i];
            _pcm.RemoveRange(0, frameValues);
            var len = _encoder.Encode(frame, FrameSamples, _packet, _packet.Length);
            _samplesSent += FrameSamples;
            if (len > 0) OpusFrame?.Invoke(_packet[..len]);
        }
    }

    public void Dispose() => Stop();
}
