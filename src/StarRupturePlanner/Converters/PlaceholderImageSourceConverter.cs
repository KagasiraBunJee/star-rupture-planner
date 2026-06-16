using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using StarRupturePlanner.Services;

namespace StarRupturePlanner.Converters;

public sealed class PlaceholderImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            ImageSource imageSource => imageSource,
            string imageUrl => BlueprintPlaceholderIcon.FromUrl(imageUrl),
            _ => BlueprintPlaceholderIcon.Image,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
