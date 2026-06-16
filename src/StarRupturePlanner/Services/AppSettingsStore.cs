using System.Diagnostics;
using System.Text.Json;
using StarRupturePlanner.Models;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace StarRupturePlanner.Services;

public sealed class AppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public AppSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultFilePath();
    }

    public string FilePath { get; }

    public static string DefaultFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StarRupture Planner",
            "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettingsStore] Failed to load settings: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using var stream = File.Create(FilePath);
            JsonSerializer.Serialize(stream, settings, JsonOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettingsStore] Failed to save settings: {ex.Message}");
        }
    }
}
