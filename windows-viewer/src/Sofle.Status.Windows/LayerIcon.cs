namespace Sofle.Status.Windows;

internal static class LayerIcon
{
    private static readonly Color[] Colors =
    [
        Color.FromArgb(68, 190, 140), Color.FromArgb(73, 156, 245),
        Color.FromArgb(191, 131, 255), Color.FromArgb(249, 191, 67),
        Color.FromArgb(246, 124, 77)
    ];

    public static Color Accent(KeyboardStatus? status)
    {
        if (status is null) return Color.Silver;
        if (status.LeftBattery is <= 20 || (status.RightConnected && status.RightBattery is <= 20))
            return Color.FromArgb(255, 100, 100);
        return Colors[status.Layer % Colors.Length];
    }

    public static Icon Create(KeyboardStatus? status)
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Accent(status));
        graphics.FillEllipse(background, 0, 0, 63, 63);
        var text = status?.Layer.ToString() ?? "?";
        using var font = new Font("Segoe UI", text.Length > 1 ? 29 : 36, FontStyle.Bold, GraphicsUnit.Pixel);
        using var foreground = new SolidBrush(Color.FromArgb(20, 22, 28));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, foreground, new RectangleF(0, 0, 64, 62), format);
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
