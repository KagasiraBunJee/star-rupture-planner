using System.Diagnostics;
using System.Windows.Media;

namespace StarRupturePlanner.Services;

/// <summary>Font/brush parsing helpers shared by the shell and the canvas cards.</summary>
public static class UiBrushHelpers
{
    public static string SafeFontFamily(string value)
        => string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value;

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
