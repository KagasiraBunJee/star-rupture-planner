using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StarRupturePlanner.Services;

namespace StarRupturePlanner.Controls;

public sealed class EdgeLayer : FrameworkElement
{
    private const double HitTolerance = 10;
    private const double LabelPaddingX = 8;
    private const double LabelPaddingY = 4;

    private static readonly Color RemovalHighlightColor = Color.FromRgb(0xFF, 0x48, 0x48);

    private IReadOnlyList<EdgeRenderItem> _edges = [];
    private string? _hoveredEdgeId;
    private IReadOnlyList<string>? _markedEdgeIds;

    public event EventHandler<EdgeLayerMouseEventArgs>? EdgeMouseLeftButtonDown;

    public EdgeLayer()
    {
        Focusable = false;
        Cursor = Cursors.Hand;
    }

    public void SetEdges(IReadOnlyList<EdgeRenderItem> edges)
    {
        _edges = edges;
        InvalidateVisual();
    }

    public void SetMarkedEdges(IReadOnlyList<string>? edgeIds)
    {
        _markedEdgeIds = edgeIds;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTestEdge(e.GetPosition(this));
        if (hit != _hoveredEdgeId)
        {
            _hoveredEdgeId = hit;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredEdgeId != null)
        {
            _hoveredEdgeId = null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var edgeId = HitTestEdge(e.GetPosition(this));
        if (edgeId is null)
        {
            base.OnMouseLeftButtonDown(e);
            return;
        }

        EdgeMouseLeftButtonDown?.Invoke(this, new EdgeLayerMouseEventArgs(edgeId, e));
        e.Handled = true;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return HitTestEdge(hitTestParameters.HitPoint) is null
            ? null
            : new PointHitTestResult(this, hitTestParameters.HitPoint);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Non-highlighted edges first so highlighted ones render on top.
        foreach (var edge in _edges)
        {
            if (IsHighlighted(edge.Id)) continue;
            DrawEdge(drawingContext, edge, pixelsPerDip, hover: false, marked: false);
        }
        foreach (var edge in _edges)
        {
            var marked = _markedEdgeIds?.Contains(edge.Id) == true;
            var hover = !marked && _hoveredEdgeId == edge.Id;
            if (!marked && !hover) continue;
            DrawEdge(drawingContext, edge, pixelsPerDip, hover, marked);
        }
    }

    private bool IsHighlighted(string edgeId) =>
        _hoveredEdgeId == edgeId || _markedEdgeIds?.Contains(edgeId) == true;

    private static void DrawEdge(DrawingContext dc, EdgeRenderItem edge, double pixelsPerDip, bool hover, bool marked)
    {
        if (edge.Points.Count < 2)
        {
            DrawInvalidLabel(dc, edge, pixelsPerDip);
            return;
        }

        var geometry = CanvasGeometryService.CreateRoutedGeometry(edge.Points);
        geometry.Freeze();

        Color strokeColor;
        byte glowAlpha;
        double glowWidth, strokeWidth;

        if (marked)
        {
            strokeColor = RemovalHighlightColor;
            glowAlpha = 85;
            glowWidth = edge.IsValid ? 12 : 9;
            strokeWidth = edge.IsValid ? 4.5 : 3.5;
        }
        else if (hover)
        {
            strokeColor = edge.StrokeColor;
            glowAlpha = 115;
            glowWidth = edge.IsValid ? 13 : 10;
            strokeWidth = edge.IsValid ? 4.5 : 3.5;
        }
        else
        {
            strokeColor = edge.StrokeColor;
            glowAlpha = 72;
            glowWidth = edge.IsValid ? 8.5 : 6;
            strokeWidth = edge.IsValid ? 3.5 : 2.5;
        }

        var glowPen = new Pen(
            new SolidColorBrush(Color.FromArgb(glowAlpha, strokeColor.R, strokeColor.G, strokeColor.B)),
            glowWidth);
        glowPen.Freeze();
        dc.DrawGeometry(null, glowPen, geometry);

        var strokePen = new Pen(new SolidColorBrush(strokeColor), strokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        strokePen.Freeze();
        dc.DrawGeometry(null, strokePen, geometry);

        DrawLabel(dc, edge, pixelsPerDip);
    }

    private string? HitTestEdge(Point point)
    {
        for (var edgeIndex = _edges.Count - 1; edgeIndex >= 0; edgeIndex--)
        {
            var edge = _edges[edgeIndex];
            if (edge.Points.Count < 2)
            {
                continue;
            }

            for (var pointIndex = 0; pointIndex < edge.Points.Count - 1; pointIndex++)
            {
                if (CanvasGeometryService.DistanceToSegment(point, edge.Points[pointIndex], edge.Points[pointIndex + 1]) <= HitTolerance)
                {
                    return edge.Id;
                }
            }

            if (IsInsideLabel(point, edge))
            {
                return edge.Id;
            }
        }

        return null;
    }

    private static bool IsInsideLabel(Point point, EdgeRenderItem edge)
    {
        var width = Math.Clamp(edge.Label.Length * edge.FontSize * 0.58 + LabelPaddingX * 2, 72, 420);
        var height = edge.FontSize + LabelPaddingY * 2 + 2;
        return new Rect(edge.LabelPosition, new Size(width, height)).Contains(point);
    }

    private static void DrawInvalidLabel(DrawingContext drawingContext, EdgeRenderItem edge, double pixelsPerDip)
    {
        var labelEdge = edge with
        {
            Label = string.IsNullOrWhiteSpace(edge.Label) ? "Invalid connection" : edge.Label,
            LabelPosition = edge.Points.Count > 0 ? new Point(edge.Points[0].X + 12, edge.Points[0].Y - 28) : edge.LabelPosition,
            LabelAngleDegrees = 0,
        };
        DrawLabel(drawingContext, labelEdge, pixelsPerDip);
    }

    private static void DrawLabel(DrawingContext drawingContext, EdgeRenderItem edge, double pixelsPerDip)
    {
        if (string.IsNullOrWhiteSpace(edge.Label))
        {
            return;
        }

        var typeface = new Typeface(edge.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(
            edge.Label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            edge.FontSize,
            new SolidColorBrush(edge.TextColor),
            pixelsPerDip);

        var size = new Size(text.Width + LabelPaddingX * 2, text.Height + LabelPaddingY * 2);
        var rect = new Rect(edge.LabelPosition, size);
        var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

        drawingContext.PushTransform(new RotateTransform(edge.LabelAngleDegrees, center.X, center.Y));
        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(edge.LabelBackground),
            null,
            rect,
            4,
            4);
        drawingContext.DrawText(text, new Point(rect.Left + LabelPaddingX, rect.Top + LabelPaddingY));
        drawingContext.Pop();
    }
}

public sealed record EdgeRenderItem(
    string Id,
    IReadOnlyList<Point> Points,
    string Label,
    Point LabelPosition,
    double LabelAngleDegrees,
    Color StrokeColor,
    Color TextColor,
    Color LabelBackground,
    FontFamily FontFamily,
    double FontSize,
    bool IsValid);

public sealed class EdgeLayerMouseEventArgs(string edgeId, MouseButtonEventArgs originalEventArgs) : EventArgs
{
    public string EdgeId { get; } = edgeId;

    public MouseButtonEventArgs OriginalEventArgs { get; } = originalEventArgs;
}
