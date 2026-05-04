using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.Resources;
using Windows.System;

using WinUIEx;

using XZip.App.Pages;
using XZip.App.Services;
using XZip.App.ViewModels;

namespace XZip.App;

public sealed partial class MainWindow : WindowEx
{
    private const string GitHubRepoUrl = "https://github.com/XxRaay/XZip";
    private readonly ResourceLoader _resources = ResourceLoader.GetForViewIndependentUse();
    private readonly INavigationService _nav;
    private readonly ISettingsService _settings;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel vm, INavigationService nav, ISettingsService settings)
    {
        ViewModel = vm;
        _nav = nav;
        _settings = settings;

        InitializeComponent();

        Title = T("AppTitle");
        this.SetWindowSize(1200, 760);
        this.CenterOnScreen();
        TrySetWindowIcon();

        ConfigureTitleBar();
        ApplyBackdrop(_settings.Backdrop);
        ApplyTheme(_settings.Theme);

        _nav.Frame = ContentFrame;
        _nav.Register("open", typeof(ArchiveExplorerPage));
        _nav.Register("create", typeof(CreateArchivePage));
        _nav.Register("recent", typeof(RecentPage));
        _nav.Register("settings", typeof(SettingsPage));

        Activated += OnActivated;
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "favicon.ico"),
                Path.Combine(baseDir, "XZip.ico"),
                Path.Combine(baseDir, "Assets", "AppIcon.ico"),
            };

            var iconPath = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrEmpty(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Best effort: on some unpackaged/debug scenarios icon loading may fail.
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (Nav.SelectedItem is null && Nav.MenuItems.Count > 0)
        {
            Nav.SelectedItem = Nav.MenuItems[0];
        }
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (AppWindow.TitleBar is { } tb)
        {
            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    private void ApplyBackdrop(BackdropKind kind)
    {
        SystemBackdrop = kind switch
        {
            BackdropKind.Mica when MicaController.IsSupported() => new MicaBackdrop { Kind = MicaKind.Base },
            BackdropKind.Acrylic when DesktopAcrylicController.IsSupported() => new DesktopAcrylicBackdrop(),
            _ => null,
        };
    }

    private void ApplyTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement fe) fe.RequestedTheme = theme;
    }

    public void ApplyThemeFromSettings() => ApplyTheme(_settings.Theme);

    public void ApplyBackdropFromSettings() => ApplyBackdrop(_settings.Backdrop);

    private async void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            if (tag == "github")
            {
                var previousTag = ViewModel.SelectedTag;
                await Launcher.LaunchUriAsync(new Uri(GitHubRepoUrl));

                var previousItem = FindItemByTag(previousTag);
                if (previousItem is not null)
                {
                    sender.SelectedItem = previousItem;
                }

                return;
            }

            ViewModel.SelectedTag = tag;
            _nav.NavigateToTag(tag);
        }
    }

    private NavigationViewItem? FindItemByTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        static NavigationViewItem? MatchByTag(IList<object> items, string tagToFind)
        {
            return items
                .OfType<NavigationViewItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, tagToFind, StringComparison.Ordinal));
        }

        return MatchByTag(Nav.MenuItems, tag) ?? MatchByTag(Nav.FooterMenuItems, tag);
    }

    private string T(string key)
    {
        var value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
