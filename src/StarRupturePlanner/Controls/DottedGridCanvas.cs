using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StarRupturePlanner.Controls;

/// <summary>
/// A canvas that paints an effectively infinite dotted grid. Dots are drawn only
/// for the currently visible viewport (computed from the pan offset, zoom and
/// viewport size), so the grid extends in every direction as the user pans while
/// the number of drawn dots stays bounded.
/// </summary>
public sealed class DottedGridCanvas : Canvas
{
    public static readonly DependencyProperty GridSizeProperty =
        DependencyProperty.Register(
            nameof(GridSize),
            typeof(double),
            typeof(DottedGridCanvas),
            new FrameworkPropertyMetadata(24d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotBrushProperty =
        DependencyProperty.Register(
            nameof(DotBrush),
            typeof(Brush),
            typeof(DottedGridCanvas),
            new FrameworkPropertyMetadata(
                new SolidColorBrush(Color.FromRgb(52, 60, 65)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MajorDotBrushProperty =
        DependencyProperty.Register(
            nameof(MajorDotBrush),
            typeof(Brush),
            typeof(DottedGridCanvas),
            new FrameworkPropertyMetadata(
                new SolidColorBrush(Color.FromRgb(70, 80, 86)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    // Pan offset (TranslateTransform), zoom (ScaleTransform) and viewport size that
    // the grid is rendered through. Bound to the canvas' own RenderTransform values
    // and the host viewport so the visible region can be culled correctly.
    public static readonly DependencyProperty OffsetXProperty =
        DependencyProperty.Register(
            nameof(OffsetX),
            typeof(double),
            typeof(DottedGridCanvas),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OffsetYProperty =
        DependencyProperty.Register(
            nameof(OffsetY),
            typeof(double),
            typeof(DottedGridCanvas),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(
            nameof(Zoom),
            typeof(double),
            typeof(DottedGridCanvas),
            new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewportWidthProperty =
        DependencyProperty.Register(
            nameof(ViewportWidth),
            typeof(double),
            typeof(DottedGridCanvas),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewportHeightProperty =
        DependencyProperty.Register(
            nameof(ViewportHeight),
            typeof(double),
            typeof(DottedGridCanvas),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double GridSize
    {
        get => (double)GetValue(GridSizeProperty);
        set => SetValue(GridSizeProperty, value);
    }

    public Brush DotBrush
    {
        get => (Brush)GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public Brush MajorDotBrush
    {
        get => (Brush)GetValue(MajorDotBrushProperty);
        set => SetValue(MajorDotBrushProperty, value);
    }

    public double OffsetX
    {
        get => (double)GetValue(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    public double OffsetY
    {
        get => (double)GetValue(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double ViewportWidth
    {
        get => (double)GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    public double ViewportHeight
    {
        get => (double)GetValue(ViewportHeightProperty);
        set => SetValue(ViewportHeightProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var spacing = GridSize <= 0 ? 24 : GridSize;
        var zoom = Zoom <= 0 ? 1 : Zoom;

        // The canvas is rendered through ScaleTransform(zoom) then TranslateTransform(offset),
        // so a point at local (lx,ly) appears on screen at (lx*zoom + offset). Invert that to
        // find which local coordinates fall inside the visible viewport.
        //
        // Only draw once the real viewport size is known. Never fall back to the canvas'
        // own (enormous) extent here, or a single render would try to draw the entire
        // 200000x200000 area worth of dots and exhaust memory.
        var viewportWidth = ViewportWidth;
        var viewportHeight = ViewportHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var minX = (0 - OffsetX) / zoom;
        var maxX = (viewportWidth - OffsetX) / zoom;
        var minY = (0 - OffsetY) / zoom;
        var maxY = (viewportHeight - OffsetY) / zoom;

        // One extra cell of margin so dots never pop in at the edges. Columns/rows may be
        // negative — the grid tiles infinitely in every direction, only ever drawing the
        // cells that fall inside the current viewport.
        var startColumn = (int)Math.Floor(minX / spacing) - 1;
        var endColumn = (int)Math.Ceiling(maxX / spacing) + 1;
        var startRow = (int)Math.Floor(minY / spacing) - 1;
        var endRow = (int)Math.Ceiling(maxY / spacing) + 1;

        for (var column = startColumn; column <= endColumn; column++)
        {
            var x = column * spacing;
            var majorColumn = column % 4 == 0;
            for (var row = startRow; row <= endRow; row++)
            {
                var major = majorColumn && row % 4 == 0;
                drawingContext.DrawEllipse(
                    major ? MajorDotBrush : DotBrush,
                    null,
                    new Point(x, row * spacing),
                    major ? 1.7 : 1.1,
                    major ? 1.7 : 1.1);
            }
        }
    }
}
