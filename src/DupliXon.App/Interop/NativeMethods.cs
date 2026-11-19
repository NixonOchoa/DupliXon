using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DupliXon.App.Interop;

/// <summary>Windows 11 materials (Acrylic/Mica), rounded corners and monitor enumeration.</summary>
internal static partial class NativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int BackdropAcrylic = 3;
    private const int CornerRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    /// <summary>Applies the frosted "liquid glass" backdrop. Returns false on systems without it (Windows 10).</summary>
    public static bool ApplyGlass(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;

        // With the frame extended into the client area, DWM would paint its own (ghost) minimize/
        // maximize/close buttons behind ours. Dropping WS_SYSMENU stops that; we draw our own.
        const int GwlStyle = -16;
        const long WsSysMenu = 0x00080000;
        SetWindowLongPtr(hwnd, GwlStyle, (nint)((long)GetWindowLongPtr(hwnd, GwlStyle) & ~WsSysMenu));

        var dark = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        var corner = CornerRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

        if (Environment.OSVersion.Version.Build < 22621) return false;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var backdrop = BackdropAcrylic;
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;
    }
}

