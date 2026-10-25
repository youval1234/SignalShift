using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SonarShift;

public sealed partial class MainWindow : Window
{
    private string? activeProfileId;
    private readonly AudioService audio = new();
    private readonly MicrophoneMuteService microphoneMute;
    private readonly bool useDemoDevices;
    private readonly SettingsStore store = new();
    private readonly SoundCueService soundCues = new();
    private SwitchNotification? switchNotification;
    public Settings Preferences { get; private set; }
    private readonly StackPanel page = new();
    private readonly StackPanel navigation = new();
    private readonly TextBlock status = Text("Ready when you are", 12, Muted);
    private readonly TextBlock trayStatus = Text("Shortcuts active", 12, Mint);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<AudioFlow, (TextBlock Title, TextBlock Detail)> activeLabels = new();
    private readonly Dictionary<string, (Border Card, Button Button, Border Badge)> deviceRows = new();
    private string deviceTopology = "";
    private AudioDeviceNotifications? deviceNotifications;
    private System.IO.FileSystemWatcher? signalWatcher;
    private bool refreshPending;
    private AudioChangeKind pendingDeviceChange;
    private bool hostExiting;
    private Forms.NotifyIcon? tray;
    private HotkeyService? hotkeys;
    private readonly List<string> hotkeyErrors = new();
    private IReadOnlyList<AudioDevice> outputs = Array.Empty<AudioDevice>();
    private IReadOnlyList<AudioDevice> inputs = Array.Empty<AudioDevice>();
    private string currentPage = "Devices";
    private AudioFlow flow;
    private string search = "";
    private StackPanel? rows;
    private TextBlock? cycleCount;
    private bool quitting;
    private bool paused;
    private bool audioUiReady;
    private DateTime lastShortcutRetry = DateTime.MinValue;
    private static readonly Brush Muted = Brush("#9297AC");
    private static readonly Brush Mint = Brush("#8BE2C5");
    private static readonly Brush Purple = Brush("#B8A3FF");

