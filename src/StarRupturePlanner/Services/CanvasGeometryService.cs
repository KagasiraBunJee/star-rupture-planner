using System.Windows;
using System.Windows.Media;
using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public static class CanvasGeometryService
{
    public static PathGeometry CreateBezier(
        Point start,
        Point end,
        string startDirection = "output",
        string endDirection = "free")
    {
        var offset = Math.Max(80, Math.Abs(end.X - start.X) * 0.45);
        var startOffset = startDirection == "input" ? -offset : offset;
        var endOffset = endDirection switch
        {
            "input" => -offset,
            "output" => offset,
            _ => end.X < start.X ? offset : -offset,
        };
        var segment = new BezierSegment(
            new Point(start.X + startOffset, start.Y),
            new Point(end.X + endOffset, end.Y),
            end,
            true);
        return new PathGeometry([new PathFigure(start, [segment], false)]);
    }

    public static List<Point> EdgePoints(SchemeEdge edge, Point sourcePoint, Point targetPoint)
    {
        var points = new List<Point> { sourcePoint };
        points.AddRange(edge.RoutePoints.Select(point => new Point(point.X, point.Y)));
        points.Add(targetPoint);
        return points;
    }

    public static PathGeometry CreateRoutedGeometry(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
        {
            return new PathGeometry();
        }

        var figure = new PathFigure { StartPoint = points[0], IsClosed = false };
        if (points.Count == 1)
        {
            return new PathGeometry([figure]);
        }

        for (var index = 0; index < points.Count - 1; index++)
        {
            var p0 = index == 0 ? points[index] : points[index - 1];
            var p1 = points[index];
            var p2 = points[index + 1];
            var p3 = index + 2 >= points.Count ? points[index + 1] : points[index + 2];

            var control1 = new Point(
                p1.X + (p2.X - p0.X) / 6,
                p1.Y + (p2.Y - p0.Y) / 6);
            var control2 = new Point(
                p2.X - (p3.X - p1.X) / 6,
                p2.Y - (p3.Y - p1.Y) / 6);
            figure.Segments.Add(new BezierSegment(
                control1,
                control2,
                p2,
                true));
        }

        return new PathGeometry([figure]);
    }

    public static CanvasLabelPlacement LabelPlacementAboveLine(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
        {
            return new CanvasLabelPlacement(new Point(), 0);
        }

        if (points.Count == 1)
        {
            return new CanvasLabelPlacement(new Point(points[0].X, points[0].Y - 28), 0);
        }

        var bestStart = points[0];
        var bestEnd = points[1];
        var bestLength = 0d;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var length = Distance(points[index], points[index + 1]);
            if (length > bestLength)
            {
                bestLength = length;
                bestStart = points[index];
                bestEnd = points[index + 1];
            }
        }

        var mid = new Point((bestStart.X + bestEnd.X) / 2, (bestStart.Y + bestEnd.Y) / 2);
        var dx = bestEnd.X - bestStart.X;
        var dy = bestEnd.Y - bestStart.Y;
        var lengthSafe = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var normalX = -dy / lengthSafe;
        var normalY = dx / lengthSafe;
        if (normalY > 0)
        {
            normalX = -normalX;
            normalY = -normalY;
        }

        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (angle > 90 || angle < -90)
        {
            angle += 180;
        }

        return new CanvasLabelPlacement(
            new Point(mid.X + normalX * 18 - 80, mid.Y + normalY * 18 - 26),
            angle);
    }

    public static double Distance(Point a, Point b)
    {
        return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    }

    public static double DistanceToSegment(Point point, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return Distance(point, start);
        }

        var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        var projection = new Point(start.X + t * dx, start.Y + t * dy);
        return Distance(point, projection);
    }
}

public sealed record CanvasLabelPlacement(Point Position, double AngleDegrees);
