namespace Sofle.Status.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, @"Local\SofleStatus", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Sofle Status is already running. Look in the notification area.", "Sofle Status");
            return;
        }
        ApplicationConfiguration.Initialize();
        try
        {
            using var context = new TrayApplication(args.Contains("--demo", StringComparer.OrdinalIgnoreCase));
            Application.Run(context);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                        or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show($"Cannot load or save Sofle Status settings.\n\n{exception.Message}\n\n{AppSettings.FilePath}",
                "Sofle Status", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
