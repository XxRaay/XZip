using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using XZip.App.ViewModels;

namespace XZip.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Application.Current is App app && app.MainWindow is MainWindow mw)
        {
            mw.ApplyThemeFromSettings();
        }
    }

    private void Backdrop_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Application.Current is App app && app.MainWindow is MainWindow mw)
        {
            mw.ApplyBackdropFromSettings();
        }
    }
}
