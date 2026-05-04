using CommunityToolkit.Mvvm.ComponentModel;

using XZip.App.Services;

namespace XZip.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _nav;

    [ObservableProperty]
    private string _windowTitle = "XZip";

    [ObservableProperty]
    private string _selectedTag = "open";

    public INavigationService Navigation => _nav;

    public MainViewModel(INavigationService nav)
    {
        _nav = nav;
    }
}
