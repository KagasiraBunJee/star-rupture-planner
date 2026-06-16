using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StarRupturePlanner.Controls;

/// <summary>
/// Paints an infinite dotted grid in screen space. The element fills the viewport and is
/// NOT transformed; instead it is given the pan offset and zoom of the content layer and
/// draws only the dots visible in the current viewport. Because it draws in screen space,
/// the grid tiles infinitely in every direction (including negative world coordinates) and
/// the drawn dot count stays bounded by viewport size / zoom.
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

    // Pan offset (TranslateTransform) and zoom (ScaleTransform) of the content layer the grid
    // sits behind. A world point (wx,wy) maps to screen (wx*Zoom + Offset).
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

    // Re-render when the viewport size changes (the element fills the viewport).
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var result = base.ArrangeOverride(arrangeSize);
        InvalidateVisual();
        return result;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var spacing = GridSize <= 0 ? 24 : GridSize;
        var zoom = Zoom <= 0 ? 1 : Zoom;
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Screen distance between adjacent dots. Guard against pathological density.
        var step = spacing * zoom;
        if (step < 2)
        {
            return;
        }

        var minorRadius = Math.Max(0.5, 1.1 * zoom);
        var majorRadius = Math.Max(0.8, 1.7 * zoom);

        // Visible world-cell index range: screen x = OffsetX + column*step must fall in [0, width].
        // Columns/rows may be negative — the grid tiles in every direction.
        var startColumn = (int)Math.Floor((0 - OffsetX) / step) - 1;
        var endColumn = (int)Math.Ceiling((width - OffsetX) / step) + 1;
        var startRow = (int)Math.Floor((0 - OffsetY) / step) - 1;
        var endRow = (int)Math.Ceiling((height - OffsetY) / step) + 1;

        for (var column = startColumn; column <= endColumn; column++)
        {
            var x = OffsetX + column * step;
            var majorColumn = column % 4 == 0;
            for (var row = startRow; row <= endRow; row++)
            {
                var major = majorColumn && row % 4 == 0;
                drawingContext.DrawEllipse(
                    major ? MajorDotBrush : DotBrush,
                    null,
                    new Point(x, OffsetY + row * step),
                    major ? majorRadius : minorRadius,
                    major ? majorRadius : minorRadius);
            }
        }
    }
}
