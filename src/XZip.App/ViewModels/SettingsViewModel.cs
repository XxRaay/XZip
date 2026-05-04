using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.Resources;

using XZip.App.Services;
using XZip.Core.Abstractions;

namespace XZip.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public sealed record LanguageOption(string Value, string Label);

    private readonly ISettingsService _settings;
    private readonly ResourceLoader _resources = ResourceLoader.GetForViewIndependentUse();

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        _theme = settings.Theme;
        _backdrop = settings.Backdrop;
        _defaultFormat = settings.DefaultFormat;
        _language = NormalizeLanguage(settings.Language);
    }

    [ObservableProperty]
    private ElementTheme _theme;

    [ObservableProperty]
    private BackdropKind _backdrop;

    [ObservableProperty]
    private ArchiveFormat _defaultFormat;

    [ObservableProperty]
    private string _language;

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

    public IReadOnlyList<LanguageOption> Languages => new[]
    {
        new LanguageOption("system", T("Settings_Language_System")),
        new LanguageOption("ru", T("Settings_Language_Russian")),
        new LanguageOption("en", T("Settings_Language_English")),
    };

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "0.1.0";

    partial void OnThemeChanged(ElementTheme value) => _settings.Theme = value;
    partial void OnBackdropChanged(BackdropKind value) => _settings.Backdrop = value;
    partial void OnDefaultFormatChanged(ArchiveFormat value) => _settings.DefaultFormat = value;
    partial void OnLanguageChanged(string value) => _settings.Language = NormalizeLanguage(value);

    private static string NormalizeLanguage(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "ru" => "ru",
            "en" => "en",
            _ => "system",
        };
    }

    private string T(string key)
    {
        var value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
