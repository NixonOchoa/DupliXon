using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DupliXon.Core;
using DupliXon.Media;
using DupliXon.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace DupliXon.Server;

/// <summary>
/// Embedded web server: serves the Safari receiver page and runs WebRTC signaling over a WebSocket.
/// Every incoming device must be approved by the user on the PC before any pixel is sent.
/// </summary>
public sealed class DupliXonServer : IAsyncDisposable
{
    public const int DefaultPort = 8787;

    private readonly string _ffmpeg;
    private readonly EncoderKind _encoder;
    private readonly ConcurrentDictionary<Guid, MirrorSession> _sessions = new();
    private readonly SystemAudioCapture _audio = new();
    private readonly object _audioGate = new();
    private readonly DupliXon.Media.VirtualDisplay.VirtualDisplayManager _virtual = new();
    private WebApplication? _app;

    public DupliXonServer(string ffmpeg, EncoderKind encoder, MirrorSettings settings, int port = DefaultPort)
    {
        _ffmpeg = ffmpeg;
        _encoder = encoder;
        Settings = settings;
        Port = port;
        // One loopback capture/encoder shared by every receiver.
        _audio.OpusFrame += frame =>
        {
            foreach (var s in _sessions.Values) s.SendAudio(frame);
        };
    }

    private void UpdateAudio()
    {
        lock (_audioGate)
        {
            var needed = _sessions.Values.Any(s => s.HasAudio && s.State == SessionState.Streaming);
            try
            {
                if (needed && !_audio.IsRunning) _audio.Start();
                else if (!needed && _audio.IsRunning) _audio.Stop();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[audio] {ex.Message}"); }
        }
    }

    /// <summary>Monitor a new session should capture, or null when Extend mode has no virtual display.</summary>
    private DisplayOutput? ResolveDisplay(DeviceInfo device, out bool acquiredVirtual)
    {
        acquiredVirtual = false;
        var outputs = DisplayCatalog.GetOutputs();
        if (Settings.Mode == ShareMode.Extend)
        {
            var target = _virtual.Acquire();
            acquiredVirtual = target is not null;
            target ??= outputs.FirstOrDefault(o => !o.IsPrimary);
            if (target is null) return null;
            // Size the extra screen like the device's panel so it is pixel-perfect — but only for the
            // first viewer: changing the mode under a running capture would break the other session.
            var (w, h) = device.NativePixels;
            var mayResize = !acquiredVirtual || _virtual.IsSoleUser;
            if (mayResize && w > 0 && h > 0 && DisplayModes.MatchResolution(target.DeviceName, w, h) is not null)
                return DisplayCatalog.GetOutputs().FirstOrDefault(o => o.DeviceName == target.DeviceName) ?? target;
            return target;
        }
        return outputs.FirstOrDefault(o => o.DeviceName == Settings.MirrorDisplay)
               ?? outputs.FirstOrDefault(o => o.IsPrimary) ?? outputs.FirstOrDefault();
    }

    public int Port { get; }
    public MirrorSettings Settings { get; }
    public string Url => $"http://{LocalAddress()}:{Port}/";

    /// <summary>Asks the UI whether a device may mirror the screen.</summary>
    public Func<ConnectionRequest, CancellationToken, Task<bool>> ApproveAsync { get; set; } = (_, _) => Task.FromResult(false);

    public event Action<MirrorSession>? SessionChanged;

    public IReadOnlyCollection<MirrorSession> Sessions => _sessions.Values.ToArray();

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
        builder.Logging.ClearProviders();

        _app = builder.Build();
        _app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        _app.UseDefaultFiles();
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot")),
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
        });
        _app.Map("/ws", HandleSocketAsync);
        await _app.StartAsync().ConfigureAwait(false);
    }

    public async Task DisconnectAsync(Guid id)
    {
        if (_sessions.TryRemove(id, out var session)) await session.DisposeAsync().ConfigureAwait(false);
        UpdateAudio();
    }

    private async Task HandleSocketAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
        var sendLock = new SemaphoreSlim(1, 1);
        var abort = ctx.RequestAborted;

        async Task Send(SignalMessage m)
        {
            if (socket.State != WebSocketState.Open) return;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(m);
            await sendLock.WaitAsync().ConfigureAwait(false);
            try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false); }
            catch { /* client went away */ }
            finally { sendLock.Release(); }
        }

        MirrorSession? session = null;
        var usesVirtual = false;
        try
        {
            await foreach (var msg in ReadMessagesAsync(socket, abort))
            {
                if (msg.Type == "hello" && session is null)
                {
                    var device = new DeviceInfo(
                        Clean(msg.Name, "Dispositivo"), Clean(msg.Kind, "Navegador"),
                        msg.Width ?? 0, msg.Height ?? 0, msg.Dpr ?? 1);
                    var request = new ConnectionRequest(Guid.NewGuid(), device,
                        ctx.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "?");

                    await Send(new SignalMessage { Type = "pending" });
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(abort);
                    timeout.CancelAfter(TimeSpan.FromSeconds(60));
                    bool accepted;
                    try { accepted = await ApproveAsync(request, timeout.Token); }
                    catch (OperationCanceledException) { accepted = false; }

                    if (!accepted) { await Send(new SignalMessage { Type = "rejected" }); break; }

                    var (display, acquired) = await Task.Run(() => (ResolveDisplay(device, out var a), a));
                    usesVirtual = acquired;
                    if (display is null)
                    {
                        await Send(new SignalMessage { Type = "error", Message = "Activa la segunda pantalla en DupliXon (instala la pantalla virtual) y vuelve a conectar." });
                        break;
                    }

                    var profile = StreamProfile.For(device, Settings.Quality, Settings.Fps);
                    session = new MirrorSession(request, profile, display, Settings.Audio, _ffmpeg, _encoder, Send);
                    session.InputReceived += (s, data) =>
                    {
                        if (!Settings.AllowControl) return;
                        try { DupliXon.Media.Input.InputInjector.Handle(s.Display, data); }
                        catch (Exception ex) { Log.Error("input", ex); }
                    };
                    session.StateChanged += s =>
                    {
                        if (s.State == SessionState.Ended) _sessions.TryRemove(s.Request.Id, out _);
                        UpdateAudio();
                        SessionChanged?.Invoke(s);
                    };
                    _sessions[request.Id] = session;
                    SessionChanged?.Invoke(session);
                    await session.StartAsync();
                }
                else if (msg.Type == "bye") break;
                else session?.HandleSignal(msg);
            }

            // Keep the socket open while streaming so "bye"/disconnects are observed.
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            if (session is not null)
            {
                _sessions.TryRemove(session.Request.Id, out _);
                await session.DisposeAsync();
            }
            if (usesVirtual) _virtual.Release(); // unplugs the virtual monitor when nobody uses it
        }
    }

    private static async IAsyncEnumerable<SignalMessage> ReadMessagesAsync(WebSocket socket,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) yield break;
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            SignalMessage? msg = null;
            try { msg = JsonSerializer.Deserialize<SignalMessage>(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length)); }
            catch (JsonException) { }
            if (msg is not null) yield return msg;
        }
    }

    private static string Clean(string? value, string fallback)
    {
        var s = (value ?? "").Trim();
        if (s.Length == 0) return fallback;
        return s.Length > 40 ? s[..40] : s;
    }

    /// <summary>Best LAN IPv4 address: an active Wi-Fi/Ethernet adapter with a default gateway.</summary>
    public static string LocalAddress()
    {
        var best = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => (Nic: n, Props: n.GetIPProperties()))
            .Where(x => x.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .OrderByDescending(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .SelectMany(x => x.Props.UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        return best?.ToString() ?? "127.0.0.1";
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _sessions.Keys.ToArray()) await DisconnectAsync(id).ConfigureAwait(false);
        _audio.Dispose();
        _virtual.Dispose();
        if (_app is not null) await _app.DisposeAsync().ConfigureAwait(false);
    }
}
