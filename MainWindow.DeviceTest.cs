using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SonarShift;

public sealed partial class MainWindow
{
    private void ShowDeviceTest(AudioDevice device)
    {
        try { RefreshDevices(); }
        catch (Exception ex) { SetStatus("Could not read audio devices: " + ex.Message, true); return; }
        CreateDeviceTestDialog(device).ShowDialog();
    }
    internal Window CreateDeviceTestDialog(AudioDevice device)
    {
        var dialog = Dialog(device.Flow == AudioFlow.Input ? "Test microphone" : "Test output", 590, 540);
        dialog.MinHeight = 480; dialog.MinWidth = 520; dialog.ResizeMode = ResizeMode.CanResize;
        var panel = new StackPanel { Margin = new Thickness(28) };
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(Text(device.Flow == AudioFlow.Input ? "Hear yourself clearly." : "Give it a listen.", 24, null, FontWeights.SemiBold));
        panel.Children.Add(new TextBlock { Text = device.Name, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 18), Foreground = Purple });
        panel.Children.Add(new TextBlock { Text = device.Flow == AudioFlow.Input
            ? "Record five seconds, then hear the clip through your chosen output. Use headphones for privacy. The clip stays in memory and is discarded when you close this test."
            : "Play a short, soft chime through this device. Tests use its current volume and do not change your default devices.", TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Muted });
        ComboBox? output = null;
        if (device.Flow == AudioFlow.Input)
        {
            panel.Children.Add(new TextBlock { Text = "PLAY BACK THROUGH", FontSize = 10, Foreground = Muted, Margin = new Thickness(0, 22, 0, 8) });
            output = new ComboBox { ItemsSource = outputs, DisplayMemberPath = "Name", SelectedValuePath = "Id", MaxDropDownHeight = 230 };
            output.SelectedValue = audio.GetDefaultId(AudioFlow.Output);
            if (output.SelectedIndex < 0 && output.Items.Count > 0) output.SelectedIndex = 0;
            AutomationPropertiesName(output, "Microphone test playback device"); panel.Children.Add(output);
        }
        var message = new TextBlock { Text = "Ready. Click below to start.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 22, 0, 10), FontSize = 13 };
        panel.Children.Add(message);
        var meter = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(0, 0, 0, 20), Visibility = device.Flow == AudioFlow.Input ? Visibility.Visible : Visibility.Collapsed };
        AutomationPropertiesName(meter, "Microphone input level"); panel.Children.Add(meter);
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        var start = Button(device.Flow == AudioFlow.Input ? "Record & hear (5 s)" : "Play test sound", () => { }, true);
        var replay = Button("Play again", () => { }); replay.Margin = new Thickness(8, 0, 0, 0); replay.IsEnabled = false;
        replay.Visibility = device.Flow == AudioFlow.Input ? Visibility.Visible : Visibility.Collapsed;
        var stop = Button("Stop", () => { }); stop.Margin = new Thickness(8, 0, 0, 0); stop.IsEnabled = false;
        buttons.Children.Add(start); buttons.Children.Add(replay); buttons.Children.Add(stop);
        var close = Button("Close", dialog.Close); close.Margin = new Thickness(0, 22, 0, 0); close.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(close);
        AudioDeviceTest.Clip? clip = null;
        CancellationTokenSource? operation = null;
        var busy = false; var closing = false; var recording = false; var started = DateTime.UtcNow;
        var meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        meterTimer.Tick += (_, _) =>
        {
            meter.Value = clip?.Peak ?? 0;
            if (recording) message.Text = "Recording… " + Math.Max(0, 5 - (int)(DateTime.UtcNow - started).TotalSeconds) + " s remaining. Speak into your microphone.";
        };
        async Task Run(bool record)
        {
            if (busy) return;
            var outputId = device.Flow == AudioFlow.Output ? device.Id : output?.SelectedValue as string;
            if (outputId == null) { message.Text = "Connect an output device first, then reopen this test."; return; }
            busy = true; start.IsEnabled = replay.IsEnabled = false; stop.IsEnabled = true;
            if (output != null) output.IsEnabled = false;
            operation = new CancellationTokenSource();
            try
            {
                if (device.Flow == AudioFlow.Input && record)
                {
                    clip?.Dispose(); clip = new AudioDeviceTest.Clip(); recording = true; started = DateTime.UtcNow;
                    message.Text = "Recording… Speak into your microphone."; meterTimer.Start();
                    await AudioDeviceTest.RecordAsync(device.Id, clip, operation.Token);
                    recording = false; meterTimer.Stop(); meter.Value = 0;
                }
                message.Text = device.Flow == AudioFlow.Input ? "Playing your microphone clip…" : "Playing test sound…";
                using var source = device.Flow == AudioFlow.Output ? AudioDeviceTest.Tone() : clip!.Open();
                await AudioDeviceTest.PlayAsync(outputId, source, operation.Token);
                message.Text = "Test finished. Nothing audible? Check the device's volume, mute and connections.";
            }
            catch (OperationCanceledException) { clip?.Dispose(); clip = null; message.Text = "Test stopped."; }
            catch (Exception ex)
            {
                clip?.Dispose(); clip = null;
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(SettingsStore.DirectoryPath, "errors.log"), DateTime.Now + " Device test " + device.Flow + ": " + ex + Environment.NewLine); } catch { }
                var accessDenied = ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005);
                message.Text = "Could not complete the test: " + ex.Message + (accessDenied && device.Flow == AudioFlow.Input ? " Check Windows microphone permissions." : "");
            }
            finally
            {
                recording = false; meterTimer.Stop(); meter.Value = 0;
                operation.Dispose(); operation = null; busy = false;
                start.IsEnabled = true; replay.IsEnabled = clip?.Length > 0; stop.IsEnabled = false;
                if (output != null) output.IsEnabled = true;
                if (closing) dialog.Close();
            }
        }
        start.Click += async (_, _) => await Run(true);
        replay.Click += async (_, _) => await Run(false);
        stop.Click += (_, _) => operation?.Cancel();
        dialog.Closing += (_, e) => { if (busy) { e.Cancel = true; closing = true; message.Text = "Stopping test…"; operation?.Cancel(); } };
        dialog.Closed += (_, _) => { meterTimer.Stop(); clip?.Dispose(); clip = null; };
        return dialog;
    }
}
