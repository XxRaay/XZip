using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources;

using XZip.App.Services;
using XZip.App.ViewModels;
using XZip.Core.Abstractions;

namespace XZip.App.Pages;

public sealed partial class CreateArchivePage : Page
{
    public CreateArchiveViewModel ViewModel { get; }

    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;
    private readonly ResourceLoader _resources = ResourceLoader.GetForViewIndependentUse();

    public CreateArchivePage()
    {
        ViewModel = App.Services.GetRequiredService<CreateArchiveViewModel>();
        _picker = App.Services.GetRequiredService<IFilePickerService>();
        _dialogs = App.Services.GetRequiredService<IDialogService>();
        InitializeComponent();
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        ViewModel.AddPaths(items.Select(i => i.Path));
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = T("Create_DragAddCaption");
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var files = await _picker.PickFilesToAddAsync(((App)Application.Current).MainWindow);
        if (files.Count > 0) ViewModel.AddPaths(files);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = await _picker.PickFolderAsync(((App)Application.Current).MainWindow);
        if (!string.IsNullOrEmpty(folder)) ViewModel.AddPaths(new[] { folder });
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => ViewModel.Clear();

    private async void ChooseOutput_Click(object sender, RoutedEventArgs e)
    {
        var ext = ViewModel.Format.DefaultExtension();
        var label = ViewModel.Format.DisplayName();
        var defaultName = "archive" + ext;
        var path = await _picker.PickArchiveSaveAsync(((App)Application.Current).MainWindow, defaultName, ext, label);
        if (!string.IsNullOrEmpty(path)) ViewModel.OutputPath = path;
    }

    private string T(string key)
    {
        var value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
