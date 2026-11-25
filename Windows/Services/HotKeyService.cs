using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StickyNoteWPF.Services;

/// <summary>
/// 全局热键服务：基于 Win32 RegisterHotKey，应用无焦点时也能响应。
/// 支持单键（如 F1）或 Ctrl+Alt+H 等组合；修饰键支持 Ctrl / Alt / Shift / Win。
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private const int HotKeyId = 0x5A11;

    private HwndSource? _source;
    private Action? _callback;

    public bool IsRegistered => _source != null;

    /// <summary>尝试注册热键；组合已被占用或格式非法时返回 false。</summary>
    public bool Register(string combination, Action callback)
    {
        Unregister();

        if (!TryParse(combination, out uint modifiers, out uint vk))
            return false;

        var parameters = new HwndSourceParameters("StickyNoteWPF_HotKeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _callback = callback;

        if (!RegisterHotKey(_source.Handle, HotKeyId, modifiers | MOD_NOREPEAT, vk))
        {
            Unregister();
            return false;
        }
        return true;
    }

    public void Unregister()
    {
        if (_source != null)
        {
            if (_source.Handle != IntPtr.Zero)
                UnregisterHotKey(_source.Handle, HotKeyId);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
        _callback = null;
    }

    public void Dispose() => Unregister();

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            _callback?.Invoke();
        }
        return IntPtr.Zero;
    }

    // 解析 "Ctrl+Alt+H" 形式的组合为修饰键与虚拟键码
    public static bool TryParse(string combination, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(combination)) return false;

        combination = combination.Trim();
        // “+”本身也是主键；Ctrl++ 对应 Ctrl 加键盘上的加号键。
        if (combination == "+" || combination.EndsWith("++", StringComparison.Ordinal)) combination = combination[..^1] + "=";
        var parts = combination.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrEmpty)) return false;

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

        if (mainKey == null) return false;

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
                '+' => 0xBB,
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

        return key.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            _ => 0
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
