namespace Sofle.Status;

public static class StatusText
{
    public static string LayerName(byte layer, IReadOnlyDictionary<int, string> names) =>
        names.TryGetValue(layer, out var name) ? name : $"Layer {layer}";

    public static string Details(KeyboardStatus status, TimeSpan elapsed)
    {
        var left = Percent(status.LeftBattery);
        string right;
        if (!status.RightConnected)
            right = "offline";
        else if (status.RightBattery is null || status.RightSampleAgeSeconds is null)
            right = "?";
        else
        {
            // ZMK's peripheral BAS normally notifies only when the percentage changes.
            // Age describes the sample, not a connection timeout.
            right = Percent(status.RightBattery);
        }

        var output = status.Output switch
        {
            KeyboardOutput.Usb => "USB",
            KeyboardOutput.Bluetooth => "BT",
            _ => "no output"
        };
        var locks = status.HasLockIndicators ? LockText(status.LockIndicators) : "";
        return $"L {left}{(status.UsbPowered ? " +USB" : "")}  R {right}  {output} P{status.Profile + 1}{locks}";
    }

    public static string Tooltip(KeyboardStatus status, IReadOnlyDictionary<int, string> names, TimeSpan elapsed)
    {
        var age = status.RightSampleAgeSeconds is uint seconds
            ? $"\nRight battery sample: {(long)(seconds + elapsed.TotalSeconds)}s ago"
            : "";
        return $"{LayerName(status.Layer, names)} ({status.Layer})\n{Details(status, elapsed)}\nActive mask: 0x{status.ActiveLayers:X8}{age}";
    }

    private static string Percent(byte? value) => value is byte level ? $"{level}%" : "?";

    private static string LockText(byte indicators)
    {
        var labels = new List<string>();
        if ((indicators & 1) != 0) labels.Add("NUM");
        if ((indicators & 2) != 0) labels.Add("CAPS");
        if ((indicators & 4) != 0) labels.Add("SCRL");
        return labels.Count == 0 ? "" : $"  {string.Join(" ", labels)}";
    }
}
