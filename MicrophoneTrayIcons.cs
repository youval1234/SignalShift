using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SonarShift;

// Four icons for the lifetime of a tray host; native callbacks never allocate icons.
internal sealed class MicrophoneTrayIcons : IDisposable
{
    private static readonly string[] tooltips = BuildTooltips();
    private readonly Icon[] icons = { Draw(MicrophoneMuteState.Unavailable), Draw(MicrophoneMuteState.Live), Draw(MicrophoneMuteState.Muted), Draw(MicrophoneMuteState.Mixed) };
    internal Icon For(MicrophoneMuteState state) => icons[(int)state];
    internal static string Description(MicrophoneMuteState state) => state switch
    {
        MicrophoneMuteState.Live => "Microphone live",
        MicrophoneMuteState.Muted => "Microphone muted",
        MicrophoneMuteState.Mixed => "Microphones partly muted",
        _ => "Microphone unavailable"
    };
    internal static string Tooltip(MicrophoneMuteState state, bool conflict = false) => tooltips[(int)state + (conflict ? 4 : 0)];
    private static string[] BuildTooltips()
    {
        var result = new string[8];
        for (var index = 0; index < 8; index++) result[index] = "SignalShift · " + Description((MicrophoneMuteState)(index % 4)) + (index >= 4 ? " · Shortcut conflict" : "");
        return result;
    }
    private static Icon Draw(MicrophoneMuteState state)
    {
        using var image = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = state switch { MicrophoneMuteState.Live => Color.FromArgb(92, 230, 162), MicrophoneMuteState.Muted => Color.FromArgb(255, 99, 120), MicrophoneMuteState.Mixed => Color.FromArgb(255, 197, 79), _ => Color.FromArgb(156, 163, 184) };
        using var pen = new Pen(color, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var capsule = new GraphicsPath();
        capsule.AddArc(12, 3, 8, 8, 180, 180); capsule.AddArc(12, 11, 8, 8, 0, 180); capsule.CloseFigure();
        graphics.DrawPath(pen, capsule);
        graphics.DrawArc(pen, 7, 7, 18, 17, 0, 180);
        graphics.DrawLine(pen, 7, 12, 7, 15); graphics.DrawLine(pen, 25, 12, 25, 15);
        graphics.DrawLine(pen, 16, 24, 16, 29); graphics.DrawLine(pen, 11, 29, 21, 29);
        if (state == MicrophoneMuteState.Muted)
        {
            using var cutout = new Pen(Color.FromArgb(20, 22, 30), 6) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(cutout, 4, 4, 28, 28); graphics.DrawLine(pen, 4, 4, 28, 28);
        }
        else if (state is MicrophoneMuteState.Mixed or MicrophoneMuteState.Unavailable)
        {
            using var background = new SolidBrush(Color.FromArgb(20, 22, 30)); using var fill = new SolidBrush(color);
            graphics.FillEllipse(background, 20, 0, 12, 12); graphics.FillEllipse(fill, 22, 2, 8, 8);
            using var mark = new Pen(Color.FromArgb(20, 22, 30), 1.5f);
            if (state == MicrophoneMuteState.Mixed) { graphics.DrawLine(mark, 26, 3, 26, 6); graphics.DrawLine(mark, 26, 7, 26, 8); }
            else { graphics.DrawLine(mark, 24, 4, 28, 8); graphics.DrawLine(mark, 28, 4, 24, 8); }
        }
        var handle = image.GetHicon();
        try { using var temporary = Icon.FromHandle(handle); return (Icon)temporary.Clone(); }
        finally { DestroyIcon(handle); }
    }
    public void Dispose() { foreach (var icon in icons) icon.Dispose(); }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
