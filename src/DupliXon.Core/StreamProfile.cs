namespace DupliXon.Core;

/// <summary>
/// Encoding parameters for one mirroring session. The width/height form a bounding box:
/// the encoder fits the captured desktop inside it, keeping the aspect ratio and never upscaling.
/// </summary>
public sealed record StreamProfile(int MaxWidth, int MaxHeight, int Fps, int BitrateKbps)
{
    public const int MinBitrateKbps = 4_000;
    public const int MaxBitrateKbps = 40_000;

    public static StreamProfile For(DeviceInfo device, QualityPreset preset, int fps)
    {
        var (w, h) = device.NativePixels;
        if (w < 320 || h < 240) (w, h) = (1920, 1080);

        (w, h) = preset switch
        {
            QualityPreset.FullHd => (Math.Min(w, 1920), Math.Min(h, 1080)),
            QualityPreset.QuadHd => (Math.Min(Math.Max(w, 2560), 2560), Math.Min(Math.Max(h, 1440), 1440)),
            QualityPreset.UltraHd => (3840, 2160),
            _ => (w, h),
        };

        // H.264 level 5.1 limit; keeps every hardware encoder happy.
        w = Math.Min(w, 4096) & ~1;
        h = Math.Min(h, 2304) & ~1;
        fps = Math.Clamp(fps, 15, 60);

        var bitrate = (int)Math.Clamp((long)w * h * fps / 10_000, MinBitrateKbps, MaxBitrateKbps);
        return new StreamProfile(w, h, fps, bitrate);
    }

    public override string ToString() => $"{MaxWidth}×{MaxHeight} @ {Fps} fps · {BitrateKbps / 1000.0:0.#} Mbps";
}
