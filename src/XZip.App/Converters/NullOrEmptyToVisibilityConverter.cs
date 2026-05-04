using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace XZip.App.Converters;

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var empty = value switch
        {
            null => true,
            string s => string.IsNullOrEmpty(s),
            System.Collections.ICollection c => c.Count == 0,
            _ => false,
        };
        if (Inverse) empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
