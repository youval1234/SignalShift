namespace SonarShift;

public sealed partial class MainWindow
{
    // Public screenshots always use sample devices rather than local hardware.
    private static Settings PreviewPreferences() => new()
    {
        Initialized = true,
        Outputs = new() { "demo-speakers", "demo-headphones" },
        Inputs = new() { "demo-microphone" },
        Profiles = new()
        {
            new() { Id = "demo-desk", Name = "Desk", OutputId = "demo-speakers", InputId = "demo-microphone" },
            new() { Id = "demo-calls", Name = "Calls", OutputId = "demo-headphones", InputId = "demo-headset" }
        }
    };
    private void LoadPreviewDevices()
    {
        outputs = new[]
        {
            new AudioDevice("demo-speakers", "Desktop speakers (USB Audio)", AudioFlow.Output, true, false),
            new AudioDevice("demo-headphones", "Wireless headphones (Bluetooth Audio)", AudioFlow.Output, false, true),
            new AudioDevice("demo-monitor", "Monitor speakers (Display Audio)", AudioFlow.Output, false, false)
        };
        inputs = new[]
        {
            new AudioDevice("demo-microphone", "USB microphone (USB Audio)", AudioFlow.Input, true, false),
            new AudioDevice("demo-headset", "Headset microphone (Bluetooth Audio)", AudioFlow.Input, false, true)
        };
    }
}
