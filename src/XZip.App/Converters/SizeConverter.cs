using Microsoft.UI.Xaml.Data;

using XZip.App.ViewModels;

namespace XZip.App.Converters;

public sealed class SizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var bytes = value switch
        {
            long l => l,
            int i => (long)i,
            _ => 0L,
        };
        return ArchiveEntryViewModel.FormatSize(bytes);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
