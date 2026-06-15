using System.Text.Json;
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

        document.FilePath ??= Path.Combine(FolderPath, $"{SafeFileName(document.Name)}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(document.FilePath)!);
        using var stream = File.Create(document.FilePath);
        JsonSerializer.Serialize(stream, document, JsonOptions);
        return document.FilePath;
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }
}