    public MainWindow(bool previewMode = false, string? previewTheme = null, string previewPage = "Devices")
    {
        StartupTrace.Mark("window-start");
        useDemoDevices = previewMode;
        microphoneMute = new(() => QueueDeviceRefresh(AudioChangeKind.Mute));
        Preferences = previewMode ? PreviewPreferences() : store.Load();
        if (previewMode && previewTheme is "Dark" or "Light") Preferences.Theme = previewTheme;
        soundCues.PlaybackFailed += ex => SetStatus("Confirmation sound unavailable: " + ex.Message, true);
        Title = "SignalShift — Your sound, one shortcut away";
        Width = 1160; Height = 900; MinWidth = 960; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ThemePalette.Apply(Preferences.Theme);
        UseLayoutRounding = true;
        Background = Brush("#0D0F15"); Foreground = Brush("#F4F4FA");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/SonarShift.png"));
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, CornerRadius = new CornerRadius(12), GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6) });
        BuildShell();
        StartupTrace.Mark("shell-ready");
        void InitializeAudioUi()
        {
            if (hostExiting || backgroundClose || quitting) return;
            try
            {
                RefreshDevices();
                StartupTrace.Mark("devices-ready");
                if (!Preferences.Initialized)
                {
                    foreach (var d in outputs.Where(d => d.IsDefault)) Preferences.Outputs.Add(d.Id);
                    foreach (var d in inputs.Where(d => d.IsDefault)) Preferences.Inputs.Add(d.Id);
                    Preferences.Initialized = true; Save();
                }
                if (!previewMode)
                {
                    if (!Program.IsInterface) SetupTray();
                    RegisterShortcuts();
                    try { deviceNotifications = new AudioDeviceNotifications(kind => QueueDeviceRefresh(kind)); }
                    catch { timer.Interval = TimeSpan.FromSeconds(30); }
                    timer.Tick += (_, _) => Tick();
                    // The timer is only needed when audio callbacks are unavailable or a shortcut conflicts.
                    if (deviceNotifications == null || hotkeyErrors.Count > 0) timer.Start();
                    if (!Program.IsInterface)
                    {
                        signalWatcher = new FileSystemWatcher(SettingsStore.DirectoryPath, "show.signal") { EnableRaisingEvents = true };
                        signalWatcher.Created += (_, _) => Dispatcher.BeginInvoke(new Action(ShowWindow));
                    }
                }
                Navigate(previewMode ? previewPage : "Devices");
                audioUiReady = true;
                StartupTrace.Mark("page-ready");
                if (themeToggle != null) themeToggle.IsEnabled = true;
                if (hotkeyErrors.Count == 0) SetStatus("Ready when you are");
            }
            catch (Exception ex)
            {
                StartupTrace.Mark("startup-error");
                MessageBox.Show(this, "Audio controls could not finish loading. Your settings are preserved.\n\n" + ex.Message, "SignalShift", MessageBoxButton.OK, MessageBoxImage.Information);
                backgroundClose = true; quitting = !Program.IsInterface; Close();
            }
        }
        SourceInitialized += (_, _) =>
        {
            InitializeWorkAreaBounds();
            if (!previewMode) hotkeys = new(new WindowInteropHelper(this).Handle);
            if (!previewMode)
            {
                var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
                source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message == 0x8001) { ShowWindow(); handled = true; }
                    if (message == 0x8002) { hostExiting = true; quitting = true; Close(); handled = true; }
                    if (message == 0x8003) { backgroundClose = true; Close(); handled = true; }
                    return IntPtr.Zero;
                });
            }
            if (previewMode) InitializeAudioUi();
            else
            {
                if (themeToggle != null) themeToggle.IsEnabled = false;
                Navigate("Devices", loading: true);
                EventHandler? rendered = null;
                rendered = (_, _) =>
                {
                    ContentRendered -= rendered;
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(InitializeAudioUi));
                };
                ContentRendered += rendered;
            }
        };
        Closing += OnClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) MinimizeToTray(); };
    }
    private void BuildShell()
    {
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new Grid { Background = Brush("#11131B") };
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(116) });
        sidebar.RowDefinitions.Add(new RowDefinition());
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 38, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var logo = new Image { Source = Icon, Width = 36, Height = 36, Margin = new Thickness(0, 0, 11, 0) };
        logo.VerticalAlignment = VerticalAlignment.Center;
        var brandText = new StackPanel();
        brandText.Children.Add(Text("SignalShift", 23, null, FontWeights.SemiBold));
        var version = Text("Version " + AppUpdater.Current.ToString(3), 12, Muted);
        version.Margin = new Thickness(0, 4, 0, 0);
        AutomationPropertiesName(version, "SignalShift version " + AppUpdater.Current.ToString(3));
        brandText.Children.Add(version);
        brand.Children.Add(logo); brand.Children.Add(brandText);
        sidebar.Children.Add(brand);
        navigation.Margin = new Thickness(16, 0, 16, 0); Grid.SetRow(navigation, 1); sidebar.Children.Add(navigation);
        var bottom = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        themeToggle = Button("", ToggleTheme);
        themeToggle.Margin = new Thickness(0, 0, 0, 22);
        UpdateThemeToggle(); bottom.Children.Add(themeToggle);
        bottom.Children.Add(Text("ALWAYS WITHIN REACH", 10, Muted, FontWeights.SemiBold));
        var trayLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 20) };
        trayLine.Children.Add(Text("●  ", 12, Mint)); trayLine.Children.Add(trayStatus); bottom.Children.Add(trayLine);
        Grid.SetRow(bottom, 2); sidebar.Children.Add(bottom); shell.Children.Add(sidebar);
        var main = new Grid(); Grid.SetColumn(main, 1);
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        main.RowDefinitions.Add(new RowDefinition());
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        var chrome = CaptionControls();
        main.Children.Add(chrome);
        var scroll = new ScrollViewer { Content = page, Padding = new Thickness(36, 10, 36, 16) }; Grid.SetRow(scroll, 1); main.Children.Add(scroll);
        var footer = new Border { BorderBrush = Brush("#20232E"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(36, 10, 36, 0), Child = status };
        Grid.SetRow(footer, 2); main.Children.Add(footer); shell.Children.Add(main); Content = shell;
    }
    private void Navigate(string name, bool loading = false)
    {
        if (!loading && name == "Devices" && !useDemoDevices) microphoneMute.Refresh();
        currentPage = name; rows = null; microphoneMuteButton = null; microphoneMuteLabel = null; page.Children.Clear(); navigation.Children.Clear(); profileRows.Clear();
        navigation.Children.Add(Text("WORKSPACE", 10, Muted, FontWeights.SemiBold));
        foreach (var item in new[] { ("Devices", "\uE7F4"), ("Shortcuts", "\uE765"), ("Profiles", "\uE8A9"), ("Settings", "\uE713") })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Glyph(item.Item2, 17, item.Item1 == name ? Purple : Muted));
            content.Children.Add(new TextBlock { Text = item.Item1, Margin = new Thickness(13, 0, 0, 0), FontSize = 14, Foreground = item.Item1 == name ? Brush("#E1D7FF") : Muted });
            var b = Button(content, () => Navigate(item.Item1)); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(16, 13, 16, 13); b.Margin = new Thickness(0, 9, 0, 0); b.BorderThickness = new Thickness(0); b.Background = item.Item1 == name ? Brush("#29223F") : Brushes.Transparent;
            // The template centers its presenter; fixed width content keeps navigation aligned.
            content.Width = 135; b.IsEnabled = !loading; navigation.Children.Add(b);
        }
        if (loading) { Heading("YOUR AUDIO, SIMPLIFIED", "Make yourself heard.", "Loading your audio devices…"); SetStatus("Starting audio controls…"); return; }
        switch (name) { case "Devices": BuildDevices(); break; case "Shortcuts": BuildShortcuts(); break; case "Profiles": BuildProfiles(); break; default: BuildSettings(); break; }
        if (!useDemoDevices) Motion.Enter(page);
    }
    private void Heading(string eyebrow, string title, string subtitle)
    {
        page.Children.Add(Text(eyebrow.ToUpperInvariant(), 10, Purple, FontWeights.SemiBold));
        page.Children.Add(new TextBlock { Text = title, FontSize = 29, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 7) });
        page.Children.Add(new TextBlock { Text = subtitle, Foreground = Muted, FontSize = 13, Margin = new Thickness(0, 0, 0, 24), TextWrapping = TextWrapping.Wrap });
    }
    private void BuildDevices()
    {
        activeLabels.Clear();
        deviceTopology = Topology();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var refresh = Button("↻   Refresh", () => { RefreshDevices(); Navigate("Devices"); SetStatus("Audio devices refreshed"); }); DockPanel.SetDock(refresh, Dock.Right); header.Children.Add(refresh);
        var title = new StackPanel(); title.Children.Add(Text("YOUR AUDIO, SIMPLIFIED", 10, Purple, FontWeights.SemiBold)); title.Children.Add(new TextBlock { Text = "Make yourself heard.", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 0) }); header.Children.Add(title); page.Children.Add(header);
        var hero = new Grid(); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) }); hero.ColumnDefinitions.Add(new ColumnDefinition());
        var copy = new StackPanel(); copy.Children.Add(Text("A LITTLE LESS FRICTION", 10, Brush("#C6B4F8"), FontWeights.SemiBold));
        copy.Children.Add(new TextBlock { Text = "Switch your sound.\nStay in your flow.", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6), LineHeight = 34 });
        copy.Children.Add(Text("Your favorite devices. One shortcut.", 13, Brush("#BFB4D7")));
        var action = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        action.Children.Add(Button("Next output   →", () => Cycle(AudioFlow.Output), true));
        action.Children.Add(new TextBlock { Text = Preferences.OutputShortcut.Label, Foreground = Brush("#CEBFE9"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0) }); copy.Children.Add(action); hero.Children.Add(copy);
        var visual = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 70, Margin = new Thickness(0, 0, 0, 18) };
        foreach (var h in new[] { 14, 29, 46, 65, 38, 56, 70, 46, 31, 50, 22, 12 }) bars.Children.Add(new Border { Width = 7, Height = h, CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Background = new LinearGradientBrush(Color.FromRgb(168, 148, 251), Color.FromRgb(125, 226, 195), 90) });
        visual.Children.Add(bars); visual.Children.Add(Text("GOOD SOUND. ZERO DETOURS.", 9, Brush("#B7ABCF"), FontWeights.SemiBold));
        Grid.SetColumn(visual, 1); hero.Children.Add(visual);
        var heroCard = Card(hero, 22); heroCard.Background = new LinearGradientBrush(ThemePalette.Color("#2E2146"), ThemePalette.Color("#1A1F2E"), 20); heroCard.BorderBrush = Brush("#493760"); heroCard.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(heroCard);
        var active = new Grid(); active.ColumnDefinitions.Add(new ColumnDefinition()); active.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var f in new[] { AudioFlow.Output, AudioFlow.Input })
        {
            var device = Devices(f).FirstOrDefault(d => d.IsDefault);
            var p = new StackPanel(); p.Children.Add(Text(f == AudioFlow.Output ? "↗  CURRENT OUTPUT" : "↙  CURRENT INPUT", 10, f == AudioFlow.Output ? Mint : Purple, FontWeights.SemiBold));
            var activeTitle = new TextBlock { Text = device?.Title ?? "No device connected", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = device?.Name };
            var activeDetail = new TextBlock { Text = device?.Detail ?? "Connect an audio device to begin", Foreground = Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            p.Children.Add(activeTitle); p.Children.Add(activeDetail); activeLabels[f] = (activeTitle, activeDetail);
            if (f == AudioFlow.Input) BuildMicrophoneMuteControls(p);
            var card = Card(p, 16); card.Margin = f == AudioFlow.Output ? new Thickness(0, 0, 7, 16) : new Thickness(7, 0, 0, 16); Grid.SetColumn(card, (int)f); active.Children.Add(card);
        }
        page.Children.Add(active);
        var tabs = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var selector = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var f in new[] { AudioFlow.Output, AudioFlow.Input })
        {
            var button = Button((f == AudioFlow.Output ? "Playback" : "Recording") + "  " + Devices(f).Count, () => { flow = f; search = ""; Navigate("Devices"); });
            button.Margin = new Thickness(0, 0, 7, 0); button.Background = flow == f ? Brush("#302944") : Brushes.Transparent; button.Foreground = flow == f ? Brush("#DDCEFF") : Muted; button.BorderBrush = flow == f ? Brush("#504266") : Brushes.Transparent; selector.Children.Add(button);
        }
        var searchBox = new TextBox { Text = search, Width = 170, ToolTip = "Filter devices by name", VerticalAlignment = VerticalAlignment.Center }; AutomationPropertiesName(searchBox, "Search audio devices");
        var searchField = new Grid { VerticalAlignment = VerticalAlignment.Center }; searchField.Children.Add(searchBox);
        var placeholder = new TextBlock { Text = "⌕  Search devices", Foreground = Muted, FontSize = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed }; searchField.Children.Add(placeholder);
        searchBox.TextChanged += (_, _) => { search = searchBox.Text; placeholder.Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed; PopulateRows(); }; DockPanel.SetDock(searchField, Dock.Right); tabs.Children.Add(searchField); tabs.Children.Add(selector); page.Children.Add(tabs);
        var hint = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; cycleCount = Text("", 11, Mint); DockPanel.SetDock(cycleCount, Dock.Right); hint.Children.Add(cycleCount); hint.Children.Add(Text("Check devices to include them in your shortcut cycle.", 11, Muted)); page.Children.Add(hint);
        rows = new StackPanel(); page.Children.Add(rows); PopulateRows();
    }
    private void PopulateRows()
    {
        if (rows == null) return;
        rows.Children.Clear(); deviceRows.Clear(); UpdateCycleCount();
        var list = Devices(flow).Where(d => d.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => Selected(flow).Contains(d.Id)).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) { rows.Children.Add(Card(Text(Devices(flow).Count == 0 ? "No active devices. Connect a device, then refresh." : "No devices match your search.", 13, Muted), 24)); return; }
        foreach (var device in list)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(39) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var check = new CheckBox { IsChecked = Selected(flow).Contains(device.Id), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Include " + device.Title + " in cycle" }; AutomationPropertiesName(check, "Include " + device.Title + " in cycle");
            check.Checked += (_, _) => Select(device, true); check.Unchecked += (_, _) => Select(device, false); row.Children.Add(check);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            info.Children.Add(new TextBlock { Text = device.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = device.Name });
            info.Children.Add(new TextBlock { Text = device.Detail, FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }); Grid.SetColumn(info, 1); row.Children.Add(info);
            var badge = new Border { Child = Text("●  DEFAULT", 9, Mint, FontWeights.SemiBold), Background = Brush("#1D3531"), CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 5, 9, 5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Visibility = device.IsDefault ? Visibility.Visible : Visibility.Collapsed }; Grid.SetColumn(badge, 2); row.Children.Add(badge);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var test = Button("Test", () => ShowDeviceTest(device)); test.Padding = new Thickness(12, 7, 12, 7); test.Margin = new Thickness(0, 0, 8, 0);
            test.ToolTip = device.Flow == AudioFlow.Output ? "Play a sound through this device" : "Record a short clip and hear your microphone";
            AutomationPropertiesName(test, "Test " + device.Name); actions.Children.Add(test);
            var use = Button(device.IsDefault ? "Active" : "Switch  ↗", () => Switch(device)); use.IsEnabled = !device.IsDefault; use.Padding = new Thickness(12, 7, 12, 7); use.MinWidth = 78; actions.Children.Add(use); Grid.SetColumn(actions, 3); row.Children.Add(actions);
            var card = Card(row, 15); card.Margin = new Thickness(0, 0, 0, 8); if (device.IsDefault) { card.BorderBrush = Brush("#3C4950"); card.Background = Brush("#192126"); } rows.Children.Add(card);
            deviceRows[device.Id] = (card, use, badge);
        }
    }
    private string Topology() => string.Join("|", outputs.Concat(inputs).Select(d => d.Flow + d.Id + d.Name));
    private void RefreshDeviceVisuals()
    {
        if (currentPage != "Devices" || !IsVisible) return;
        if (deviceTopology != Topology()) { Navigate("Devices"); return; }
        foreach (var flow in new[] { AudioFlow.Output, AudioFlow.Input })
        {
            var current = Devices(flow).FirstOrDefault(d => d.IsDefault);
            if (activeLabels.TryGetValue(flow, out var labels))
            {
                labels.Title.Text = current?.Title ?? "No device connected"; labels.Title.ToolTip = current?.Name;
                labels.Detail.Text = current?.Detail ?? "Connect an audio device to begin";
            }
        }
        foreach (var device in Devices(flow)) if (deviceRows.TryGetValue(device.Id, out var view))
        {
            view.Button.IsEnabled = !device.IsDefault; view.Button.Content = device.IsDefault ? "Active" : "Switch  ↗";
            view.Badge.Visibility = device.IsDefault ? Visibility.Visible : Visibility.Collapsed;
            view.Card.BorderBrush = Brush(device.IsDefault ? "#3C4950" : "#292D3A");
            view.Card.Background = Brush(device.IsDefault ? "#192126" : "#171A23");
        }
    }
    private void UpdateCycleCount() { if (cycleCount != null) cycleCount.Text = Devices(flow).Count(d => Selected(flow).Contains(d.Id)) + " in cycle"; }
    private void Select(AudioDevice device, bool selected) { if (selected) Selected(device.Flow).Add(device.Id); else Selected(device.Flow).Remove(device.Id); Save(); UpdateCycleCount(); SetStatus("Cycle updated · " + Selected(device.Flow).Count + " saved devices"); }
    private void BuildShortcuts()
    {
        Heading("Your muscle memory", "A shortcut to better sound.", "Choose a key combination, then switch devices from any app.");
        BuildProfileCycleShortcut();
        BuildMicrophoneMuteShortcut();
        foreach (var f in new[] { AudioFlow.Output, AudioFlow.Input })
        {
            var p = new StackPanel(); p.Children.Add(Text(f == AudioFlow.Output ? "↗  CYCLE PLAYBACK" : "↙  CYCLE RECORDING", 11, f == AudioFlow.Output ? Mint : Purple, FontWeights.SemiBold));
            p.Children.Add(new TextBlock { Text = f == AudioFlow.Output ? "Next output device" : "Next input device", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
            p.Children.Add(Text("Cycles through the connected devices you selected on the Devices page.", 12, Muted));
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            line.Children.Add(Card(Text((f == AudioFlow.Output ? Preferences.OutputShortcut : Preferences.InputShortcut).Label, 17, Purple, FontWeights.SemiBold), 13));
            var edit = Button("Record shortcut", () => ChangeShortcut(f)); edit.Margin = new Thickness(14, 0, 0, 0); edit.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(edit); p.Children.Add(line);
            var active = new CheckBox { Content = "Enable this shortcut", IsChecked = f == AudioFlow.Output ? Preferences.OutputHotkeyEnabled : Preferences.InputHotkeyEnabled, Margin = new Thickness(0, 16, 0, 0) };
            active.Click += (_, _) =>
            {
                var previous = f == AudioFlow.Output ? Preferences.OutputHotkeyEnabled : Preferences.InputHotkeyEnabled;
                if (f == AudioFlow.Output) Preferences.OutputHotkeyEnabled = active.IsChecked == true; else Preferences.InputHotkeyEnabled = active.IsChecked == true;
                try { SettingsTransfer.Validate(Preferences); RegisterShortcuts(); Save(); }
                catch (Exception ex) { if (f == AudioFlow.Output) Preferences.OutputHotkeyEnabled = previous; else Preferences.InputHotkeyEnabled = previous; active.IsChecked = previous; RegisterShortcuts(); SetStatus(ex.Message, true); }
            };
            p.Children.Add(active);
            var card = Card(p, 24); card.Margin = new Thickness(0, 0, 0, 16); page.Children.Add(card);
        }
        var enabled = new CheckBox { Content = "Enable global shortcuts", IsChecked = Preferences.HotkeysEnabled, Margin = new Thickness(2, 8, 0, 15) };
        enabled.Click += (_, _) =>
        {
            var previous = Preferences.HotkeysEnabled; Preferences.HotkeysEnabled = enabled.IsChecked == true;
            try { SettingsTransfer.Validate(Preferences); RegisterShortcuts(); Save(); Navigate("Shortcuts"); }
            catch (Exception ex) { Preferences.HotkeysEnabled = previous; enabled.IsChecked = previous; RegisterShortcuts(); SetStatus(ex.Message, true); }
        }; page.Children.Add(enabled);
        page.Children.Add(Text("Use Ctrl, Alt or Shift with a letter, number or function key. Esc cancels recording.", 12, Muted));
        if (hotkeyErrors.Count > 0) page.Children.Add(new TextBlock { Text = "Shortcut unavailable: " + string.Join(", ", hotkeyErrors) + ". Choose a different combination or release it in the other app.", Foreground = Brush("#F0B68A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
    }
    private void ChangeShortcut(AudioFlow f)
    {
        var previous = f == AudioFlow.Output ? Preferences.OutputShortcut : Preferences.InputShortcut;
        var shortcut = CaptureShortcut(previous);
        if (shortcut == null) return;
        if (IsDuplicateShortcut(shortcut, f == AudioFlow.Output ? "output" : "input", null)) { SetStatus("That shortcut is already assigned in SignalShift.", true); return; }
        hotkeys!.Clear();
        if (!hotkeys.Register(500, shortcut, () => { })) { RegisterShortcuts(); SetStatus("That shortcut is used by another app. Your previous shortcut is kept.", true); return; }
        hotkeys.Clear();
        if (f == AudioFlow.Output) Preferences.OutputShortcut = shortcut; else Preferences.InputShortcut = shortcut;
        RegisterShortcuts(); Save(); Navigate("Shortcuts"); SetStatus("Shortcut saved · " + shortcut.Label);
    }
    private Shortcut? CaptureShortcut(Shortcut? initial)
    {
        hotkeys?.Clear(); paused = true;
        Shortcut? result = null;
        var dialog = Dialog("Record a shortcut", 470, 250);
        var stack = new StackPanel { Margin = new Thickness(30) }; stack.Children.Add(Text("Make it yours.", 24, null, FontWeights.SemiBold)); stack.Children.Add(new TextBlock { Text = "Press Ctrl, Alt or Shift plus another key.\nEsc cancels. F12 and Windows shortcuts are reserved.", Foreground = Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 18) });
        var feedback = Text(initial?.Label ?? "Waiting for your shortcut…", 17, Purple); stack.Children.Add(feedback); dialog.Content = stack;
        dialog.Focusable = true;
        dialog.Loaded += (_, _) => { dialog.Activate(); Keyboard.Focus(dialog); };
        dialog.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { dialog.Close(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.None || modifiers.HasFlag(ModifierKeys.Windows) || key == Key.F12 || key == Key.None) { feedback.Text = "Choose Ctrl / Alt / Shift + another key"; return; }
            uint native = 0; var names = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) { native |= 2; names.Add("Ctrl"); }
            if (modifiers.HasFlag(ModifierKeys.Alt)) { native |= 1; names.Add("Alt"); }
            if (modifiers.HasFlag(ModifierKeys.Shift)) { native |= 4; names.Add("Shift"); }
            names.Add(key >= Key.D0 && key <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
            result = new(native, (uint)KeyInterop.VirtualKeyFromKey(key), string.Join(" + ", names)); dialog.Close();
        };
        try { dialog.ShowDialog(); } finally { paused = false; RegisterShortcuts(); }
        return result;
    }
    private bool IsDuplicateShortcut(Shortcut shortcut, string? exclude, string? profileId) =>
        (exclude != "output" && Same(shortcut, Preferences.OutputShortcut)) || (exclude != "input" && Same(shortcut, Preferences.InputShortcut)) || (exclude != "profile-cycle" && Same(shortcut, Preferences.ProfileCycleShortcut)) || (exclude != "microphone-mute" && Same(shortcut, Preferences.MicrophoneMuteShortcut)) || Preferences.Profiles.Any(p => p.Id != profileId && p.Hotkey != null && Same(shortcut, p.Hotkey));
    private static bool Same(Shortcut a, Shortcut b) => a.Modifiers == b.Modifiers && a.Key == b.Key;
    private void BuildProfiles()
    {
        Heading("Your setups, saved", "A place for every mode.", "Switch your output and microphone together. Cycle all profiles with one shortcut, or assign individual shortcuts.");
        var add = Button("+   New profile", () => EditProfile(null), true); add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 0, 0, 20); page.Children.Add(add);
        if (Preferences.Profiles.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(20, 28, 20, 28) }; empty.Children.Add(Glyph("\uE8A9", 36, Purple)); empty.Children.Add(new TextBlock { Text = "Work mode. Game night. Quiet hours.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) }); empty.Children.Add(Text("Save a device pair, then move between setups in one step.", 13, Muted)); page.Children.Add(Card(empty, 24));
        }
        foreach (var profile in Preferences.Profiles)
        {
            var content = new StackPanel();
            var header = new DockPanel();
            var badge = new Border { Child = Text("●  ACTIVE", 10, Mint, FontWeights.SemiBold), Background = Brush("#1D3531"), Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            DockPanel.SetDock(badge, Dock.Right); header.Children.Add(badge); header.Children.Add(Text(profile.Name, 21, null, FontWeights.SemiBold)); content.Children.Add(header);
            content.Children.Add(new TextBlock { Text = "↗  " + DeviceName(profile.OutputId, outputs) + "\n↙  " + DeviceName(profile.InputId, inputs), Foreground = Muted, FontSize = 13, Margin = new Thickness(0, 12, 0, 16), LineHeight = 25, TextWrapping = TextWrapping.Wrap });
            if (profile.Hotkey != null) content.Children.Add(new TextBlock { Text = profile.Hotkey.Label, Foreground = Purple, Margin = new Thickness(0, 0, 0, 14) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal }; var activate = Button("Activate profile  ↗", () => ActivateProfile(profile), true); buttons.Children.Add(activate); var edit = Button("Edit", () => EditProfile(profile)); edit.Margin = new Thickness(10, 0, 0, 0); buttons.Children.Add(edit); var remove = Button("Remove", () => { Preferences.Profiles.Remove(profile); Save(); RegisterShortcuts(); Navigate("Profiles"); }); remove.Margin = edit.Margin; buttons.Children.Add(remove); content.Children.Add(buttons);
            var card = Card(content, 24); card.Margin = new Thickness(0, 0, 0, 15); page.Children.Add(card);
            profileRows[profile.Id] = (card, activate, badge, profile.Name);
        }
        RefreshProfileVisuals();
    }
    private void EditProfile(AudioProfile? original)
    {
        RefreshDevices(); paused = true;
        var dialog = Dialog(original == null ? "New profile" : "Edit profile", 520, 520);
        var content = new StackPanel { Margin = new Thickness(28) }; content.Children.Add(Text("Your sound, together.", 24, null, FontWeights.SemiBold));
        var name = new TextBox { Text = original?.Name ?? "My setup", Margin = new Thickness(0, 8, 0, 16) }; content.Children.Add(new TextBlock { Text = "PROFILE NAME", FontSize = 10, Foreground = Muted, Margin = new Thickness(0, 22, 0, 0) }); content.Children.Add(name);
        ComboBox Picker(string? id, IReadOnlyList<AudioDevice> devices)
        {
            var combo = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id", Margin = new Thickness(0, 7, 0, 17), MaxDropDownHeight = 250 };
            combo.Items.Add(new AudioDevice("", "Leave unchanged", AudioFlow.Output, false, false)); foreach (var d in devices) combo.Items.Add(d);
            // Keep disconnected device references editable without silently replacing them.
            if (id != null && devices.All(d => d.Id != id)) combo.Items.Add(new AudioDevice(id, "Disconnected device (saved)", AudioFlow.Output, false, false));
            combo.SelectedValue = id ?? ""; return combo;
        }
        content.Children.Add(Text("OUTPUT DEVICE", 10, Muted)); var output = Picker(original == null ? outputs.FirstOrDefault(d => d.IsDefault)?.Id : original.OutputId, outputs); content.Children.Add(output);
        content.Children.Add(Text("INPUT DEVICE", 10, Muted)); var input = Picker(original == null ? inputs.FirstOrDefault(d => d.IsDefault)?.Id : original.InputId, inputs); content.Children.Add(input);
        Shortcut? shortcut = original?.Hotkey;
        var hotkeyButton = Button(shortcut?.Label ?? "+   Add a shortcut (optional)", () => { var captured = CaptureShortcut(shortcut); if (captured != null) shortcut = captured; });
        hotkeyButton.Click += (_, _) => { hotkeyButton.Content = shortcut?.Label ?? "+   Add a shortcut (optional)"; };
        var shortcutLine = new StackPanel { Orientation = Orientation.Horizontal }; shortcutLine.Children.Add(hotkeyButton);
        var clearShortcut = Button("Clear", () => { shortcut = null; hotkeyButton.Content = "+   Add a shortcut (optional)"; }); clearShortcut.Margin = new Thickness(8, 0, 0, 0); shortcutLine.Children.Add(clearShortcut); content.Children.Add(shortcutLine);
        var error = new TextBlock { Foreground = Brush("#F0B68A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10), FontSize = 12 }; content.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("Cancel", () => dialog.Close())); var save = Button("Save profile", () =>
        {
            var outputId = output.SelectedValue as string; var inputId = input.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(name.Text) || (string.IsNullOrEmpty(outputId) && string.IsNullOrEmpty(inputId))) { error.Text = "Give your profile a name and choose at least one device."; return; }
            if (shortcut != null && IsDuplicateShortcut(shortcut, null, original?.Id)) { error.Text = "That shortcut is already assigned in SignalShift."; return; }
            hotkeys!.Clear();
            if (shortcut != null && !hotkeys.Register(500, shortcut, () => { })) { RegisterShortcuts(); error.Text = "Another app uses that shortcut. Choose a different one."; return; }
            hotkeys.Clear();
            var profile = original ?? new AudioProfile(); profile.Name = name.Text.Trim(); profile.OutputId = string.IsNullOrEmpty(outputId) ? null : outputId; profile.InputId = string.IsNullOrEmpty(inputId) ? null : inputId; profile.Hotkey = shortcut;
            if (original == null) Preferences.Profiles.Add(profile); Save(); RegisterShortcuts(); dialog.Close();
        }, true); save.Margin = new Thickness(10, 0, 0, 0); buttons.Children.Add(save); content.Children.Add(buttons); dialog.Content = content;
        try { dialog.ShowDialog(); } finally { paused = false; RegisterShortcuts(); Navigate("Profiles"); }
    }
    private void BuildSettings()
    {
        Heading("Small details, your way", "Settle into your flow.", "SignalShift stays quietly in your system tray, ready whenever you need it.");
        BuildAppearance();
        BuildUpdates();
        BuildSettingsTransfer();
        var preferences = new StackPanel();
        StackPanel Option(string title, string description, bool value, Action<bool> change)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 23) }; var check = new CheckBox { Content = title, IsChecked = value }; check.Click += (_, _) => { try { change(check.IsChecked == true); Save(); } catch (Exception ex) { check.IsChecked = value; SetStatus(ex.Message, true); } }; p.Children.Add(check); p.Children.Add(new TextBlock { Text = description, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(30, 7, 0, 0) }); preferences.Children.Add(p); return p;
        }
        Option("Also switch communications devices", "Keep calls and voice chat on the same device as your other audio.", Preferences.CommunicationsToo, v => Preferences.CommunicationsToo = v);
        var sounds = Option("Play confirmation sounds", "Soft plucks confirm device/profile shortcuts and microphone mute controls. Separate from visual notifications.", Preferences.HotkeySounds, v => { Preferences.HotkeySounds = v; if (!v) soundCues.Cancel(); });
        var previews = new WrapPanel { Margin = new Thickness(30, 10, 0, 0) };
        previews.Children.Add(Button("Preview output sound", () => soundCues.Play(AudioFlow.Output)));
        var inputPreview = Button("Preview input sound", () => soundCues.Play(AudioFlow.Input)); inputPreview.Margin = new Thickness(8, 0, 0, 0); previews.Children.Add(inputPreview); sounds.Children.Add(previews);
        Option("Show a notification after switching", "A small popup shows the latest device immediately. Repeated switches update the same popup.", Preferences.Notifications, v => { Preferences.Notifications = v; if (!v) switchNotification?.Dismiss(); });
        Option("Keep running when the window closes", "Your shortcuts keep working. Choose Quit from the tray to exit.", Preferences.CloseToTray, v => Preferences.CloseToTray = v);
        var startup = new StackPanel { Margin = new Thickness(0, 0, 0, 23) };
        var startupCheck = new CheckBox { Content = "Start with Windows", IsChecked = IsStartupEnabled() };
        startupCheck.Click += async (_, _) =>
        {
            startupCheck.IsEnabled = false;
            try { await StartupRegistration.SetAsync(startupCheck.IsChecked == true); }
            catch (Exception ex) { SetStatus(ex.Message, true); }
            finally { startupCheck.IsChecked = IsStartupEnabled(); startupCheck.IsEnabled = true; }
        };
        startup.Children.Add(startupCheck);
        startup.Children.Add(new TextBlock { Text = "Launch SignalShift in your tray when you sign in.", Foreground = Muted, FontSize = 12, Margin = new Thickness(30, 7, 0, 0) });
        preferences.Children.Add(startup);
        Option("Open in the tray", "Keep the main window hidden when SignalShift starts.", Preferences.StartMinimized, v => Preferences.StartMinimized = v);
        page.Children.Add(Card(preferences, 25));
        var about = new StackPanel { Margin = new Thickness(0, 25, 0, 0) }; about.Children.Add(Text("SIGNALSHIFT " + AppUpdater.Current.ToString(3), 11, Purple, FontWeights.SemiBold)); about.Children.Add(new TextBlock { Text = "Native Windows audio switching. No account, no ads, no telemetry.\nYour preferences stay on this computer.", Foreground = Muted, FontSize = 13, LineHeight = 24, Margin = new Thickness(0, 10, 0, 15) });
        var line = new StackPanel { Orientation = Orientation.Horizontal }; line.Children.Add(Button("Open settings folder", () => Process.Start(new ProcessStartInfo("explorer.exe", SettingsStore.DirectoryPath) { UseShellExecute = true })));
        var licenses = Button("Third-party licenses", () =>
        {
            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("SignalShift.ThirdPartyNotices")!;
            using var reader = new StreamReader(stream);
            var dialog = Dialog("Third-party licenses", 650, 520);
            dialog.Content = new TextBox { Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) };
            dialog.ShowDialog();
        }); licenses.Margin = new Thickness(12, 0, 0, 0); line.Children.Add(licenses);
        var quit = Button("Quit SignalShift", Quit); quit.Margin = new Thickness(12, 0, 0, 0); line.Children.Add(quit); about.Children.Add(line); page.Children.Add(about);
    }
    private void Cycle(AudioFlow f, bool fromHotkey = false)
    {
        try
        {
            if (fromHotkey) soundCues.Cancel();
            RefreshDevices(); var next = CyclePlanner.Next(Devices(f), Selected(f), audio.GetDefaultId(f));
            if (next == null) { SetStatus("Choose at least one connected " + (f == AudioFlow.Output ? "playback" : "recording") + " device for your cycle.", true); ShowWindow(); return; }
            Switch(next, fromHotkey);
        }
        catch (Exception ex) { SetStatus("Could not switch audio: " + ex.Message, true); }
    }
    private void Switch(AudioDevice device, bool fromHotkey = false)
    {
        try { audio.SetDefault(device, Preferences.CommunicationsToo); RefreshDevices(); if (currentPage == "Devices") RefreshDeviceVisuals(); if (fromHotkey && Preferences.HotkeySounds) soundCues.Play(device.Flow); Notify(device.Flow == AudioFlow.Output ? "Output switched" : "Input switched", device.Title, device.Flow); SetStatus((device.Flow == AudioFlow.Output ? "Output" : "Input") + " → " + device.Title); }
        catch (Exception ex) { RefreshDevices(); if (currentPage == "Devices") RefreshDeviceVisuals(); SetStatus("Could not switch: " + ex.Message, true); }
    }
    private void ActivateProfile(AudioProfile profile, bool fromHotkey = false)
    {
        try
        {
            if (fromHotkey) soundCues.Cancel();
            RefreshDevices(); var output = outputs.FirstOrDefault(d => d.Id == profile.OutputId); var input = inputs.FirstOrDefault(d => d.Id == profile.InputId);
            if ((profile.OutputId != null && output == null) || (profile.InputId != null && input == null)) throw new InvalidOperationException("A device in this profile is disconnected. No devices were changed.");
            var prior = SnapshotDefaults();
            try { if (output != null) audio.SetDefault(output, Preferences.CommunicationsToo); if (input != null) audio.SetDefault(input, Preferences.CommunicationsToo); }
            catch (Exception original)
            {
                var failures = new List<Exception> { original };
                foreach (var entry in prior) if (entry.Id != null) { try { audio.SetRoles(entry.Id, new[] { entry.Role }); } catch (Exception restoreError) { failures.Add(restoreError); } }
                if (failures.Count > 1) throw new AggregateException("Profile switching failed and Windows could not restore every previous default.", failures);
                throw;
            }
            activeProfileId = profile.Id; RefreshDevices(); if (currentPage == "Devices") RefreshDeviceVisuals(); if (fromHotkey && Preferences.HotkeySounds) soundCues.Play(output != null ? AudioFlow.Output : AudioFlow.Input); Notify("Profile activated", profile.Name); SetStatus("Profile → " + profile.Name);
        }
        catch (Exception ex) { SetStatus("Could not activate profile: " + ex.Message, true); }
    }
    private List<(string? Id, int Role)> SnapshotDefaults()
    {
        var snapshot = new List<(string?, int)>(); foreach (var f in new[] { AudioFlow.Output, AudioFlow.Input }) foreach (var role in new[] { 0, 1, 2 }) snapshot.Add((audio.GetDefaultId(f, role), role)); return snapshot;
    }
    private void RefreshDevices()
    {
        if (useDemoDevices) { LoadPreviewDevices(); RefreshProfileVisuals(); return; }
        outputs = audio.Enumerate(AudioFlow.Output); inputs = audio.Enumerate(AudioFlow.Input); microphoneMute.Refresh(); UpdateMicrophoneMuteVisuals(); UpdateTray(); RefreshProfileVisuals();
    }
    private IReadOnlyList<AudioDevice> Devices(AudioFlow f) => f == AudioFlow.Output ? outputs : inputs;
    private HashSet<string> Selected(AudioFlow f) => f == AudioFlow.Output ? Preferences.Outputs : Preferences.Inputs;
    private void RegisterShortcuts()
    {
        if (hotkeys == null) return; hotkeys.Clear(); hotkeyErrors.Clear();
        if (Preferences.HotkeysEnabled)
        {
            if (Preferences.OutputHotkeyEnabled && !hotkeys.Register(1, Preferences.OutputShortcut, () => Cycle(AudioFlow.Output, fromHotkey: true))) hotkeyErrors.Add("output");
            if (Preferences.InputHotkeyEnabled && !hotkeys.Register(2, Preferences.InputShortcut, () => Cycle(AudioFlow.Input, fromHotkey: true))) hotkeyErrors.Add("input");
            if (Preferences.ProfileCycleHotkeyEnabled && !hotkeys.Register(4, Preferences.ProfileCycleShortcut, CycleProfiles)) hotkeyErrors.Add("profile cycle");
            if (Preferences.MicrophoneMuteHotkeyEnabled && !hotkeys.Register(5, Preferences.MicrophoneMuteShortcut, ToggleMicrophoneMute)) hotkeyErrors.Add("microphone mute");
            var id = 10; foreach (var profile in Preferences.Profiles) if (profile.Hotkey != null && !hotkeys.Register(id++, profile.Hotkey, () => ActivateProfile(profile, fromHotkey: true))) hotkeyErrors.Add(profile.Name);
        }
        lastShortcutRetry = DateTime.UtcNow;
        var anyEnabled = Preferences.HotkeysEnabled && (Preferences.OutputHotkeyEnabled || Preferences.InputHotkeyEnabled || Preferences.ProfileCycleHotkeyEnabled || Preferences.MicrophoneMuteHotkeyEnabled || Preferences.Profiles.Any(p => p.Hotkey != null));
        trayStatus.Text = !anyEnabled ? "Shortcuts paused" : hotkeyErrors.Count > 0 ? "Shortcut needs attention" : "Shortcuts active";
        trayStatus.Foreground = hotkeyErrors.Count > 0 ? Brush("#F0B68A") : Mint;
        if (hotkeyErrors.Count > 0) SetStatus("Shortcut conflict: " + string.Join(", ", hotkeyErrors) + ". Open Shortcuts to change it.", true);
        if (deviceNotifications != null) { if (hotkeyErrors.Count > 0) timer.Start(); else timer.Stop(); }
        if (!paused && pendingDeviceChange != AudioChangeKind.None) QueueDeviceRefresh(AudioChangeKind.None);
    }
    private void SetupTray()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SonarShift.ico")).Stream;
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(stream), Text = "SignalShift · Your sound, one shortcut away", Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow); UpdateTray();
    }
    private void UpdateTray()
    {
        if (tray == null) return;
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Open SignalShift", null, (_, _) => Dispatcher.Invoke(ShowWindow)); menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var f in new[] { AudioFlow.Output, AudioFlow.Input })
        {
            var group = new Forms.ToolStripMenuItem(f == AudioFlow.Output ? "Playback" : "Recording");
            foreach (var device in Devices(f)) { var item = new Forms.ToolStripMenuItem(device.Title) { Checked = device.IsDefault }; item.Click += (_, _) => Dispatcher.Invoke(() => Switch(device)); group.DropDownItems.Add(item); }
            menu.Items.Add(group);
        }
        menu.Items.Add("Next output", null, (_, _) => Dispatcher.Invoke(() => Cycle(AudioFlow.Output))); menu.Items.Add("Next input", null, (_, _) => Dispatcher.Invoke(() => Cycle(AudioFlow.Input)));
        if (Preferences.Profiles.Count > 0) { var profiles = new Forms.ToolStripMenuItem("Profiles"); foreach (var profile in Preferences.Profiles) profiles.DropDownItems.Add(profile.Name, null, (_, _) => Dispatcher.Invoke(() => ActivateProfile(profile))); menu.Items.Add(profiles); }
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Quit SignalShift", null, (_, _) => Dispatcher.Invoke(Quit)); var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; old?.Dispose();
    }
    private void Tick()
    {
        try
        {
            var signal = Path.Combine(SettingsStore.DirectoryPath, "show.signal"); if (File.Exists(signal)) { File.Delete(signal); ShowWindow(); }
            if (paused) return;
            if (hotkeyErrors.Count > 0 && DateTime.UtcNow - lastShortcutRetry > TimeSpan.FromSeconds(5)) RegisterShortcuts();
            if (deviceNotifications == null) ApplyDeviceRefresh();
        }
        catch (Exception ex) { SetStatus("Waiting for Windows audio: " + ex.Message, true); }
    }
    private void Notify(string title, string text, AudioFlow? flow = null)
    {
        if (!Preferences.Notifications || (!Program.IsInterface && tray == null)) return;
        if (Program.IsInterface)
        {
            try { HostConnection.Notify(title, text, flow); }
            catch (Exception ex) { SetStatus("Switch confirmation unavailable: " + ex.Message, true); }
            return;
        }
        try { (switchNotification ??= new SwitchNotification()).Present(title, text, flow); }
        catch (Exception ex) { SetStatus("Switch confirmation unavailable: " + ex.Message, true); }
    }
    private void ShowWindow() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); if (audioUiReady) { RefreshDevices(); Navigate(currentPage); } }
    private void MinimizeToTray() { if (Program.IsInterface) { backgroundClose = true; Close(); } else Hide(); }
    private bool backgroundClose;
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!Program.IsInterface && !quitting && Preferences.CloseToTray) { e.Cancel = true; Hide(); SetStatus("SignalShift is running in your tray"); return; }
        if (Program.IsInterface && !hostExiting && (quitting || (!backgroundClose && !Preferences.CloseToTray))) HostConnection.TrySend("quit");
        timer.Stop(); deviceNotifications?.Dispose(); microphoneMute.Dispose(); signalWatcher?.Dispose(); hotkeys?.Dispose(); soundCues.Dispose(); switchNotification?.Dispose(); tray?.Dispose(); Application.Current.Shutdown();
    }
    private void QueueDeviceRefresh() => QueueDeviceRefresh(AudioChangeKind.Topology);
    private void QueueDeviceRefresh(AudioChangeKind kind)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            pendingDeviceChange |= kind;
            if (refreshPending) return;
            refreshPending = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { refreshPending = false; if (paused) return; var change = pendingDeviceChange; pendingDeviceChange = AudioChangeKind.None; ApplyDeviceRefresh(change); }));
        }));
    }
    private void ApplyDeviceRefresh(AudioChangeKind kind = AudioChangeKind.Topology)
    {
        try
        {
            IReadOnlyList<AudioDevice> nextOutputs = outputs, nextInputs = inputs;
            if ((kind & AudioChangeKind.Topology) != 0 ||
                ((kind & AudioChangeKind.Defaults) != 0 &&
                 (!audio.TryRefreshDefaults(outputs, AudioFlow.Output, out nextOutputs) || !audio.TryRefreshDefaults(inputs, AudioFlow.Input, out nextInputs))))
            {
                nextOutputs = audio.Enumerate(AudioFlow.Output); nextInputs = audio.Enumerate(AudioFlow.Input);
                microphoneMute.Refresh();
            }
            else if ((kind & AudioChangeKind.Defaults) != 0) microphoneMute.Refresh();
            else if (!microphoneMute.RefreshState())
            { nextOutputs = audio.Enumerate(AudioFlow.Output); nextInputs = audio.Enumerate(AudioFlow.Input); microphoneMute.Refresh(); }
            UpdateMicrophoneMuteVisuals();
            if (outputs.SequenceEqual(nextOutputs) && inputs.SequenceEqual(nextInputs)) return;
            outputs = nextOutputs; inputs = nextInputs; UpdateTray();
            if (IsVisible && currentPage == "Devices") RefreshDeviceVisuals();
            if (IsVisible && currentPage == "Profiles") RefreshProfileVisuals();
        }
        catch (Exception ex) { SetStatus("Waiting for Windows audio: " + ex.Message, true); }
    }
    private void Quit() { quitting = true; Close(); }
    private void Save() { if (useDemoDevices && Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") == null) return; store.Save(Preferences); if (Program.IsInterface) HostConnection.TrySend("reload"); UpdateTray(); }
    private void SetStatus(string text, bool error = false) { status.Text = (error ? "!  " : "●  ") + text; status.Foreground = error ? Brush("#F0B68A") : Muted; status.ToolTip = text; }
    private static string DeviceName(string? id, IReadOnlyList<AudioDevice> devices) => id == null ? "Leave unchanged" : devices.FirstOrDefault(d => d.Id == id)?.Title ?? "Disconnected device";
    private static bool IsStartupEnabled() => StartupRegistration.IsEnabled();
    private Window Dialog(string title, int width, int height) => new() { Title = title, Owner = this, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Brush("#171A23"), Foreground = Brush("#F4F4FA"), FontFamily = FontFamily, ShowInTaskbar = true, Icon = Icon };
    private static Brush Brush(string color) => ThemePalette.Get(color);
    private static TextBlock Text(string value, double size, Brush? color = null, FontWeight? weight = null) => new() { Text = value, FontSize = size, Foreground = color ?? Brush("#F4F4FA"), FontWeight = weight ?? FontWeights.Normal };
    private static TextBlock Glyph(string value, double size, Brush color) => new() { Text = value, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
    private static Border Card(UIElement content, double padding) => new() { Child = content, Padding = new Thickness(padding), Background = Brush("#171A23"), BorderBrush = Brush("#292D3A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13) };
    private static Button Button(object label, Action click, bool primary = false)
    {
        var button = new Button { Content = primary && label is string text ? Text(text, 13, Brush("#171124"), FontWeights.SemiBold) : label }; if (primary) button.Style = (Style)Application.Current.FindResource("Primary"); button.Click += (_, _) => click(); return button;
    }
    private static void AutomationPropertiesName(DependencyObject element, string name) => System.Windows.Automation.AutomationProperties.SetName(element, name);
}
