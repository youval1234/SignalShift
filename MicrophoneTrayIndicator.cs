using System;
using Forms = System.Windows.Forms;

namespace SonarShift;

// A separate status icon. The host owns the shared context menu; this component
// owns only its NotifyIcon and four reusable microphone images.
internal sealed class MicrophoneTrayIndicator : IDisposable
{
    private readonly MicrophoneTrayIcons icons = new();
    private readonly Forms.NotifyIcon tray;
    private bool requestedVisible;
    internal MicrophoneTrayIndicator(Action openInterface, bool visible = true)
    {
        requestedVisible = visible;
        tray = new Forms.NotifyIcon { Icon = icons.For(MicrophoneMuteState.Unavailable), Text = MicrophoneTrayIcons.Tooltip(MicrophoneMuteState.Unavailable), Visible = visible };
        tray.DoubleClick += (_, _) => openInterface();
    }
    internal void SetMenu(Forms.ContextMenuStrip menu) => tray.ContextMenuStrip = menu;
    internal void SetVisible(bool value)
    {
        requestedVisible = value;
        if (tray.Visible != value) tray.Visible = value;
    }
    internal void Update(MicrophoneMuteState state, bool conflict = false)
    {
        var icon = icons.For(state);
        if (!ReferenceEquals(tray.Icon, icon)) tray.Icon = icon;
        var text = MicrophoneTrayIcons.Tooltip(state, conflict);
        if (tray.Text != text) tray.Text = text;
    }
    internal bool Matches(MicrophoneMuteState state, bool conflict = false) => tray.Visible == requestedVisible && ReferenceEquals(tray.Icon, icons.For(state)) && tray.Text == MicrophoneTrayIcons.Tooltip(state, conflict);
    internal bool IsSeparateFrom(Forms.NotifyIcon main) => main.Visible && tray.Visible == requestedVisible && !ReferenceEquals(main, tray) && main.Icon != null && !ReferenceEquals(main.Icon, tray.Icon);
    public void Dispose()
    {
        tray.Visible = false; tray.ContextMenuStrip = null; tray.Dispose(); icons.Dispose();
    }
}
