using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Sonora;

public sealed partial class MainWindow
{
    private string? transferMessage;
    private void BuildSettingsTransfer()
    {
        var content = new StackPanel();
        content.Children.Add(Text("YOUR SETUP, TO GO", 10, Purple, FontWeights.SemiBold));
        content.Children.Add(new TextBlock { Text = "Save it. Bring it back.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 7) });
        content.Children.Add(new TextBlock { Text = "Export a readable .txt file for Notepad, restore a saved setup, or bring your SoundSwitch choices into Sonora.", Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        foreach (var item in new (string Label, Action Action, bool Primary)[]
        {
            ("Export settings .txt", ExportSettingsText, true),
            ("Import settings file", ImportSettingsText, false),
            ("Import from SoundSwitch", ImportFromSoundSwitch, false)
        })
        {
            var button = Button(item.Label, item.Action, item.Primary); button.Margin = new Thickness(0, 0, 9, 8); buttons.Children.Add(button);
        }
        content.Children.Add(buttons);
        content.Children.Add(new TextBlock { Text = transferMessage ?? "A backup is saved automatically before every import.", Foreground = transferMessage == null ? Muted : Mint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) });
        var card = Card(content, 23); card.Margin = new Thickness(0, 0, 0, 18); page.Children.Add(card);
    }
    private void ExportSettingsText()
    {
        paused = true;
        try
        {
            var dialog = new SaveFileDialog { Title = "Export Sonora settings", FileName = "Sonora-settings-" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt", DefaultExt = ".txt", Filter = "Notepad text file (*.txt)|*.txt", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            RefreshDevices();
            // Never overwrite the live settings database with the portable document envelope.
            if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(store.FilePath), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a separate export file instead of Sonora's live settings file.");
            SettingsTransfer.ExportFile(dialog.FileName, Preferences, outputs.Concat(inputs), IsStartupEnabled());
            transferMessage = "Exported to " + dialog.FileName; Navigate("Settings"); SetStatus("Settings exported as a Notepad text file");
        }
        catch (Exception ex) { SetStatus("Could not export settings: " + ex.Message, true); }
        finally { paused = false; }
    }
    private async void ImportSettingsText()
    {
        paused = true;
        try
        {
            var dialog = new OpenFileDialog { Title = "Import Sonora or SoundSwitch settings", Filter = "Settings files (*.txt;*.json)|*.txt;*.json|All files (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) await ImportSettingsFileAsync(dialog.FileName);
        }
        catch (Exception ex) { SetStatus("Import failed: " + ex.Message, true); }
        finally { paused = false; }
    }
    private async void ImportFromSoundSwitch()
    {
        var path = SettingsTransfer.FindSoundSwitchConfiguration();
        if (path == null) { SetStatus("SoundSwitch settings were not found. Use Import settings file to choose a saved configuration.", true); return; }
        paused = true;
        try { await ImportSettingsFileAsync(path); }
        catch (Exception ex) { SetStatus("Import failed: " + ex.Message, true); }
        finally { paused = false; }
    }
    private async Task ImportSettingsFileAsync(string path)
    {
        RefreshDevices(); var devices = outputs.Concat(inputs).ToList();
        var imported = SettingsTransfer.ReadFile(path, Preferences, devices);
        var backup = await SettingsTransfer.ApplyAsync(store, imported, devices);
        Preferences = imported.Settings; RegisterShortcuts(); RefreshDevices();
        var details = new List<string>
        {
            "Imported from " + imported.Source + ": " + Preferences.Outputs.Count + " playback choices, " + Preferences.Inputs.Count + " recording choices.",
            "Playback: " + Preferences.OutputShortcut.Label + ". Recording: " + Preferences.InputShortcut.Label + ".",
            "Previous settings: " + backup
        };
        details.AddRange(imported.Warnings);
        if (hotkeyErrors.Count > 0) details.Add("A shortcut is still reserved by another app. Quit SoundSwitch or release its shortcut; Sonora retries automatically.");
        transferMessage = string.Join("\n", details); Navigate("Settings");
        SetStatus(hotkeyErrors.Count > 0 ? "Settings imported. Quit SoundSwitch to free the imported shortcuts." : "Settings imported successfully", hotkeyErrors.Count > 0);
    }
}
