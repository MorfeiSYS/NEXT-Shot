using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace NEXTShot.Converters;

public sealed class CaptureToolbarIconConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        var selected = values[1] is true;
        var uriPath = path;
        if (selected && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            uriPath = path[..^4] + ".checked.png";

        var rel = new Uri(uriPath, UriKind.Relative);
        try
        {
            return new BitmapImage(rel);
        }
        catch
        {
            if (!selected) return null;
            try
            {
                return new BitmapImage(new Uri(path, UriKind.Relative));
            }
            catch
            {
                return null;
            }
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
