using DupliXon.Core;

namespace DupliXon.Server;

/// <summary>User-selected options applied to every new session.</summary>
public sealed class MirrorSettings
{
    public int MonitorIndex { get; set; }
    public QualityPreset Quality { get; set; } = QualityPreset.Auto;
    public int Fps { get; set; } = 60;
}
