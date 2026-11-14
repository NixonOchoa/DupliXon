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

    public event Action<byte[]>? FrameEncoded;
    public event Action<string>? Failed;

    public void Start(StreamProfile profile, int monitorIndex)
    {
        if (_process is not null) throw new InvalidOperationException("Already streaming.");

        var scale = $"scale=w='min(iw\\,{profile.MaxWidth})':h='min(ih\\,{profile.MaxHeight})'" +
                    ":force_original_aspect_ratio=decrease:force_divisible_by=2:flags=bicubic";
        var args = string.Join(' ',
            "-hide_banner -loglevel error -fflags nobuffer",
            $"-f lavfi -i ddagrab=output_idx={monitorIndex}:framerate={profile.Fps}:draw_mouse=1",
            $"-vf \"hwdownload,format=bgra,{scale},format=nv12\"",
            VideoEncoder.Arguments(encoder, profile),
            "-bsf:v h264_metadata=aud=insert -flush_packets 1 -f h264 pipe:1");

        _process = Process.Start(new ProcessStartInfo(ffmpegPath, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start ffmpeg.");

        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) Debug.WriteLine($"[ffmpeg] {e.Data}");
        };
        _process.BeginErrorReadLine();

        _cts = new CancellationTokenSource();
        var output = _process.StandardOutput.BaseStream;
        var token = _cts.Token;
        _pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in new AnnexBAccessUnitReader(output).ReadAsync(token))
                    FrameEncoded?.Invoke(frame);
                if (!token.IsCancellationRequested) Failed?.Invoke("La captura de pantalla se detuvo inesperadamente.");
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
