using System.Diagnostics;
using System.IO;
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
                Outputs = ReadSchemeOutputs(path),
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

    public bool SchemeFileNameExists(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var sourcePath = Path.GetFullPath(filePath);
        var destinationPath = Path.Combine(FolderPath, Path.GetFileName(sourcePath));
        return File.Exists(destinationPath) && !PathUtil.SamePath(sourcePath, destinationPath);
    }

    public SchemeListItem ImportSchemeFile(string filePath, SchemeImportMode importMode = SchemeImportMode.KeepBoth)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Scheme file path is required.", nameof(filePath));
        }

        var sourcePath = Path.GetFullPath(filePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Scheme file was not found.", sourcePath);
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only scheme JSON files can be added.");
        }

        ValidateSchemeJsonFile(sourcePath);
        var document = Load(sourcePath);
        if (string.IsNullOrWhiteSpace(document.Name))
        {
            document.Name = Path.GetFileNameWithoutExtension(sourcePath);
        }

        Directory.CreateDirectory(FolderPath);
        var defaultDestinationPath = Path.Combine(FolderPath, Path.GetFileName(sourcePath));
        var destinationPath = importMode == SchemeImportMode.Replace
            ? defaultDestinationPath
            : NextAvailablePath(defaultDestinationPath);
        if (PathUtil.SamePath(sourcePath, destinationPath))
        {
            return new SchemeListItem
            {
                FilePath = sourcePath,
                Name = Path.GetFileNameWithoutExtension(sourcePath),
                Outputs = ReadSchemeOutputs(sourcePath),
            };
        }

        document.FilePath = destinationPath;
        Save(document);
        return new SchemeListItem
        {
            FilePath = destinationPath,
            Name = Path.GetFileNameWithoutExtension(destinationPath),
            Outputs = ReadSchemeOutputs(destinationPath),
        };
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

        RemoveReferencesToDeletedScheme(targetPath);
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }

    private static string NextAvailablePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(folder, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void ValidateSchemeJsonFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Selected JSON file is not a StarRupture scheme.");
        }

        var hasSchemeProperty =
            HasProperty(root, "version")
            || HasProperty(root, "name")
            || HasProperty(root, "canvas")
            || HasProperty(root, "nodes")
            || HasProperty(root, "edges")
            || HasProperty(root, "comments")
            || HasProperty(root, "corporation_levels")
            || HasProperty(root, "selected_rail_tier_id");
        if (!hasSchemeProperty)
        {
            throw new InvalidOperationException("Selected JSON file is not a StarRupture scheme.");
        }

        RequireKindIfPresent(root, "canvas", JsonValueKind.Object);
        RequireKindIfPresent(root, "nodes", JsonValueKind.Array);
        RequireKindIfPresent(root, "edges", JsonValueKind.Array);
        RequireKindIfPresent(root, "comments", JsonValueKind.Array);
        RequireKindIfPresent(root, "corporation_levels", JsonValueKind.Object);
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        return element.EnumerateObject()
            .Any(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static void RequireKindIfPresent(JsonElement element, string propertyName, JsonValueKind expectedKind)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != expectedKind)
            {
                throw new InvalidOperationException("Selected JSON file is not a valid StarRupture scheme.");
            }

            return;
        }
    }

    private static List<SchemeListOutputItem> ReadSchemeOutputs(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<SchemeDocument>(stream, JsonOptions);
            if (document is null)
            {
                return [];
            }

            return document.Nodes
                .Where(node => node.IsSchemeOutput && !string.IsNullOrWhiteSpace(node.SelectedRecipeKey))
                .Select(node => new SchemeListOutputItem
                {
                    RecipeKey = node.SelectedRecipeKey!,
                    MachineCount = ProductionAnalysisService.EffectiveMachineCount(node),
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchemeStore] Failed to read outputs from '{path}': {ex.Message}");
            return [];
        }
    }

    private void RemoveReferencesToDeletedScheme(string deletedSchemePath)
    {
        var deletedName = Path.GetFileNameWithoutExtension(deletedSchemePath);
        foreach (var path in Directory.EnumerateFiles(FolderPath, "*.json"))
        {
            var fullPath = Path.GetFullPath(path);
            if (string.Equals(fullPath, deletedSchemePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SchemeDocument document;
            try
            {
                document = Load(fullPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchemeStore] Failed to load scheme '{fullPath}' while removing references: {ex.Message}");
                continue;
            }

            var removedNodeIds = document.Nodes
                .Where(node => node.NodeType == SchemeNodeType.BlueprintSource
                    && ReferencesDeletedScheme(node, deletedSchemePath, deletedName))
                .Select(node => node.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (removedNodeIds.Count == 0)
            {
                continue;
            }

            document.Nodes.RemoveAll(node => removedNodeIds.Contains(node.Id));
            document.Edges.RemoveAll(edge =>
                removedNodeIds.Contains(edge.SourceNodeId) || removedNodeIds.Contains(edge.TargetNodeId));
            Save(document);
        }
    }

    private static bool ReferencesDeletedScheme(SchemeNode node, string deletedSchemePath, string deletedName)
    {
        if (!string.IsNullOrWhiteSpace(node.SourceSchemePath)
            && string.Equals(Path.GetFullPath(node.SourceSchemePath), deletedSchemePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(node.SourceSchemePath)
            && string.Equals(node.SourceSchemeName, deletedName, StringComparison.CurrentCultureIgnoreCase);
    }
}
