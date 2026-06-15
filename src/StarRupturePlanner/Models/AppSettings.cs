using System.Text.Json.Serialization;

namespace StarRupturePlanner.Models;

public sealed class AppSettings
{
    [JsonPropertyName("theme")]
    public AppTheme Theme { get; set; } = AppTheme.System;

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
}

[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    System,
    Dark,
    Light,
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
