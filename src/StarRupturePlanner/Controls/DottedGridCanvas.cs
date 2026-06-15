using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StarRupturePlanner.Controls;

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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var spacing = GridSize <= 0 ? 24 : GridSize;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        for (var x = 0d; x <= width; x += spacing)
        {
            var column = (int)Math.Round(x / spacing);
            for (var y = 0d; y <= height; y += spacing)
            {
                var row = (int)Math.Round(y / spacing);
                var major = column % 4 == 0 && row % 4 == 0;
                drawingContext.DrawEllipse(
                    major ? MajorDotBrush : DotBrush,
                    null,
                    new Point(x, y),
                    major ? 1.7 : 1.1,
                    major ? 1.7 : 1.1);
            }
        }
    }
}
