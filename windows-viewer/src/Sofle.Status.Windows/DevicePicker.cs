using Windows.Devices.Enumeration;

namespace Sofle.Status.Windows;

internal sealed class DevicePicker : Form
{
    private readonly ListBox _devices = new() { Dock = DockStyle.Fill, DisplayMember = nameof(DeviceChoice.Label) };
    public string? SelectedDeviceId => (_devices.SelectedItem as DeviceChoice)?.Id;

    public DevicePicker(IEnumerable<DeviceInformation> devices, string? selected)
    {
        Text = "Choose your paired ZMK keyboard";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new(540, 300);
        MinimumSize = new(400, 240);
        var help = new Label
        {
            Dock = DockStyle.Top, Height = 52, Padding = new Padding(8),
            Text = "Pair the LEFT half in Windows Bluetooth settings first.\nSelect Sofle_RGB (or your keyboard's configured Bluetooth name)."
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6)
        };
        var connect = new Button { Text = "Connect", DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([connect, cancel]);
        Controls.AddRange([_devices, help, buttons]);
        AcceptButton = connect;
        CancelButton = cancel;
        foreach (var device in devices.OrderBy(device => device.Name))
        {
            var choice = new DeviceChoice(device.Id, $"{device.Name}  [{device.Id[^Math.Min(12, device.Id.Length)..]}]");
            var index = _devices.Items.Add(choice);
            if (device.Id == selected) _devices.SelectedIndex = index;
        }
        _devices.SelectedIndexChanged += (_, _) => connect.Enabled = SelectedDeviceId is not null;
        connect.Enabled = SelectedDeviceId is not null;
    }

    private sealed record DeviceChoice(string Id, string Label);
}
