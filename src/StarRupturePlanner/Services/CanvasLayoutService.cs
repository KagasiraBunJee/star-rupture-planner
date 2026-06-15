using System.Windows;

namespace StarRupturePlanner.Services;

public sealed class CanvasLayoutService : ICanvasLayoutService
{
    public CanvasLayoutService(double gridSize = 24)
    {
        if (gridSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize), "Grid size must be positive.");
        }

        GridSize = gridSize;
    }

    public double GridSize { get; }

    public double Snap(double value)
    {
        return Math.Round(value / GridSize, MidpointRounding.AwayFromZero) * GridSize;
    }

    public Point Snap(Point point)
    {
        return new Point(Snap(point.X), Snap(point.Y));
    }
}
