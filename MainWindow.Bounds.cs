using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SonarShift;

public sealed partial class MainWindow
{
    private void InitializeWorkAreaBounds()
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
        var initial = new MonitorBounds { Size = Marshal.SizeOf<MonitorBounds>() };
        if (GetMonitorInfo(MonitorFromWindow(source.Handle, 2), ref initial))
        {
            var scale = source.CompositionTarget.TransformToDevice;
            var availableWidth = (initial.Work.Right - initial.Work.Left) / scale.M11;
            var availableHeight = (initial.Work.Bottom - initial.Work.Top) / scale.M22;
            MinWidth = Math.Min(MinWidth, availableWidth); MinHeight = Math.Min(MinHeight, availableHeight);
            Width = Math.Min(Width, availableWidth); Height = Math.Min(Height, availableHeight);
        }
        source.AddHook((IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (message != 0x24) return IntPtr.Zero; // WM_GETMINMAXINFO
            var info = new MonitorBounds { Size = Marshal.SizeOf<MonitorBounds>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return IntPtr.Zero;
            var bounds = Marshal.PtrToStructure<MinMaxBounds>(l);
            bounds.MaxPosition = new(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
            bounds.MaxSize = new(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
            var dpi = source.CompositionTarget.TransformToDevice;
            bounds.MinTrack = new(Math.Min(bounds.MaxSize.X, (int)Math.Ceiling(MinWidth * dpi.M11)), Math.Min(bounds.MaxSize.Y, (int)Math.Ceiling(MinHeight * dpi.M22)));
            Marshal.StructureToPtr(bounds, l, false); handled = true;
            return IntPtr.Zero;
        });
    }
    internal void VerifyMaximizedWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new MonitorBounds { Size = Marshal.SizeOf<MonitorBounds>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info) || !GetWindowRect(hwnd, out var rect)) throw new InvalidOperationException("Could not inspect maximized window bounds.");
        if (rect.Left < info.Work.Left || rect.Top < info.Work.Top || rect.Right > info.Work.Right || rect.Bottom > info.Work.Bottom)
            throw new InvalidOperationException("Maximized window overlaps the monitor's reserved work area.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct PixelPoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct PixelRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorBounds { public int Size; public PixelRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxBounds { public PixelPoint Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorBounds info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out PixelRect rect);
}
