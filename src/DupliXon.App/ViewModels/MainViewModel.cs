using System.Collections.ObjectModel;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DupliXon.App.Interop;
using DupliXon.Core;
using DupliXon.Media;
using DupliXon.Server;
using DupliXon.Streaming;
using QRCoder;

namespace DupliXon.App.ViewModels;

public sealed partial class MonitorOption(int index, string label) : ObservableObject
{
    public int Index { get; } = index;
    public string Label { get; } = label;
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class DeviceItem(Guid id, string name, string kind, string detail) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public string Glyph { get; } = kind switch { "iPhone" => "", "iPad" => "", _ => "" };
    [ObservableProperty] private string _detail = detail;
}

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly MirrorSettings _settings = new();
    private readonly SemaphoreSlim _approvalGate = new(1, 1);
    private DupliXonServer? _server;
    private TaskCompletionSource<bool>? _pendingDecision;

    [ObservableProperty] private string _statusText = "Iniciando…";
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private string _encoderName = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private BitmapImage? _qrCode;
    [ObservableProperty] private string? _setupError;

    [ObservableProperty] private ConnectionRequest? _pendingRequest;
    [ObservableProperty] private string _pendingTitle = "";
    [ObservableProperty] private string _pendingDetail = "";

    [ObservableProperty] private QualityPreset _quality = QualityPreset.Auto;
    [ObservableProperty] private int _fps = 60;

    public ObservableCollection<MonitorOption> Monitors { get; } = [];
    public ObservableCollection<DeviceItem> Devices { get; } = [];

    public bool HasDevices => Devices.Count > 0;

    public MainViewModel()
    {
        Devices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDevices));
        var monitors = NativeMethods.GetMonitors();
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var label = monitors.Count == 1 ? $"Pantalla · {m.Width}×{m.Height}" : $"{(m.IsPrimary ? "Principal" : $"Pantalla {i + 1}")} · {m.Width}×{m.Height}";
            Monitors.Add(new MonitorOption(i, label) { IsSelected = i == 0 });
        }
    }

    public async Task InitializeAsync()
    {
        var ffmpeg = FfmpegLocator.Find();
        if (ffmpeg is null)
        {
            SetupError = "Falta FFmpeg, el motor de captura. Ejecuta scripts\\install-ffmpeg.ps1 o: winget install Gyan.FFmpeg";
            StatusText = "Configuración pendiente";
            return;
        }

        StatusText = "Detectando aceleración por hardware…";
        var encoder = await VideoEncoder.DetectAsync(ffmpeg);
        EncoderName = VideoEncoder.DisplayName(encoder);

        _server = new DupliXonServer(ffmpeg, encoder, _settings) { ApproveAsync = ApproveAsync };
        _server.SessionChanged += s => Application.Current.Dispatcher.BeginInvoke(() => OnSessionChanged(s));
        try
        {
            await _server.StartAsync();
        }
        catch (IOException ex)
        {
            SetupError = $"El puerto {_server.Port} está ocupado. ¿DupliXon ya está abierto? ({ex.Message})";
            StatusText = "No se pudo iniciar";
            return;
        }

        Url = _server.Url;
        QrCode = RenderQr(Url + "?go=1");
        IsReady = true;
        StatusText = "Listo para compartir";
    }

    partial void OnQualityChanged(QualityPreset value) => _settings.Quality = value;
    partial void OnFpsChanged(int value) => _settings.Fps = value;

    [RelayCommand]
    private void SelectMonitor(MonitorOption option)
    {
        foreach (var m in Monitors) m.IsSelected = m == option;
        _settings.MonitorIndex = option.Index;
    }

    [RelayCommand] private void SetQuality(string value) => Quality = Enum.Parse<QualityPreset>(value);
    [RelayCommand] private void SetFps(string value) => Fps = int.Parse(value);
    [RelayCommand] private void CopyUrl() { try { Clipboard.SetText(Url); } catch { } }

    [RelayCommand] private void Accept() => _pendingDecision?.TrySetResult(true);
    [RelayCommand] private void Reject() => _pendingDecision?.TrySetResult(false);

    [RelayCommand]
    private async Task Disconnect(DeviceItem item)
    {
        if (_server is not null) await _server.DisconnectAsync(item.Id);
        Devices.Remove(item);
        UpdateStatus();
    }

    private async Task<bool> ApproveAsync(ConnectionRequest request, CancellationToken ct)
    {
        // Development only: lets automated end-to-end tests skip the approval sheet.
        if (Environment.GetEnvironmentVariable("DUPLIXON_DEV_AUTO_ACCEPT") == "1") return true;

        await _approvalGate.WaitAsync(ct);
        try
        {
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var reg = ct.Register(() => decision.TrySetResult(false));
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _pendingDecision = decision;
                PendingTitle = $"«{request.Device.Name}» quiere ver tu pantalla";
                var (w, h) = request.Device.NativePixels;
                PendingDetail = $"{request.Device.Kind} · {w}×{h} · {request.RemoteAddress}";
                PendingRequest = request;
                SystemSounds.Asterisk.Play();
                RequestAttention?.Invoke();
            });
            var accepted = await decision.Task;
            await Application.Current.Dispatcher.InvokeAsync(() => { PendingRequest = null; _pendingDecision = null; });
            return accepted;
        }
        finally { _approvalGate.Release(); }
    }

    /// <summary>Raised so the window can come to the foreground when a device asks to connect.</summary>
    public event Action? RequestAttention;

    private void OnSessionChanged(MirrorSession s)
    {
        var existing = Devices.FirstOrDefault(d => d.Id == s.Request.Id);
        if (s.State == SessionState.Ended)
        {
            if (existing is not null) Devices.Remove(existing);
        }
        else
        {
            var detail = s.State == SessionState.Streaming ? $"En vivo · {s.Profile}" : "Conectando…";
            if (existing is null) Devices.Add(new DeviceItem(s.Request.Id, s.Request.Device.Name, s.Request.Device.Kind, detail));
            else existing.Detail = detail;
        }
        UpdateStatus();
    }

    private void UpdateStatus() => StatusText = Devices.Count switch
    {
        0 => "Listo para compartir",
        1 => "Compartiendo con 1 dispositivo",
        var n => $"Compartiendo con {n} dispositivos",
    };

    private static BitmapImage RenderQr(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12, [10, 12, 20], [255, 255, 255], drawQuietZones: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public async ValueTask DisposeAsync()
    {
        _pendingDecision?.TrySetResult(false);
        if (_server is not null) await _server.DisposeAsync();
    }
}
