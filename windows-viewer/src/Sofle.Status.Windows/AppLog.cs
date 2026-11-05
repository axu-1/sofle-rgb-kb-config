namespace Sofle.Status.Windows;

internal static class AppLog
{
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(AppSettings.Folder, "status.log");

    public static void Write(string message, Exception? exception = null)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(AppSettings.Folder);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000)
                File.Move(FilePath, FilePath + ".old", overwrite: true);
            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {message}{(exception is null ? "" : $"\n{exception}")}\n");
        }
    }
}
