using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace SonarShift;

// Small native desktop popup. WPF is never loaded just to confirm a background switch.
internal sealed class BackgroundFeedback : IDisposable
{
    private Popup? popup;
    private readonly Forms.Timer soundDelay = new() { Interval = 120 };
    private AudioFlow pending;
    private readonly Dictionary<AudioFlow, GCHandle> waves = new();
    internal BackgroundFeedback() { soundDelay.Tick += (_, _) => { soundDelay.Stop(); PlayNow(pending); }; }
    internal void Play(AudioFlow flow) { CancelSound(); pending = flow; soundDelay.Start(); }
    private void PlayNow(AudioFlow flow)
    {
        if (!waves.TryGetValue(flow, out var pinned))
        {
            using var source = typeof(BackgroundFeedback).Assembly.GetManifestResourceStream(flow == AudioFlow.Output ? "SonarShift.OutputSound" : "SonarShift.InputSound")!;
            using var buffer = new MemoryStream(); source.CopyTo(buffer);
            pinned = GCHandle.Alloc(buffer.ToArray(), GCHandleType.Pinned); waves.Add(flow, pinned);
        }
        PlaySound(pinned.AddrOfPinnedObject(), IntPtr.Zero, 0x0001 | 0x0002 | 0x0004);
    }
    internal void CancelSound() { soundDelay.Stop(); PlaySound(IntPtr.Zero, IntPtr.Zero, 0); }
    internal void Present(string title, string message, AudioFlow? flow) => (popup ??= new Popup()).Present(title, message, flow);
    internal bool IsNotificationVisible => popup?.Visible == true;
    internal string? CurrentMessage => popup?.CurrentMessage;
    internal IntPtr NotificationHandle => popup?.Handle ?? IntPtr.Zero;
    internal int ActivationCount => popup?.ActivationCount ?? 0;
    internal static void ExportPreview(string path) { using var preview = new Popup(); preview.Export(path); }
    internal void Dismiss() => popup?.Dismiss();
    public void Dispose() { CancelSound(); soundDelay.Dispose(); popup?.Dispose(); foreach (var wave in waves.Values) wave.Free(); waves.Clear(); }
    [DllImport("winmm.dll")] private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    private sealed class Popup : Forms.Form
    {
        private readonly Forms.Timer animation = new() { Interval = 16 };
        private readonly Stopwatch elapsed = new();
        private readonly Font heading = new("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font text = new("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font brand = new("Segoe UI", 9, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font icon = new("Segoe MDL2 Assets", 23, FontStyle.Regular, GraphicsUnit.Pixel);
        private string title = "", message = "", glyph = "";
        internal string CurrentMessage => message;
        internal int ActivationCount { get; private set; }
        protected override bool ShowWithoutActivation => true;
        protected override Forms.CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x20; return p; } }
        internal Popup()
        {
            FormBorderStyle = Forms.FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            BackColor = Color.FromArgb(23, 25, 35); StartPosition = Forms.FormStartPosition.Manual;
            AutoScaleMode = Forms.AutoScaleMode.None;
            Activated += (_, _) => ActivationCount++;
            animation.Tick += (_, _) => { if (elapsed.ElapsedMilliseconds >= 1760) Dismiss(); else { if (elapsed.ElapsedMilliseconds > 1600 && AnimationsEnabled()) Opacity = Math.Max(0, 1 - (elapsed.ElapsedMilliseconds - 1600) / 160.0); Invalidate(); } };
        }
        internal void Present(string caption, string device, AudioFlow? flow)
        {
            title = caption.ToUpperInvariant(); message = device; glyph = flow == AudioFlow.Output ? "\uE7F5" : flow == AudioFlow.Input ? "\uE720" : "\uE8A9";
            AccessibleName = caption + ": " + device;
            elapsed.Restart(); Opacity = 1;
            var foreground = GetForegroundWindow();
            var screen = Forms.Screen.FromHandle(foreground);
            var scale = Math.Max(1, GetDpiForWindow(foreground) / 96f);
            var area = screen.WorkingArea;
            Width = Math.Min(area.Width, (int)(384 * scale)); Height = Math.Min(area.Height, (int)(102 * scale));
            Left = area.Left + (area.Width - Width) / 2; Top = Math.Max(area.Top, area.Bottom - Height - (int)(44 * scale));
            using var path = Rounded(new RectangleF(0, 0, Width, Height), 18 * scale);
            var old = Region; Region = new Region(path); old?.Dispose();
            if (!Visible) Show();
            Invalidate(); animation.Start();
        }
        internal void Dismiss() { animation.Stop(); Hide(); elapsed.Reset(); Opacity = 1; }
        internal void Export(string path)
        {
            title = "OUTPUT SWITCHED"; message = "SteelSeries Sonar — Aux"; glyph = "\uE7F5"; Size = new Size(384, 102);
            using var image = new Bitmap(Width, Height); DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(Width / 384f, Height / 102f);
            using var outline = new Pen(Color.FromArgb(59, 53, 79));
            using var path = Rounded(new RectangleF(0.5f, 0.5f, 383, 101), 18); g.DrawPath(outline, path);
            using var iconPath = Rounded(new RectangleF(20, 25, 44, 44), 14);
            using var iconBackground = new SolidBrush(Color.FromArgb(48, 40, 64)); g.FillPath(iconBackground, iconPath);
            using var accent = new SolidBrush(Color.FromArgb(184, 163, 255));
            g.DrawString(glyph, icon, accent, new PointF(29, 34));
            g.DrawString(title, heading, accent, new PointF(76, 20));
            using var muted = new SolidBrush(Color.FromArgb(116, 120, 143)); g.DrawString("SONARSHIFT", brand, muted, new PointF(304, 20));
            using var foreground = new SolidBrush(Color.FromArgb(243, 240, 252));
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit };
            g.DrawString(message, text, foreground, new RectangleF(76, 39, 288, 40), format);
            using var track = new SolidBrush(Color.FromArgb(41, 44, 60)); g.FillRectangle(track, 76, 80, 288, 2);
            using var mint = new SolidBrush(Color.FromArgb(139, 226, 197)); g.FillRectangle(mint, 76, 80, (float)(288 * Math.Max(0, 1 - elapsed.ElapsedMilliseconds / 1600.0)), 2);
        }
        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var p = new GraphicsPath(); var diameter = radius * 2;
            p.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90); p.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            p.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90); p.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90); p.CloseFigure(); return p;
        }
        private static bool AnimationsEnabled() => SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled;
        protected override void Dispose(bool disposing) { if (disposing) { animation.Dispose(); heading.Dispose(); text.Dispose(); brand.Dispose(); icon.Dispose(); } base.Dispose(disposing); }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
    }
}
