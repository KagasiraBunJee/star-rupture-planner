using System.Text.Json.Serialization;

namespace StarRupturePlanner.Models;

public sealed class AppSettings
{
    [JsonPropertyName("theme")]
    public AppTheme Theme { get; set; } = AppTheme.System;

    [JsonPropertyName("planner_language")]
    public string PlannerLanguage { get; set; } = PlannerLanguages.English;

    [JsonPropertyName("api_port")]
    public int ApiPort { get; set; } = 8010;

    [JsonPropertyName("canvas_card_font")]
    public FontSettings CanvasCardFont { get; set; } = new()
    {
        Family = "Segoe UI",
        Size = 12,
        Color = "#F4F0E8",
    };

    [JsonPropertyName("left_bar_list_font")]
    public FontSettings LeftBarListFont { get; set; } = new()
    {
        Family = "Segoe UI",
        Size = 12,
        Color = "#F4F0E8",
    };

    [JsonPropertyName("current_rail_tier_id")]
    public string? CurrentRailTierId { get; set; }

    public static int NormalizeApiPort(int port) => port is >= 1 and <= 65535 ? port : 8010;
}

[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    System,
    Dark,
    Light,
}

public static class PlannerLanguages
{
    public const string English = "en";
    public const string Russian = "ru";
    public const string German = "de";
    public const string Ukrainian = "uk";

    public static readonly string[] Supported =
    [
        English,
        Russian,
        German,
        Ukrainian,
    ];

    public static string Normalize(string? language)
    {
        var code = string.IsNullOrWhiteSpace(language)
            ? English
            : language.Trim().ToLowerInvariant().Split('-', '_')[0];
        return Supported.Contains(code, StringComparer.Ordinal) ? code : English;
    }
}

public sealed class FontSettings
{
    [JsonPropertyName("family")]
    public string Family { get; set; } = "Segoe UI";

    [JsonPropertyName("size")]
    public double Size { get; set; } = 12;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#F4F0E8";
}
