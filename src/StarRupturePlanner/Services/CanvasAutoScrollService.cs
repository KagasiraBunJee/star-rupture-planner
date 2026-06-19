using System.Windows;

namespace StarRupturePlanner.Services;

public static class CanvasAutoScrollService
{
    public const double EdgeThreshold = 80;
    public const double MaxPixelsPerSecond = 720;

    public static Vector TranslateDelta(
        Point viewportPoint,
        Size viewportSize,
        double elapsedSeconds,
        double edgeThreshold = EdgeThreshold,
        double maxPixelsPerSecond = MaxPixelsPerSecond)
    {
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0 || elapsedSeconds <= 0)
        {
            return new Vector();
        }

        var velocity = new Vector(
            AxisVelocity(viewportPoint.X, viewportSize.Width, edgeThreshold, maxPixelsPerSecond),
            AxisVelocity(viewportPoint.Y, viewportSize.Height, edgeThreshold, maxPixelsPerSecond));
        return velocity * elapsedSeconds;
    }

    private static double AxisVelocity(double position, double length, double threshold, double maxPixelsPerSecond)
    {
        if (threshold <= 0 || maxPixelsPerSecond <= 0 || length <= 0)
        {
            return 0;
        }

        if (position < threshold)
        {
            var factor = Math.Clamp((threshold - position) / threshold, 0, 1);
            return maxPixelsPerSecond * factor;
        }

        if (position > length - threshold)
        {
            var factor = Math.Clamp((position - (length - threshold)) / threshold, 0, 1);
            return -maxPixelsPerSecond * factor;
        }

        return 0;
    }
}
