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

public sealed partial class MonitorOption(string deviceName, string label) : ObservableObject
{
    public string DeviceName { get; } = deviceName;
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

    // ---- Share mode & audio ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMirror), nameof(IsExtend), nameof(ShowVirtualSetup), nameof(ShowVirtualReady))]
    private ShareMode _mode = ShareMode.Mirror;
    [ObservableProperty] private bool _audio = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVirtualSetup), nameof(ShowVirtualReady))]
    private string? _virtualDisplay;
    [ObservableProperty] private bool _isInstallingDriver;
    [ObservableProperty] private string? _virtualHint;

    public bool IsMirror => Mode == ShareMode.Mirror;
    public bool IsExtend => Mode == ShareMode.Extend;
    public bool ShowVirtualSetup => IsExtend && VirtualDisplay is null;
    public bool ShowVirtualReady => IsExtend && VirtualDisplay is not null;

    public MainViewModel()
    {
        Devices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDevices));
        RefreshDisplays();
        // Development only: start in a given mode for automated end-to-end tests.
        if (Enum.TryParse<ShareMode>(Environment.GetEnvironmentVariable("DUPLIXON_DEV_MODE"), out var devMode)) Mode = devMode;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(RefreshDisplays);
    }

    [RelayCommand]
    private void RefreshDisplays()
    {
        IReadOnlyList<DisplayOutput> outputs;
        try { outputs = DisplayCatalog.GetOutputs(); }
        catch { return; }

        var selected = _settings.MirrorDisplay;
        Monitors.Clear();
        var physical = outputs.Where(o => !o.IsVirtual).ToList();
        for (var i = 0; i < physical.Count; i++)
        {
            var m = physical[i];
            var name = physical.Count == 1 ? "Pantalla" : m.IsPrimary ? "Principal" : $"Pantalla {i + 1}";
            Monitors.Add(new MonitorOption(m.DeviceName, $"{name} · {m.Width}×{m.Height}"));
        }
        var pick = Monitors.FirstOrDefault(m => m.DeviceName == selected) ?? Monitors.FirstOrDefault();
        if (pick is not null) { pick.IsSelected = true; _settings.MirrorDisplay = pick.DeviceName; }

        var virt = outputs.FirstOrDefault(o => o.IsVirtual);
        VirtualDisplay = virt is not null
            ? $"Pantalla virtual activa · {virt.Width}×{virt.Height}"
            : DupliXon.Media.VirtualDisplay.VirtualDisplayManager.DriverInstalled
                ? "Pantalla virtual lista · se crea al conectar"
                : null;
    }

    partial void OnModeChanged(ShareMode value) => _settings.Mode = value;
    partial void OnAudioChanged(bool value) => _settings.Audio = value;

    [ObservableProperty] private bool _allowControl = true;
    partial void OnAllowControlChanged(bool value) => _settings.AllowControl = value;
    [RelayCommand] private void SetControl(string value) => AllowControl = value == "on";

    [RelayCommand] private void SetMode(string value) => Mode = Enum.Parse<ShareMode>(value);
    [RelayCommand] private void SetAudio(string value) => Audio = value == "on";

    [RelayCommand]
    private void OpenDisplaySettings() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:display") { UseShellExecute = true });

    /// <summary>
    /// Installs the Parsec Virtual Display Driver (a real IddCx driver) from the official winget repository.
    /// Windows shows its own administrator prompt; the user stays in control.
    /// </summary>
    [RelayCommand]
    private async Task InstallVirtualDisplay()
    {
        IsInstallingDriver = true;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("winget",
                $"install --id {DupliXon.Media.VirtualDisplay.VirtualDisplayManager.WingetId} -e --accept-source-agreements --accept-package-agreements")
            { UseShellExecute = true };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is not null) await p.WaitForExitAsync();
            // The driver registers its device a few seconds after setup finishes.
            for (var i = 0; i < 10 && !DupliXon.Media.VirtualDisplay.VirtualDisplayManager.DriverInstalled; i++)
                await Task.Delay(1000);
            RefreshDisplays();
            if (VirtualDisplay is null)
                VirtualHint = "La instalación no terminó. Si Windows pidió permiso de administrador, acéptalo e inténtalo de nuevo.";
            else VirtualHint = null;
        }
        catch (Exception ex) { VirtualHint = $"No se pudo instalar la pantalla virtual: {ex.Message}"; }
        finally { IsInstallingDriver = false; }
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

        IsReady = true;
        StatusText = "Listo para compartir";
        RefreshLink(force: true);

        // Watch for an iPhone/iPad plugged in by cable (and network changes) to switch the QR code.
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(() => RefreshLink());
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => RefreshLink();
        timer.Start();
    }

    // ---- Connection link: Wi-Fi or USB cable ----
    [ObservableProperty] private bool _isUsb;
    [ObservableProperty] private string _linkTitle = "Escanea con tu iPad o iPhone";
    [ObservableProperty] private string _linkHint = "Se abrirá Safari. Acepta la solicitud aquí y listo, sin instalar nada.";
    [ObservableProperty] private string? _usbNotice;
    private UsbState _usb = UsbState.None;
    private bool _refreshing;

    private async void RefreshLink(bool force = false)
    {
        if (_server is null || _refreshing) return;
        _refreshing = true;
        try
        {
            var usb = await Task.Run(UsbLink.Detect);
            if (!force && usb == _usb) return;
            _usb = usb;

            IsUsb = usb.Ready;
            var url = usb.Ready ? _server.UrlFor(usb.Address!) : _server.Url;
            if (url != Url) { Url = url; QrCode = RenderQr(Url + "?go=1"); }

            if (usb.Ready)
            {
                LinkTitle = "Conectado por cable USB";
                LinkHint = "Menor latencia que el Wi-Fi. Escanea el código con el dispositivo conectado.";
            }
            else
            {
                LinkTitle = "Escanea con tu iPad o iPhone";
                LinkHint = "Se abrirá Safari. Acepta la solicitud aquí y listo, sin instalar nada.";
            }

            UsbNotice = usb switch
            {
                { Ready: true } => "Mientras uses el cable, tu PC puede usar los datos móviles del dispositivo para Internet.",
                { DriverMissing: true } =>
                    $"Detecté un {usb.DeviceName} por cable. Instala «Apple Devices» desde Microsoft Store para poder usar el cable.",
                { Plugged: true } =>
                    $"Detecté un {usb.DeviceName} por cable. Para usarlo con menor latencia, activa en él Ajustes → Compartir Internet. (Los iPad solo Wi-Fi no lo tienen: usa el Wi-Fi.)",
                _ => null,
            };
        }
        finally { _refreshing = false; }
    }

    partial void OnQualityChanged(QualityPreset value) => _settings.Quality = value;
    partial void OnFpsChanged(int value) => _settings.Fps = value;

    [RelayCommand]
    private void SelectMonitor(MonitorOption option)
    {
        foreach (var m in Monitors) m.IsSelected = m == option;
        _settings.MirrorDisplay = option.DeviceName;
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
            var via = s.Request.RemoteAddress.StartsWith("172.20.10.", StringComparison.Ordinal) ? "Cable USB" : "Wi-Fi";
            var detail = s.State == SessionState.Streaming
                ? $"{s.DisplayName}{(s.HasAudio ? " con sonido" : "")} · {via} · {s.Profile}"
                : "Conectando…";
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

