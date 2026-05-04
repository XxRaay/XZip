using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace XZip.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var b = value is bool v && v;
        if (parameter is string s && s.Equals("inverse", StringComparison.OrdinalIgnoreCase)) b = !b;
        if (Inverse) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}
