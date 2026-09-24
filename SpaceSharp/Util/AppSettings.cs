using System.Text.Json;

namespace SpaceSharp.Util;

/// <summary>User preferences, saved to %AppData%\SpaceSharp\settings.json.</summary>
internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceSharp", "settings.json");

    private static AppSettings? _current;

    /// <summary>The one shared instance, loaded on first use.</summary>
    public static AppSettings Current => _current ??= Load();

    public string? Palette { get; set; }
    public string? ColorMode { get; set; }
    public string? Theme { get; set; }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to save preferences shouldn't interrupt anything.
        }
    }
}
