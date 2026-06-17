using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace StarRupturePlanner.Services;

/// <summary>Font/brush parsing helpers shared by the shell and the canvas cards.</summary>
public static class UiBrushHelpers
{
    public static string SafeFontFamily(string value)
        => string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value;

    /// <summary>Resolves a theme brush's colour from the merged resource dictionaries (theme-aware).</summary>
    public static Color ThemeColor(string resourceKey, Color fallback)
        => Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush ? brush.Color : fallback;

    public static SolidColorBrush ThemeBrush(string resourceKey, Color fallback)
        => new(ThemeColor(resourceKey, fallback));

    public static SolidColorBrush BrushFromString(string? value, string fallback)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value ?? fallback)!);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UiBrushHelpers.BrushFromString] Invalid color '{value}': {ex.Message}");
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)!);
        }
    }
}
