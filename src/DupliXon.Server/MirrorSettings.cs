using DupliXon.Core;

namespace DupliXon.Server;

public enum ShareMode
{
    /// <summary>Show the same picture as a physical monitor.</summary>
    Mirror,
    /// <summary>Use the device as an extra monitor (backed by a virtual display).</summary>
    Extend,
}

/// <summary>User-selected options applied to every new session.</summary>
public sealed class MirrorSettings
{
    public ShareMode Mode { get; set; } = ShareMode.Mirror;
    /// <summary>DXGI device name (e.g. \\.\DISPLAY1) of the monitor to mirror; null = primary.</summary>
    public string? MirrorDisplay { get; set; }
    public QualityPreset Quality { get; set; } = QualityPreset.Auto;
    public int Fps { get; set; } = 60;
    public bool Audio { get; set; } = true;
}
