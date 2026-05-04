using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace XZip.App.ViewModels;

public enum PreviewKind { None, Text, Image, Hex, TooLarge }

public partial class PreviewViewModel : ObservableObject
{
    [ObservableProperty]
    private PreviewKind _kind = PreviewKind.None;

    [ObservableProperty]
    private string? _entryName;

    [ObservableProperty]
    private string? _textContent;

    [ObservableProperty]
    private BitmapImage? _image;

    [ObservableProperty]
    private string? _hexContent;

    [ObservableProperty]
    private string? _statusText;

    public bool IsTextVisible => Kind == PreviewKind.Text;
    public bool IsImageVisible => Kind == PreviewKind.Image;
    public bool IsHexVisible => Kind == PreviewKind.Hex;
    public bool IsEmptyVisible => Kind == PreviewKind.None;
    public bool IsTooLargeVisible => Kind == PreviewKind.TooLarge;

    partial void OnKindChanged(PreviewKind value)
    {
        OnPropertyChanged(nameof(IsTextVisible));
        OnPropertyChanged(nameof(IsImageVisible));
        OnPropertyChanged(nameof(IsHexVisible));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(IsTooLargeVisible));
    }

    public void Reset()
    {
        EntryName = null;
        TextContent = null;
        Image = null;
        HexContent = null;
        StatusText = null;
        Kind = PreviewKind.None;
    }
}
