namespace DupliXon.Media.Input;

/// <summary>Maps W3C KeyboardEvent.code values to Windows virtual-key codes.</summary>
public static class KeyMap
{
    private static readonly Dictionary<string, ushort> Named = new(StringComparer.Ordinal)
    {
        ["Enter"] = 0x0D, ["NumpadEnter"] = 0x0D, ["Backspace"] = 0x08, ["Tab"] = 0x09, ["Escape"] = 0x1B,
        ["Space"] = 0x20, ["CapsLock"] = 0x14, ["Delete"] = 0x2E, ["Insert"] = 0x2D,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["ArrowLeft"] = 0x25, ["ArrowUp"] = 0x26, ["ArrowRight"] = 0x27, ["ArrowDown"] = 0x28,
        ["ShiftLeft"] = 0xA0, ["ShiftRight"] = 0xA1, ["ControlLeft"] = 0xA2, ["ControlRight"] = 0xA3,
        ["AltLeft"] = 0xA4, ["AltRight"] = 0xA5,
        // Command (⌘) on iPad keyboards acts as Ctrl, so ⌘C/⌘V/⌘Z work as on a Mac.
        ["MetaLeft"] = 0xA2, ["MetaRight"] = 0xA3, ["OSLeft"] = 0xA2, ["OSRight"] = 0xA3,
        ["Minus"] = 0xBD, ["Equal"] = 0xBB, ["BracketLeft"] = 0xDB, ["BracketRight"] = 0xDD,
        ["Backslash"] = 0xDC, ["Semicolon"] = 0xBA, ["Quote"] = 0xDE, ["Comma"] = 0xBC,
        ["Period"] = 0xBE, ["Slash"] = 0xBF, ["Backquote"] = 0xC0, ["IntlBackslash"] = 0xE2,
        ["PrintScreen"] = 0x2C, ["ContextMenu"] = 0x5D,
        ["NumpadAdd"] = 0x6B, ["NumpadSubtract"] = 0x6D, ["NumpadMultiply"] = 0x6A, ["NumpadDivide"] = 0x6F,
        ["NumpadDecimal"] = 0x6E,
        ["AudioVolumeUp"] = 0xAF, ["AudioVolumeDown"] = 0xAE, ["AudioVolumeMute"] = 0xAD, ["MediaPlayPause"] = 0xB3,
    };

    public static ushort ToVirtualKey(string code)
    {
        if (Named.TryGetValue(code, out var vk)) return vk;
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal)) return code[3];       // KeyA → 'A'
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal)) return code[5];     // Digit1 → '1'
        if (code.Length == 7 && code.StartsWith("Numpad", StringComparison.Ordinal) && char.IsDigit(code[6]))
            return (ushort)(0x60 + (code[6] - '0'));
        if (code.Length is 2 or 3 && code[0] == 'F' && int.TryParse(code.AsSpan(1), out var f) && f is >= 1 and <= 24)
            return (ushort)(0x6F + f);
        return 0;
    }

    public static bool IsExtended(ushort vk) =>
        vk is >= 0x21 and <= 0x2E || vk is 0xA3 or 0xA5 or 0x6F or 0x5D;
}
