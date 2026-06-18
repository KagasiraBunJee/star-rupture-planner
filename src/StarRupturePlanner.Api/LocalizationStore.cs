using System.Text.Json;
using System.Text.Json.Nodes;

namespace StarRupturePlanner.Api;

public sealed class LocalizationStore
{
    private const string DefaultLanguage = "en";
    private static readonly string[] SupportedLanguages = ["en", "ru", "de", "uk"];
    private readonly Dictionary<string, JsonObject> _packs;

    public LocalizationStore(ApiSettings settings)
    {
        _packs = SupportedLanguages.ToDictionary(language => language, language => Load(settings.LocalizationDir, language));
    }

    public IReadOnlyList<Dictionary<string, string>> Languages { get; } =
    [
        new() { ["code"] = "en", ["name"] = "English" },
        new() { ["code"] = "ru", ["name"] = "Russian" },
        new() { ["code"] = "de", ["name"] = "German" },
        new() { ["code"] = "uk", ["name"] = "Ukrainian" },
    ];

    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return DefaultLanguage;
        }

        string code = language.Trim().ToLowerInvariant().Replace('_', '-').Split('-', 2)[0];
        return SupportedLanguages.Contains(code, StringComparer.Ordinal) ? code : DefaultLanguage;
    }

    public object? Text(string? language, string section, string? key, string field, object? fallback)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        string normalized = NormalizeLanguage(language);
        if (normalized != DefaultLanguage)
        {
            object? localized = Lookup(normalized, section, key, field, null);
            if (localized is not null and not "")
            {
                return localized;
            }
        }

        return Lookup(DefaultLanguage, section, key, field, fallback);
    }

    public string? Ui(string? language, string key, string? fallback)
    {
        string normalized = NormalizeLanguage(language);
        string? localized = LookupString(normalized, "ui", key);
        if (!string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        localized = LookupString(DefaultLanguage, "ui", key);
        return string.IsNullOrWhiteSpace(localized) ? fallback : localized;
    }

    private object? Lookup(string language, string section, string key, string field, object? fallback)
    {
        JsonNode? value = _packs.GetValueOrDefault(language)?[section]?[key]?[field];
        if (value is null)
        {
            return fallback;
        }

        if (value is JsonValue jsonValue)
        {
            return jsonValue.GetValueKind() switch
            {
                JsonValueKind.String => jsonValue.GetValue<string>(),
                JsonValueKind.Number when jsonValue.TryGetValue(out long number) => number,
                JsonValueKind.Number when jsonValue.TryGetValue(out double number) => number,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback,
            };
        }

        return fallback;
    }

    private string? LookupString(string language, string section, string key)
    {
        JsonNode? value = _packs.GetValueOrDefault(language)?[section]?[key];
        return value is JsonValue jsonValue && jsonValue.TryGetValue(out string? text) ? text : null;
    }

    private static JsonObject Load(string localizationDir, string language)
    {
        string path = Path.Combine(localizationDir, language + ".json");
        if (!File.Exists(path))
        {
            return [];
        }

        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ?? [];
    }
}
