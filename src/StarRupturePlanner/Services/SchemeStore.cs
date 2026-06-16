using System.Diagnostics;
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
