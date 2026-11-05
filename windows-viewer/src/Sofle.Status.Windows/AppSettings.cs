using System.Text.Json;

namespace Sofle.Status.Windows;

internal sealed class AppSettings
{
    public string? DeviceId { get; set; }
    public bool ShowPanel { get; set; } = true;
    public int? PanelX { get; set; }
    public int? PanelY { get; set; }
    public Dictionary<int, string> LayerNames { get; set; } = new()
    {
        [0] = "BASE", [1] = "LOWER", [2] = "RAISE", [3] = "ADJUST", [4] = "GAMING"
    };

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SofleStatus");
    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("Settings file contains null, not an object.");
        if (settings.LayerNames is null || settings.LayerNames.Any(pair =>
            pair.Key is < 0 or >= 32 || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 40))
            throw new InvalidDataException("Layer names must have IDs 0-31 and nonempty names of at most 40 characters.");
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
