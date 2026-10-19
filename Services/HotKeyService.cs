using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StickyNoteWPF.Services;

/// <summary>
/// 全局热键服务：基于 Win32 RegisterHotKey，应用无焦点时也能响应。
/// 组合格式：如 "Ctrl+Alt+H"；修饰键支持 Ctrl / Alt / Shift / Win；主键为单字符（A-Z / 0-9 / F1-F12 等）。
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private const int HotKeyId = 0x5A11;

    private HwndSource? _source;
    private Action? _callback;
    private (uint Modifiers, uint Key)? _gesture;

    public bool IsRegistered => _source != null;

    /// <summary>尝试注册热键；组合已被占用或格式非法时返回 false。</summary>
    public bool Register(string combination, Action callback)
    {
        if (!TryParse(combination, out uint modifiers, out uint vk)) return false;
        if (_gesture == (modifiers, vk) && _source != null)
        {
            _callback = callback;
            return true;
        }

        HwndSource? candidate = null;
        try
        {
            candidate = new HwndSource(new HwndSourceParameters("StickyNoteWPF_HotKeyWindow")
            {
                Width = 0, Height = 0, WindowStyle = 0
            });
            if (!RegisterHotKey(candidate.Handle, HotKeyId, modifiers | 0x4000 /* MOD_NOREPEAT */, vk))
            {
                candidate.Dispose();
                return false;
            }
            // Keep the current registration until the replacement is known to work.
            Unregister();
            _source = candidate;
            _gesture = (modifiers, vk);
            _callback = callback;
            _source.AddHook(WndProc);
            return true;
        }
        catch (Exception ex)
        {
            candidate?.Dispose();
            Logger.LogException("HotKeyService.Register", ex);
            return false;
        }
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
        _gesture = null;
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

    public static bool TryParse(string combination, out uint modifiers, out uint vk)
        => HotKeyGesture.TryParse(combination, out modifiers, out vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
