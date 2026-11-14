using System.Runtime.InteropServices;

namespace DupliXon.Media;

/// <summary>Reads and switches display modes, used to size the virtual "second screen" to the receiver.</summary>
public static class DisplayModes
{
    private const int EnumCurrentSettings = -1;
    private const int DispChangeSuccessful = 0;
    private const int DmPelsWidth = 0x80000, DmPelsHeight = 0x100000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string deviceName, ref DevMode devMode, nint hwnd, uint flags, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint devNum, ref DisplayDevice dd, uint flags);

    /// <summary>True when the monitor attached to this display reports itself as virtual.</summary>
    public static bool IsVirtualMonitor(string deviceName)
    {
        var dd = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevices(deviceName, i, ref dd, 0); i++)
        {
            var text = $"{dd.DeviceString} {dd.DeviceID}";
            if (text.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("MTT", StringComparison.Ordinal) || text.Contains("Parsec", StringComparison.OrdinalIgnoreCase))
                return true;
            dd.cb = Marshal.SizeOf<DisplayDevice>();
        }
        return false;
    }

    /// <summary>
    /// Switches the display to the supported mode that best matches the target size: same aspect ratio first,
    /// then the largest area not above the target. Returns the applied size, or null if nothing changed.
    /// </summary>
    public static (int Width, int Height)? MatchResolution(string deviceName, int targetWidth, int targetHeight)
    {
        var modes = new List<DevMode>();
        var dm = New();
        for (var i = 0; EnumDisplaySettings(deviceName, i, ref dm); i++) { modes.Add(dm); dm = New(); }
        if (modes.Count == 0) return null;

        var targetAspect = (double)targetWidth / targetHeight;
        var best = modes
            .Where(m => m.dmPelsWidth >= m.dmPelsHeight)
            .OrderBy(m => Math.Abs((double)m.dmPelsWidth / m.dmPelsHeight - targetAspect) < 0.03 ? 0 : 1)
            .ThenBy(m => m.dmPelsWidth <= targetWidth && m.dmPelsHeight <= targetHeight ? 0 : 1)
            .ThenByDescending(m => (long)m.dmPelsWidth * m.dmPelsHeight)
            .ThenByDescending(m => m.dmDisplayFrequency)
            .FirstOrDefault();
        if (best.dmPelsWidth == 0) return null;

        var current = New();
        if (EnumDisplaySettings(deviceName, EnumCurrentSettings, ref current) &&
            current.dmPelsWidth == best.dmPelsWidth && current.dmPelsHeight == best.dmPelsHeight)
            return ((int)best.dmPelsWidth, (int)best.dmPelsHeight);

        best.dmFields = DmPelsWidth | DmPelsHeight;
        return ChangeDisplaySettingsEx(deviceName, ref best, 0, 0, 0) == DispChangeSuccessful
            ? ((int)best.dmPelsWidth, (int)best.dmPelsHeight)
            : null;
    }

    private static DevMode New() => new() { dmSize = (ushort)Marshal.SizeOf<DevMode>() };
}
