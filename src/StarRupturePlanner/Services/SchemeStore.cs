using System.Text.Json;
using System.Text.Json.Serialization;
using StarRupturePlanner.Models;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace StarRupturePlanner.Services;

public sealed class SchemeStore : DocumentStoreBase<SchemeDocument, SchemeListItem>, ISchemeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SchemeStore(string? folderPath = null)
        : base(folderPath ?? DefaultFolderPath())
    {
    }

    public static string DefaultFolderPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "StarRupture Planner",
            "Schemes");
    }

    public IReadOnlyList<SchemeListItem> ListSchemes() => List();

    public override IReadOnlyList<SchemeListItem> List()
    {
        Directory.CreateDirectory(FolderPath);
        return Directory
            .EnumerateFiles(FolderPath, "*.json")
            .Select(path => new SchemeListItem
            {
                FilePath = path,
                Name = Path.GetFileNameWithoutExtension(path),
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public override SchemeDocument Load(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var document = JsonSerializer.Deserialize<SchemeDocument>(stream, JsonOptions)
            ?? new SchemeDocument();
        document.FilePath = filePath;
        return document;
    }

    public override string Save(SchemeDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Name))
        {
            document.Name = "Untitled";
        }

        document.Version = SchemeMigrationService.CurrentVersion;
        document.FilePath ??= Path.Combine(FolderPath, $"{SafeFileName(document.Name)}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(document.FilePath)!);
        using var stream = File.Create(document.FilePath);
        JsonSerializer.Serialize(stream, document, JsonOptions);
        return document.FilePath;
    }

    public void Delete(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Scheme file path is required.", nameof(filePath));
        }

        var folderPath = Path.GetFullPath(FolderPath);
        var folderRoot = folderPath.EndsWith(Path.DirectorySeparatorChar)
            ? folderPath
            : folderPath + Path.DirectorySeparatorChar;
        var targetPath = Path.GetFullPath(filePath);

        if (!targetPath.StartsWith(folderRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Scheme file is outside the active scheme folder.");
        }

        if (!string.Equals(Path.GetExtension(targetPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only saved scheme JSON files can be deleted.");
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }
}
