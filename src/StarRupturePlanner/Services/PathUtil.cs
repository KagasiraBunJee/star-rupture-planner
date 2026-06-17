using System.Diagnostics;

namespace StarRupturePlanner.Services;

/// <summary>File-path helpers shared across UI modules. Extracted from MainWindow.</summary>
public static class PathUtil
{
    /// <summary>True if both paths resolve to the same full path (case-insensitive).</summary>
    public static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            left = System.IO.Path.GetFullPath(left);
            right = System.IO.Path.GetFullPath(right);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PathUtil.SamePath] Failed to normalize paths: {ex.Message}");
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
