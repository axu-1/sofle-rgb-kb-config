using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Sofle.Status.Windows;

internal sealed class TrayApplication : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private AppSettings _settings = AppSettings.Load();
    private readonly StatusPanel _panel = new();
    private readonly NotifyIcon _tray = new() { Visible = true };
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly ToolStripMenuItem _showPanel = new("Show status panel") { CheckOnClick = true };
    private readonly ToolStripMenuItem _startup = new("Start with Windows") { CheckOnClick = true };
    private readonly bool _demo;
    private readonly object _dispatchGate = new();
    private CancellationTokenSource? _connectionCancellation;
    private Task _connectionTask = Task.CompletedTask;
    private KeyboardStatus? _status;
    private long _receivedAt;
    private string _connectionMessage = "Choose keyboard from tray menu.";
    private string? _iconKey;
    private int _generation;
    private bool _closing;
    private int _demoTicks;

    public TrayApplication(bool demo)
    {
        _demo = demo;
        _ = _panel.Handle;
        _showPanel.Checked = _settings.ShowPanel;
        _startup.Checked = StartupEnabled();
        _startup.Enabled = !demo;
        var choose = new ToolStripMenuItem("Choose keyboard...");
        choose.Enabled = !demo;
        choose.Click += async (_, _) => await ChooseDeviceAsync();
        _showPanel.Click += (_, _) => SaveAction(() =>
        {
            _settings.ShowPanel = _showPanel.Checked;
            _settings.Save();
            SetPanelVisibility();
        });
        _startup.Click += (_, _) => SaveAction(() => SetStartup(_startup.Checked));
        var reset = new ToolStripMenuItem("Reset panel position");
        reset.Click += (_, _) => SaveAction(() =>
        {
            _settings.PanelX = _settings.PanelY = null;
            _settings.Save();
            _panel.PositionPanel(_settings);
        });
        var edit = new ToolStripMenuItem("Edit layer names (settings)...");
        edit.Click += (_, _) => SaveAction(() =>
        {
            _settings.Save();
            OpenFile(AppSettings.FilePath);
        });
        var reload = new ToolStripMenuItem("Reload settings");
        reload.Click += (_, _) => SaveAction(() =>
        {
            var next = AppSettings.Load();
            next.DeviceId = _settings.DeviceId;
            _settings = next;
            _showPanel.Checked = next.ShowPanel;
            _panel.PositionPanel(next);
            SetPanelVisibility();
            Render();
        });
        var diagnostics = new ToolStripMenuItem("Connection details...");
        diagnostics.Click += (_, _) => MessageBox.Show(
            $"{(_demo ? "DEMO: no hardware connection." : _connectionMessage)}\n\n" +
            $"Device: {_settings.DeviceId ?? "not selected"}\nLog: {AppLog.FilePath}\n\n" +
            "Missing service: flash the modified LEFT firmware. If Windows cached old services, remove and re-pair keyboard.\n" +
            "Sleeping or other profile: wake keyboard and select this laptop's Bluetooth profile.",
            "Sofle Status");
        var log = new ToolStripMenuItem("Open log");
        log.Click += (_, _) => SaveAction(() =>
        {
            AppLog.Write("Log opened.");
            OpenFile(AppLog.FilePath);
        });
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitThread();
        _menu.Items.AddRange([choose, _showPanel, _startup, reset, new ToolStripSeparator(),
            edit, reload, diagnostics, log, new ToolStripSeparator(), exit]);
        _tray.ContextMenuStrip = _menu;
        _panel.ContextMenuStrip = _menu;
        _panel.PositionSaved += point => SaveAction(() =>
        {
            _settings.PanelX = point.X;
            _settings.PanelY = point.Y;
            _settings.Save();
        });
        _panel.DpiChanged += (_, _) => { Render(); _panel.PositionPanel(_settings); };
        _timer.Tick += (_, _) =>
        {
            if (_demo && _demoTicks++ % 2 == 0) DemoStatus();
            Render();
        };
        Render();
        _panel.PositionPanel(_settings);
        SetPanelVisibility();
        _timer.Start();
        _panel.BeginInvoke(new Action(async () =>
        {
            if (_demo) DemoStatus();
            else if (_settings.DeviceId is null) await ChooseDeviceAsync();
            else await StartConnectionAsync(_settings.DeviceId);
        }));
    }

    private async Task ChooseDeviceAsync()
    {
        try
        {
            _connectionMessage = "Finding paired Bluetooth devices...";
            Render();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var devices = await DeviceInformation.FindAllAsync(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(timeout.Token);
            if (_closing) return;
            if (devices.Count == 0)
            {
                _connectionMessage = "No paired BLE devices. Pair Sofle's left half in Windows settings.";
                MessageBox.Show(_connectionMessage, "Sofle Status");
                Render();
                return;
            }
            using var picker = new DevicePicker(devices, _settings.DeviceId);
            if (picker.ShowDialog() == DialogResult.OK && picker.SelectedDeviceId is string id)
            {
                _settings.DeviceId = id;
                _settings.Save();
                await StartConnectionAsync(id);
            }
            else
            {
                _connectionMessage = _status is null ? "Choose keyboard from tray menu." : "Connected.";
                Render();
            }
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException
                                         or OperationCanceledException)
        {
            ReportError("Cannot select keyboard", exception);
        }
    }

    private async Task StartConnectionAsync(string id)
    {
        var generation = ++_generation;
        _connectionCancellation?.Cancel();
        await _connectionTask;
        if (_closing || generation != _generation) return;
        _connectionCancellation?.Dispose();
        _connectionCancellation = new();
        var cancellation = _connectionCancellation.Token;
        var keyboard = new BleKeyboard();
        keyboard.StatusReceived += status => Dispatch(generation, () =>
        {
            _status = status;
            _receivedAt = Stopwatch.GetTimestamp();
            _connectionMessage = "Connected.";
            Render();
        });
        keyboard.ConnectionChanged += message => Dispatch(generation, () =>
        {
            _status = null;
            _connectionMessage = message;
            Render();
        });
        _status = null;
        _connectionMessage = "Connecting to keyboard...";
        Render();
        _connectionTask = Task.Run(() => keyboard.RunAsync(id, cancellation));
    }

    private void Dispatch(int generation, Action action)
    {
        lock (_dispatchGate)
        {
            if (_closing) return;
            _panel.BeginInvoke(new Action(() =>
            {
                if (!_closing && generation == _generation) action();
            }));
        }
    }

    private void DemoStatus()
    {
        var layer = (byte)((_demoTicks / 2) % 5);
        _status = new(layer, 87, 92, 0, KeyboardOutput.Bluetooth, true, false, true, true,
            layer == 4 ? (byte)2 : (byte)0, layer == 3 ? 15u : 1u | (1u << layer), (uint)_demoTicks, 0);
        _receivedAt = Stopwatch.GetTimestamp();
        Render();
    }

    private void Render()
    {
        var elapsed = _status is null ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_receivedAt);
        var heading = _status is null ? "Sofle Status - offline"
            : $"{_status.Layer}  {StatusText.LayerName(_status.Layer, _settings.LayerNames)}";
        if (_demo) heading = $"DEMO  {heading}";
        var details = _status is null ? _connectionMessage : StatusText.Details(_status, elapsed);
        if (details.Length > 105) details = details[..102] + "...";
        _panel.SetStatus(heading, details, LayerIcon.Accent(_status));
        _panel.PositionPanel(_settings);
        var tooltip = _status is null ? _connectionMessage : StatusText.Tooltip(_status, _settings.LayerNames, elapsed);
        if (_demo) tooltip = "DEMO\n" + tooltip;
        _tray.Text = tooltip.Length > 127 ? tooltip[..124] + "..." : tooltip;
        var iconKey = $"{_status?.Layer.ToString() ?? "?"}/{LayerIcon.Accent(_status).ToArgb()}";
        if (iconKey != _iconKey)
        {
            var old = _tray.Icon;
            _tray.Icon = LayerIcon.Create(_status);
            old?.Dispose();
            _iconKey = iconKey;
        }
    }

    private void SetPanelVisibility()
    {
        if (_settings.ShowPanel) _panel.Show();
        else _panel.Hide();
    }

    private static void OpenFile(string path) =>
        Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true, ArgumentList = { path } });

    private static bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("SofleStatus") is string command
            && command == $"\"{Environment.ProcessPath}\"";
    }

    private void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("SofleStatus", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("SofleStatus", throwOnMissingValue: false);
    }

    private void SaveAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or System.Text.Json.JsonException or InvalidDataException
                                         or System.ComponentModel.Win32Exception)
        {
            _showPanel.Checked = _settings.ShowPanel;
            _startup.Checked = StartupEnabled();
            ReportError("Settings action failed", exception);
        }
    }

    private void ReportError(string title, Exception exception)
    {
        AppLog.Write(title, exception);
        _connectionMessage = $"{title}: {exception.Message}";
        Render();
        MessageBox.Show(_connectionMessage, "Sofle Status", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    protected override void ExitThreadCore()
    {
        lock (_dispatchGate) _closing = true;
        _connectionCancellation?.Cancel();
        _timer.Stop();
        _tray.Visible = false;
        _panel.Hide();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_dispatchGate) _closing = true;
            _connectionCancellation?.Cancel();
            _timer.Dispose();
            var icon = _tray.Icon;
            _tray.Dispose();
            icon?.Dispose();
            _menu.Dispose();
            _panel.Dispose();
        }
        base.Dispose(disposing);
    }
}
