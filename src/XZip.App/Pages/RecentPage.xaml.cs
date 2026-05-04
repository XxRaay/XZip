using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using XZip.App.Services;
using XZip.App.ViewModels;

namespace XZip.App.Pages;

public sealed partial class RecentPage : Page
{
    public RecentViewModel ViewModel { get; }

    private readonly INavigationService _nav;

    public RecentPage()
    {
        ViewModel = App.Services.GetRequiredService<RecentViewModel>();
        _nav = App.Services.GetRequiredService<INavigationService>();
        InitializeComponent();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    private void Clear_Click(object sender, RoutedEventArgs e) => ViewModel.Clear();

    private void Item_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecentItem item && item.Exists)
        {
            _nav.NavigateTo(typeof(ArchiveExplorerPage), item.Path);
        }
    }
}
