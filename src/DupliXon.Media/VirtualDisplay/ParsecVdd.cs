using System.Runtime.InteropServices;
using DupliXon.Core;
using Microsoft.Win32.SafeHandles;

namespace DupliXon.Media.VirtualDisplay;

/// <summary>
/// Minimal client for the Parsec Virtual Display Driver (IddCx). Displays exist only while this
/// process keeps the device handle open and pings it at least once a second.
/// Protocol per the open-source parsec-vdd headers (MIT).
/// </summary>
internal sealed class ParsecVdd : IDisposable
{
    private static readonly Guid InterfaceGuid = new("00b41627-04c4-429e-a26e-0265cf50c8fa");
    private const uint IoctlAdd = 0x0022E004, IoctlRemove = 0x0022A008, IoctlUpdate = 0x0022A00C;

    private readonly SafeFileHandle _handle;
    private readonly Timer _keepAlive;
    private readonly object _io = new();

    private ParsecVdd(SafeFileHandle handle)
    {
        _handle = handle;
        _keepAlive = new Timer(_ => Ping(), null, 100, 100);
    }

    /// <summary>True when the driver is installed and its device is present.</summary>
    public static bool IsInstalled => FindDevicePath() is not null;

    public static ParsecVdd? Open()
    {
        var path = FindDevicePath();
        if (path is null) return null;
        var h = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, 0, OpenExisting,
            FileAttributeNormal | FileFlagNoBuffering | FileFlagWriteThrough, 0);
        if (h.IsInvalid) { Log.Error($"Parsec VDD: CreateFile failed ({Marshal.GetLastWin32Error()})"); return null; }
        return new ParsecVdd(h);
    }

    /// <summary>Plugs a new virtual monitor; returns its index or -1.</summary>
    public int AddDisplay()
    {
        var index = Control(IoctlAdd, ReadOnlySpan<byte>.Empty);
        Ping();
        return index;
    }

    public void RemoveDisplay(int index)
    {
        // The driver expects the index as a big-endian 16-bit value.
        Span<byte> data = [(byte)((index >> 8) & 0xFF), (byte)(index & 0xFF)];
        Control(IoctlRemove, data);
        Ping();
    }

    private void Ping()
    {
        try { Control(IoctlUpdate, ReadOnlySpan<byte>.Empty); } catch { }
    }

    private int Control(uint code, ReadOnlySpan<byte> input)
    {
        var inBuf = new byte[32];
        input.CopyTo(inBuf);
        var outBuf = new byte[4];
        lock (_io)
        {
            if (_handle.IsClosed) return -1;
            if (!DeviceIoControl(_handle, code, inBuf, inBuf.Length, outBuf, outBuf.Length, out _, 0))
                return -1;
        }
        return BitConverter.ToInt32(outBuf);
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
        lock (_io) _handle.Dispose();
    }

    // ---- SetupAPI ---------------------------------------------------------------------

    private static string? FindDevicePath()
    {
        var guid = InterfaceGuid;
        var set = SetupDiGetClassDevs(ref guid, 0, 0, DigcfPresent | DigcfDeviceInterface);
        if (set == -1 || set == 0) return null;
        try
        {
            var data = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, 0, ref data)) return null;
            SetupDiGetDeviceInterfaceDetail(set, ref data, 0, 0, out var required, 0);
            if (required <= 0) return null;
            var buffer = Marshal.AllocHGlobal(required);
            try
            {
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                return SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out _, 0)
                    ? Marshal.PtrToStringUni(buffer + 4)
                    : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    private const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80, FileFlagNoBuffering = 0x20000000, FileFlagWriteThrough = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData { public int CbSize; public Guid InterfaceClassGuid; public int Flags; public nint Reserved; }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(ref Guid classGuid, nint enumerator, nint parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint devInfo, ref Guid guid, uint index, ref SpDeviceInterfaceData data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref SpDeviceInterfaceData data, nint detail, int size, out int required, nint devInfo);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(nint set);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, nint overlapped);
}
