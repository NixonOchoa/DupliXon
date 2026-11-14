using System.Diagnostics;
using DupliXon.Core;
using DupliXon.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace DupliXon.Streaming;

/// <summary>One receiver watching this PC: a WebRTC peer connection fed by a dedicated encoder.</summary>
public sealed class MirrorSession : IAsyncDisposable
{
    private readonly RTCPeerConnection _pc;
    private readonly ScreenStreamer _streamer;
    private readonly Func<SignalMessage, Task> _send;
    private readonly DisplayOutput _display;
    private readonly Stopwatch _clock = new();
    private long _lastTicks;
    private int _started;

    public ConnectionRequest Request { get; }
    public bool HasAudio { get; }
    public string DisplayName => _display.IsVirtual ? "Segunda pantalla" : "Duplicando";
    public StreamProfile Profile { get; }
    public SessionState State { get; private set; } = SessionState.Connecting;
    public event Action<MirrorSession>? StateChanged;

    /// <summary>Raw input event (JSON) received from the device over the "input" data channel.</summary>
    public event Action<MirrorSession, byte[]>? InputReceived;

    /// <summary>The monitor this session shows; input coordinates are mapped onto it.</summary>
    public DisplayOutput Display => _display;

    public MirrorSession(ConnectionRequest request, StreamProfile profile, DisplayOutput display, bool withAudio,
        string ffmpeg, EncoderKind encoder, Func<SignalMessage, Task> send)
    {
        Request = request;
        Profile = profile;
        _send = send;
        _display = display;
        HasAudio = withAudio;

        _pc = new RTCPeerConnection(new RTCConfiguration());
        var h264 = new VideoFormat(VideoCodecsEnum.H264, 96, 90000,
            "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f");
        _pc.addTrack(new MediaStreamTrack(h264, MediaStreamStatusEnum.SendOnly));
        if (withAudio)
        {
            var opus = new AudioFormat(AudioCodecsEnum.OPUS, 111, 48000, 2, "minptime=10;useinbandfec=1;stereo=1;sprop-stereo=1");
            _pc.addTrack(new MediaStreamTrack(opus, MediaStreamStatusEnum.SendOnly));
        }

        _pc.onicecandidate += c =>
        {
            if (c is null) return;
            var line = c.ToString();
            _ = _send(new SignalMessage
            {
                Type = "ice",
                Candidate = line.StartsWith("candidate:", StringComparison.Ordinal) ? line : "candidate:" + line,
                SdpMid = c.sdpMid ?? "0",
                SdpMLineIndex = c.sdpMLineIndex,
            });
        };
        _pc.onconnectionstatechange += s =>
        {
            if (s == RTCPeerConnectionState.connected) BeginStreaming();
            else if (s is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed or RTCPeerConnectionState.disconnected)
                SetState(SessionState.Ended);
        };

        _streamer = new ScreenStreamer(ffmpeg, encoder);
        _streamer.FrameEncoded += OnFrame;
        _streamer.Failed += msg =>
        {
            _ = _send(new SignalMessage { Type = "error", Message = msg });
            SetState(SessionState.Ended);
        };
    }

    public async Task StartAsync()
    {
        // Ordered, reliable channel for touch / mouse / keyboard events from the device.
        var input = await _pc.createDataChannel("input", new RTCDataChannelInit { ordered = true }).ConfigureAwait(false);
        input.onopen += () => Log.Info($"input channel open ({Request.Device.Name})");
        input.onmessage += (_, _, data) =>
        {
            if (State != SessionState.Streaming) Log.Info($"input ignored, state {State}");
            if (State == SessionState.Streaming) InputReceived?.Invoke(this, data);
        };

        var offer = _pc.createOffer();
        await _pc.setLocalDescription(offer).ConfigureAwait(false);
        await _send(new SignalMessage { Type = "offer", Sdp = offer.sdp, Info = Profile.ToString() }).ConfigureAwait(false);
    }

    public void HandleSignal(SignalMessage msg)
    {
        switch (msg.Type)
        {
            case "answer" when msg.Sdp is not null:
                var result = _pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = msg.Sdp });
                if (result != SetDescriptionResultEnum.OK)
                {
                    Debug.WriteLine($"[webrtc] setRemoteDescription: {result}");
                    SetState(SessionState.Ended);
                }
                break;
            case "ice" when !string.IsNullOrWhiteSpace(msg.Candidate):
                _pc.addIceCandidate(new RTCIceCandidateInit
                {
                    candidate = msg.Candidate,
                    sdpMid = msg.SdpMid,
                    sdpMLineIndex = msg.SdpMLineIndex ?? 0,
                });
                break;
        }
    }

    private void BeginStreaming()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _clock.Start();
        _streamer.Start(Profile, _display);
        SetState(SessionState.Streaming);
    }

    private void OnFrame(byte[] accessUnit)
    {
        var now = _clock.ElapsedTicks;
        var elapsed = _lastTicks == 0 ? Stopwatch.Frequency / Profile.Fps : now - _lastTicks;
        _lastTicks = now;
        var rtpUnits = (uint)Math.Max(1, elapsed * 90_000 / Stopwatch.Frequency);
        _pc.SendVideo(rtpUnits, accessUnit);
    }

    /// <summary>Sends one 20 ms Opus frame (48 kHz RTP clock).</summary>
    public void SendAudio(byte[] opusFrame)
    {
        if (HasAudio && State == SessionState.Streaming) _pc.SendAudio(960, opusFrame);
    }

    private void SetState(SessionState state)
    {
        if (State == state || State == SessionState.Ended) return;
        State = state;
        StateChanged?.Invoke(this);
    }

    public async ValueTask DisposeAsync()
    {
        SetState(SessionState.Ended);
        await _streamer.DisposeAsync().ConfigureAwait(false);
        try { _pc.close(); } catch { }
    }
}

