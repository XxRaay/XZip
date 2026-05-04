using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

using XZip.App.Services;
using XZip.Core.Abstractions;

namespace XZip.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        _theme = settings.Theme;
        _backdrop = settings.Backdrop;
        _defaultFormat = settings.DefaultFormat;
    }

    [ObservableProperty]
    private ElementTheme _theme;

    [ObservableProperty]
    private BackdropKind _backdrop;

    [ObservableProperty]
    private ArchiveFormat _defaultFormat;

    public IReadOnlyList<ElementTheme> Themes { get; } = new[]
    {
        ElementTheme.Default, ElementTheme.Light, ElementTheme.Dark,
    };

    public IReadOnlyList<BackdropKind> Backdrops { get; } = new[]
    {
        BackdropKind.Mica, BackdropKind.Acrylic, BackdropKind.None,
    };

    public IReadOnlyList<ArchiveFormat> Formats { get; } = new[]
    {
        ArchiveFormat.Zip, ArchiveFormat.SevenZip, ArchiveFormat.TarGz, ArchiveFormat.TarBz2, ArchiveFormat.Tar,
    };

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "0.1.0";

    partial void OnThemeChanged(ElementTheme value) => _settings.Theme = value;
    partial void OnBackdropChanged(BackdropKind value) => _settings.Backdrop = value;
    partial void OnDefaultFormatChanged(ArchiveFormat value) => _settings.DefaultFormat = value;
}
