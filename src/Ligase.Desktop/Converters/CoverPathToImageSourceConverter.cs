using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Ligase.Host.Desktop.Converters;

public sealed class CoverPathToImageSourceConverter : IValueConverter
{
    public object? Convert(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        return Uri.TryCreate(path, UriKind.Absolute, out var uri)
            ? new BitmapImage(uri)
            : null;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        string language) =>
        throw new NotSupportedException();
}
