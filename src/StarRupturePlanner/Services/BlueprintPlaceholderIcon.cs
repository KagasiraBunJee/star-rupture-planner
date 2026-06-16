using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StarRupturePlanner.Services;

public static class BlueprintPlaceholderIcon
{
    public static ImageSource Image { get; } = CreateImage();

    public static ImageSource FromUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return Image;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imageUrl, UriKind.RelativeOrAbsolute);
            bitmap.CacheOption = BitmapCacheOption.OnDemand;
            bitmap.EndInit();

            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BlueprintPlaceholderIcon] Failed to load image '{imageUrl}': {ex.Message}");
            return Image;
        }
    }

    private static ImageSource CreateImage()
    {
        var drawing = new DrawingGroup
        {
            ClipGeometry = new RectangleGeometry(new Rect(0, 0, 64, 64)),
        };

        var pageFill = new SolidColorBrush(Color.FromRgb(8, 31, 55));
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(72, 56, 189, 255)), 1);
        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(83, 211, 255)), 3)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(46, 154, 218)), 2);
        var nodeFill = new SolidColorBrush(Color.FromRgb(28, 96, 139));
        var nodePen = new Pen(new SolidColorBrush(Color.FromRgb(117, 225, 255)), 2);
        var foldFill = new SolidColorBrush(Color.FromRgb(12, 51, 83));

        drawing.Children.Add(new GeometryDrawing(pageFill, borderPen, new RectangleGeometry(new Rect(7, 7, 50, 50), 5, 5)));

        var gridLines = new GeometryGroup();
        for (var offset = 17; offset <= 47; offset += 10)
        {
            gridLines.Children.Add(new LineGeometry(new Point(offset, 9), new Point(offset, 55)));
            gridLines.Children.Add(new LineGeometry(new Point(9, offset), new Point(55, offset)));
        }

        drawing.Children.Add(new GeometryDrawing(null, gridPen, gridLines));
        drawing.Children.Add(new GeometryDrawing(foldFill, borderPen, Geometry.Parse("M 43 7 L 57 21 L 43 21 Z")));

        var route = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(18, 41), IsClosed = false };
        figure.Segments.Add(new LineSegment(new Point(30, 29), true));
        figure.Segments.Add(new LineSegment(new Point(41, 35), true));
        figure.Segments.Add(new LineSegment(new Point(49, 25), true));
        route.Figures.Add(figure);
        drawing.Children.Add(new GeometryDrawing(null, linePen, route));

        drawing.Children.Add(new GeometryDrawing(nodeFill, nodePen, new EllipseGeometry(new Point(18, 41), 5, 5)));
        drawing.Children.Add(new GeometryDrawing(nodeFill, nodePen, new EllipseGeometry(new Point(30, 29), 5, 5)));
        drawing.Children.Add(new GeometryDrawing(nodeFill, nodePen, new EllipseGeometry(new Point(41, 35), 5, 5)));
        drawing.Children.Add(new GeometryDrawing(nodeFill, nodePen, new EllipseGeometry(new Point(49, 25), 5, 5)));

        var image = new DrawingImage(drawing);
        if (image.CanFreeze)
        {
            image.Freeze();
        }

        return image;
    }
}
