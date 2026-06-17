using System.Diagnostics;
using System.Windows.Controls;

namespace StarRupturePlanner.Services;

/// <summary>
/// Loads asset images into an <see cref="Image"/>, falling back to the blueprint placeholder.
/// Shared by the inspector and the canvas node cards. Extracted from MainWindow.
/// </summary>
public static class UiImageLoader
{
    public static void SetImage(IPlannerApiClient apiClient, Image image, string? assetUrl)
    {
        var absolute = apiClient.ToAbsoluteAssetUrl(assetUrl);
        if (string.IsNullOrWhiteSpace(absolute))
        {
            image.Source = BlueprintPlaceholderIcon.Image;
            return;
        }

        try
        {
            image.Source = BlueprintPlaceholderIcon.FromUrl(absolute);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UiImageLoader.SetImage] Failed to load image '{absolute}': {ex.Message}");
            image.Source = BlueprintPlaceholderIcon.Image;
        }
    }
}
