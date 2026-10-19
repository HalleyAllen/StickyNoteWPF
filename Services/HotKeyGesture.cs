namespace StickyNoteWPF.Services;

public static class HotKeyGesture
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    // 解析 "Ctrl+Alt+H" 形式的组合为修饰键与虚拟键码
    public static bool TryParse(string combination, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(combination)) return false;

        // The OEM plus key needs a name because '+' also separates modifiers.
        if (combination.EndsWith("++", StringComparison.Ordinal))
            combination = combination[..^1] + "OemPlus";
        var parts = combination.Split('+', StringSplitOptions.TrimEntries);
        if (Array.Exists(parts, string.IsNullOrEmpty)) return false;
        if (parts.Length < 2) return false; // 至少一个修饰键 + 一个主键

        string? mainKey = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                case "meta":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    if (mainKey != null) return false; // 出现多个主键，非法
                    mainKey = part;
                    break;
            }
        }

        if (mainKey == null || modifiers == 0) return false; // 必须同时有主键和修饰键

        vk = (uint)KeyToVk(mainKey);
        return vk != 0;
    }

    private static int KeyToVk(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z') return c;   // A-Z 的 VK 码即 ASCII 码
            if (c is >= '0' and <= '9') return c;   // 0-9
            return c switch
            {
                ' ' => 0x20,
                '-' => 0xBD,
                '=' => 0xBB,
                ',' => 0xBC,
                '.' => 0xBE,
                '/' => 0xBF,
                ';' => 0xBA,
                '\'' => 0xDE,
                '[' => 0xDB,
                ']' => 0xDD,
                '\\' => 0xDC,
                '`' => 0xC0,
                _ => 0
            };
        }

        if (key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)
            && key.Length == 7 && key[6] is >= '0' and <= '9')
            return 0x60 + key[6] - '0';

        return key.ToUpperInvariant() switch
        {
            "SPACE" => 0x20, "OEMPLUS" => 0xBB,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            _ => 0
        };
    }

}
