using System.Runtime.InteropServices;
using System.Text.Json;

namespace DupliXon.Media.Input;

/// <summary>
/// Turns remote input events (touch, pointer, keyboard from the receiver) into real Windows mouse/keyboard input
/// with SendInput. Coordinates arrive normalized (0..1) to the shared monitor and are mapped onto it.
/// </summary>
/// <remarks>
/// Protocol (JSON): m=move, d/u=button down/up (b: 0 left, 2 right), w=wheel (dx, dy in px),
/// k=key by KeyboardEvent.code (d: down), txt=unicode text.
/// Windows blocks injection into elevated (administrator) windows unless DupliXon also runs elevated.
/// </remarks>
public static class InputInjector
{
    public static void Handle(DisplayOutput display, ReadOnlySpan<byte> json)
    {
        using var doc = JsonDocument.Parse(json.ToArray());
        var e = doc.RootElement;
        switch (e.GetProperty("t").GetString())
        {
            case "m": Move(display, e); break;
            case "d": Move(display, e); Button(e, down: true); break;
            case "u": Move(display, e); Button(e, down: false); break;
            case "w": Wheel(e); break;
            case "k": Key(e); break;
            case "txt": Text(e.GetProperty("s").GetString() ?? ""); break;
        }
    }

    // ---- Mouse --------------------------------------------------------------------------

    private static void Move(DisplayOutput d, JsonElement e)
    {
        if (!e.TryGetProperty("x", out var xp) || !e.TryGetProperty("y", out var yp)) return;
        var x = d.Left + Math.Clamp(xp.GetDouble(), 0, 1) * (d.Width - 1);
        var y = d.Top + Math.Clamp(yp.GetDouble(), 0, 1) * (d.Height - 1);

        // Absolute coordinates are normalized over the whole virtual desktop (all monitors).
        var vx = GetSystemMetrics(SmXVirtualScreen);
        var vy = GetSystemMetrics(SmYVirtualScreen);
        var vw = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen) - 1);
        var vh = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen) - 1);
        Mouse(MouseMove | MouseAbsolute | MouseVirtualDesk,
            (int)Math.Round((x - vx) * 65535 / vw), (int)Math.Round((y - vy) * 65535 / vh));
    }

    private static void Button(JsonElement e, bool down)
    {
        var right = e.TryGetProperty("b", out var b) && b.GetInt32() == 2;
        var flag = right ? (down ? MouseRightDown : MouseRightUp) : (down ? MouseLeftDown : MouseLeftUp);
        Mouse(flag, 0, 0);
    }

    private static void Wheel(JsonElement e)
    {
        var dy = e.TryGetProperty("dy", out var y) ? y.GetDouble() : 0;
        var dx = e.TryGetProperty("dx", out var x) ? x.GetDouble() : 0;
        // Browser deltas are pixels (down = positive); Windows wheel: 120 per notch, up = positive.
        if (Math.Abs(dy) >= 1) Mouse(MouseWheel, 0, 0, (int)Math.Round(-dy * 1.2));
        if (Math.Abs(dx) >= 1) Mouse(MouseHWheel, 0, 0, (int)Math.Round(dx * 1.2));
    }

    private static void Mouse(uint flags, int dx, int dy, int data = 0)
    {
        var input = new Input { Type = InputMouse };
        input.U.Mi = new MouseInput { Dx = dx, Dy = dy, MouseData = data, Flags = flags };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    // ---- Keyboard -----------------------------------------------------------------------

    private static void Key(JsonElement e)
    {
        var code = e.GetProperty("c").GetString() ?? "";
        var down = e.TryGetProperty("d", out var d) && d.GetBoolean();
        var vk = KeyMap.ToVirtualKey(code);
        if (vk == 0) return;
        var flags = down ? 0u : KeyUp;
        if (KeyMap.IsExtended(vk)) flags |= KeyExtended;
        var input = new Input { Type = InputKeyboard };
        input.U.Ki = new KeybdInput { Vk = vk, Flags = flags };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private static void Text(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var ch in text)
        {
            if (ch == '\n' || ch == '\r') { Tap(inputs, 0x0D); continue; }
            foreach (var up in new[] { false, true })
            {
                var i = new Input { Type = InputKeyboard };
                i.U.Ki = new KeybdInput { Scan = ch, Flags = KeyUnicode | (up ? KeyUp : 0) };
                inputs.Add(i);
            }
        }
        if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
    }

    private static void Tap(List<Input> list, ushort vk)
    {
        foreach (var up in new[] { false, true })
        {
            var i = new Input { Type = InputKeyboard };
            i.U.Ki = new KeybdInput { Vk = vk, Flags = up ? KeyUp : 0 };
            list.Add(i);
        }
    }

    // ---- Win32 --------------------------------------------------------------------------

    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseMove = 0x0001, MouseLeftDown = 0x0002, MouseLeftUp = 0x0004,
        MouseRightDown = 0x0008, MouseRightUp = 0x0010, MouseWheel = 0x0800, MouseHWheel = 0x1000,
        MouseVirtualDesk = 0x4000, MouseAbsolute = 0x8000;
    private const uint KeyExtended = 0x0001, KeyUp = 0x0002, KeyUnicode = 0x0004;
    private const int SmXVirtualScreen = 76, SmYVirtualScreen = 77, SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int Dx, Dy, MouseData; public uint Flags, Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput { public ushort Vk, Scan; public uint Flags, Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mi;
        [FieldOffset(0)] public KeybdInput Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion U; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
