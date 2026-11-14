using DupliXon.Core;

namespace DupliXon.Media.VirtualDisplay;

/// <summary>
/// Provides the extra monitor for "second screen" mode. Uses an existing virtual display if one is
/// already attached; otherwise plugs one in through the Parsec VDD driver for as long as a session needs it.
/// </summary>
public sealed class VirtualDisplayManager : IDisposable
{
    public const string WingetId = "Parsec.ParsecVDD";

    private readonly object _gate = new();
    private ParsecVdd? _vdd;
    private int _index = -1;
    private int _users;

    /// <summary>A virtual monitor is attached right now, or the driver can create one on demand.</summary>
    public static bool IsAvailable => DisplayCatalog.GetOutputs().Any(o => o.IsVirtual) || ParsecVdd.IsInstalled;

    public static bool DriverInstalled => ParsecVdd.IsInstalled;

    /// <summary>True when exactly one session uses the virtual monitor, so it may pick the resolution.</summary>
    public bool IsSoleUser { get { lock (_gate) return _users == 1; } }

    /// <summary>Returns a virtual monitor to stream, creating it if needed. Pair every success with <see cref="Release"/>.</summary>
    public DisplayOutput? Acquire()
    {
        lock (_gate)
        {
            var existing = DisplayCatalog.GetOutputs().FirstOrDefault(o => o.IsVirtual);
            if (existing is not null) { _users++; return existing; }

            _vdd ??= ParsecVdd.Open();
            if (_vdd is null) return null;
            _index = _vdd.AddDisplay();
            Log.Info($"Parsec VDD: added display #{_index}");
            if (_index < 0) return null;

            // The new monitor is connected but not on the desktop yet: extend onto it.
            Thread.Sleep(1200);
            var extended = DisplayModes.ExtendDesktop();
            Log.Info($"Parsec VDD: extend desktop -> {extended}");

            for (var i = 0; i < 40; i++)
            {
                Thread.Sleep(150);
                if (i == 15) DisplayModes.ExtendDesktop(); // retry once if Windows was still settling
                var created = DisplayCatalog.GetOutputs().FirstOrDefault(o => o.IsVirtual);
                if (created is not null) { _users++; return created; }
            }
            Log.Error("Parsec VDD: display did not appear on the desktop");
            RemoveLocked();
            return null;
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            if (_users > 0) _users--;
            if (_users == 0) RemoveLocked();
        }
    }

    private void RemoveLocked()
    {
        if (_vdd is null || _index < 0) return;
        _vdd.RemoveDisplay(_index);
        Log.Info($"Parsec VDD: removed display #{_index}");
        _index = -1;
        _vdd.Dispose();
        _vdd = null;
    }

    public void Dispose()
    {
        lock (_gate) { _users = 0; RemoveLocked(); }
    }
}
