using System.Runtime.InteropServices;

namespace Sofle.Status.Windows;

internal sealed class StatusPanel : Form
{
    private readonly Label _heading = new() { AutoSize = true, ForeColor = Color.White };
    private readonly Label _details = new() { AutoSize = true, ForeColor = Color.Gainsboro };
    private readonly Font _headingFont = new("Segoe UI", 17, FontStyle.Bold);
    private readonly Font _detailsFont = new("Segoe UI", 9);
    private bool _dragging;
    public event Action<Point>? PositionSaved;

    public StatusPanel()
    {
        Text = "Sofle Status";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 30, 36);
        AutoScaleMode = AutoScaleMode.Dpi;
        _heading.Font = _headingFont;
        _details.Font = _detailsFont;
        Controls.AddRange([_heading, _details]);
        MouseDown += Drag;
        _heading.MouseDown += Drag;
        _details.MouseDown += Drag;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    public void SetStatus(string heading, string details, Color accent)
    {
        _heading.Text = heading;
        _heading.ForeColor = accent;
        _details.Text = details;
        var padding = 12 * DeviceDpi / 96;
        _heading.Location = new(padding, padding / 2);
        _details.Location = new(padding, _heading.Bottom + 2);
        var area = Screen.FromControl(this).WorkingArea;
        ClientSize = new(
            Math.Min(area.Width, Math.Max(300 * DeviceDpi / 96,
                Math.Max(_heading.PreferredWidth, _details.PreferredWidth) + padding * 2)),
            _details.Bottom + padding);
    }

    public void PositionPanel(AppSettings settings)
    {
        if (_dragging) return;
        var area = settings.PanelX is int x && settings.PanelY is int y
            ? Screen.FromPoint(new(x, y)).WorkingArea
            : (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        var desired = settings.PanelX is int px && settings.PanelY is int py
            ? new Point(px, py)
            : new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Location = new(
            Math.Clamp(desired.X, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(desired.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    private void Drag(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        _dragging = true;
        try
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }
        finally
        {
            _dragging = false;
        }
        PositionSaved?.Invoke(Location);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
        {
            message.Result = new IntPtr(3); // MA_NOACTIVATE
            return;
        }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _headingFont.Dispose();
            _detailsFont.Dispose();
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
