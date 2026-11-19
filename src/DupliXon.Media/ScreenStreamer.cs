using System.Diagnostics;
using DupliXon.Core;

namespace DupliXon.Media;

/// <summary>
/// Captures a monitor with DXGI Desktop Duplication (ffmpeg ddagrab), scales it to the receiver's
/// resolution and encodes H.264 on the GPU. Emits one Annex-B access unit per frame.
/// </summary>
public sealed class ScreenStreamer(string ffmpegPath, EncoderKind encoder) : IAsyncDisposable
{
    private Process? _process;
    private CancellationTokenSource? _cts;
    private Task? _pump;

    /// <summary>Set when the zero-copy GPU pipeline fails on this PC; later sessions use the CPU path.</summary>
    private static volatile bool GpuPathBroken;
    private bool _gpuPath;
    private DateTime _startedAt;

    /// <summary>Largest even size with the source aspect ratio that fits the box, never upscaling.</summary>
    public static (int Width, int Height) Fit(int srcW, int srcH, int boxW, int boxH)
    {
        if (srcW <= 0 || srcH <= 0) return (boxW & ~1, boxH & ~1);
        var s = Math.Min(1.0, Math.Min((double)boxW / srcW, (double)boxH / srcH));
        return (Math.Max(2, (int)(srcW * s)) & ~1, Math.Max(2, (int)(srcH * s)) & ~1);
    }

    public event Action<byte[]>? FrameEncoded;
    public event Action<string>? Failed;

    public void Start(StreamProfile profile, DisplayOutput display)
    {
        if (_process is not null) throw new InvalidOperationException("Already streaming.");

        var (w, h) = Fit(display.Width, display.Height, profile.MaxWidth, profile.MaxHeight);
        var grab = $"ddagrab=output_idx={display.OutputIndex}:framerate={profile.Fps}:draw_mouse=1";
        _gpuPath = encoder == EncoderKind.IntelQuickSync && !GpuPathBroken;

        // Intel: the frame never leaves the GPU (capture → scale → encode, zero copies).
        // Others: copy to RAM and scale on the CPU.
        var graph = _gpuPath
            ? $"-init_hw_device qsv=qs@dx -filter_hw_device dx -filter_complex \"{grab}," +
              $"hwmap=derive_device=qsv,format=qsv,vpp_qsv=w={w}:h={h}:format=nv12[v]\""
            : $"-filter_hw_device dx -filter_complex \"{grab},hwdownload,format=bgra," +
              $"scale={w}:{h}:flags=bicubic,format=nv12[v]\"";

        var args = string.Join(' ',
            "-hide_banner -loglevel error",
            // Capture on the GPU that owns the monitor (virtual displays may live on another adapter).
            $"-init_hw_device d3d11va=dx:{display.AdapterIndex}",
            graph, "-map \"[v]\"",
            VideoEncoder.Arguments(encoder, profile),
            // FLV frames each frame with its length, so every frame is forwarded the moment it is encoded.
            "-flush_packets 1 -flvflags no_duration_filesize -f flv pipe:1");

        _process = Process.Start(new ProcessStartInfo(ffmpegPath, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start ffmpeg.");

        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) Log.Error($"ffmpeg: {e.Data}");
        };
        Log.Info($"capture {display.DeviceName} (adapter {display.AdapterIndex}, output {display.OutputIndex}, {display.Width}x{display.Height}) -> {profile}");
        _process.BeginErrorReadLine();

        _startedAt = DateTime.UtcNow;
        _cts = new CancellationTokenSource();
        var output = _process.StandardOutput.BaseStream;
        var token = _cts.Token;
        _pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in new FlvH264Reader(output).ReadAsync(token))
                    FrameEncoded?.Invoke(frame);
                if (!token.IsCancellationRequested)
                {
                    if (_gpuPath && DateTime.UtcNow - _startedAt < TimeSpan.FromSeconds(5))
                    {
                        GpuPathBroken = true; // the automatic restart will use the CPU path
                        Log.Error("GPU capture path failed at start; falling back to CPU path");
                    }
                    Failed?.Invoke("La captura de pantalla se detuvo inesperadamente.");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!token.IsCancellationRequested) { Failed?.Invoke(ex.Message); }
            catch { }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }
        if (_pump is not null) { try { await _pump.ConfigureAwait(false); } catch { } }
        _process?.Dispose();
        _process = null;
    }
}
