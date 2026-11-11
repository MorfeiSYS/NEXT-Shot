using System.Globalization;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace NEXTShot.Converters;

public sealed class PackUriToImageConverter : IValueConverter
{
    private static readonly string AssemblyName =
        Assembly.GetExecutingAssembly().GetName().Name ?? "NEXTShot";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = ToPackUri(path);
            img.EndInit();
            if (img.CanFreeze) img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }

    internal static Uri ToPackUri(string path)
    {
        path = path.Trim().Replace('\\', '/').TrimStart('/');
        return new Uri($"pack://application:,,,/{AssemblyName};component/{path}", UriKind.Absolute);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
