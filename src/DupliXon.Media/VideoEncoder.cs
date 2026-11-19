using System.Diagnostics;
using DupliXon.Core;

namespace DupliXon.Media;

public enum EncoderKind { Nvidia, IntelQuickSync, Amd, Software }

/// <summary>Picks the best H.264 encoder available on this PC and builds its low-latency arguments.</summary>
public static class VideoEncoder
{
    private static readonly (EncoderKind Kind, string Codec)[] Order =
    [
        (EncoderKind.Nvidia, "h264_nvenc"),
        (EncoderKind.IntelQuickSync, "h264_qsv"),
        (EncoderKind.Amd, "h264_amf"),
    ];

    public static async Task<EncoderKind> DetectAsync(string ffmpeg, CancellationToken ct = default)
    {
        foreach (var (kind, codec) in Order)
        {
            var psi = new ProcessStartInfo(ffmpeg,
                $"-hide_banner -loglevel quiet -f lavfi -i color=s=640x360 -frames:v 1 -c:v {codec} -f null -")
            { CreateNoWindow = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            if (p.ExitCode == 0) return kind;
        }
        return EncoderKind.Software;
    }

    public static string DisplayName(EncoderKind kind) => kind switch
    {
        EncoderKind.Nvidia => "NVIDIA NVENC",
        EncoderKind.IntelQuickSync => "Intel Quick Sync",
        EncoderKind.Amd => "AMD AMF",
        _ => "Software (x264)",
    };

    internal static string Arguments(EncoderKind kind, StreamProfile p)
    {
        var b = p.BitrateKbps;
        // Intra refresh: instead of periodic full keyframes (big bursts that Wi-Fi drops, freezing the
        // picture until the next one), a moving band of intra blocks repaints the image every ~1/3 s.
        // Frame sizes stay flat and any loss heals in a fraction of a second.
        var cycle = Math.Max(10, p.Fps / 3);
        var longGop = p.Fps * 10;
        var rate = $"-b:v {b}k -maxrate {b}k -bufsize {b / 4}k -bf 0 -profile:v high";
        return kind switch
        {
            EncoderKind.Nvidia =>
                $"-c:v h264_nvenc -preset p1 -tune ull -zerolatency 1 -delay 0 -rc cbr -g {longGop} -intra-refresh 1 {rate}",
            EncoderKind.IntelQuickSync =>
                $"-c:v h264_qsv -low_power 1 -preset veryfast -async_depth 1 -look_ahead 0 -g {longGop} " +
                $"-int_ref_type vertical -int_ref_cycle_size {cycle} {rate}",
            EncoderKind.Amd => $"-c:v h264_amf -usage ultralowlatency -rc cbr -g {p.Fps} {rate}",
            _ => $"-c:v libx264 -preset ultrafast -tune zerolatency -pix_fmt yuv420p -g {longGop} -intra-refresh 1 {rate}",
        };
    }
}
