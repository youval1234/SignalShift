using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Sonora;

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource source;
    private readonly Dictionary<int, Action> actions = new();
    public HotkeyService(IntPtr handle) { source = HwndSource.FromHwnd(handle)!; source.AddHook(Hook); }
    public bool Register(int id, Shortcut shortcut, Action action)
    {
        if (shortcut.Key == 0 || shortcut.Key == 0x7B || shortcut.Modifiers == 0 || (shortcut.Modifiers & ~7u) != 0) return false;
        if (!RegisterHotKey(source.Handle, id, shortcut.Modifiers | 0x4000, shortcut.Key)) return false;
        actions[id] = action;
        return true;
    }
    public void Clear() { foreach (var id in actions.Keys) UnregisterHotKey(source.Handle, id); actions.Clear(); }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && actions.TryGetValue(wParam.ToInt32(), out var action)) { handled = true; action(); }
        return IntPtr.Zero;
    }
    public void Dispose() { Clear(); source.RemoveHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
