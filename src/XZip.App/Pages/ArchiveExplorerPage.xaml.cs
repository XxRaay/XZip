using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

using XZip.App.Services;
using XZip.App.ViewModels;
using XZip.Core.Abstractions;

namespace XZip.App.Pages;

public sealed partial class ArchiveExplorerPage : Page
{
    public ArchiveExplorerViewModel ViewModel { get; }

    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;

    public ArchiveExplorerPage()
    {
        ViewModel = App.Services.GetRequiredService<ArchiveExplorerViewModel>();
        _picker = App.Services.GetRequiredService<IFilePickerService>();
        _dialogs = App.Services.GetRequiredService<IDialogService>();

        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string path && File.Exists(path))
        {
            _ = ViewModel.OpenAsync(path);
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var path = await _picker.PickArchiveToOpenAsync(((App)Application.Current).MainWindow);
        if (!string.IsNullOrEmpty(path))
        {
            await ViewModel.OpenAsync(path);
        }
    }

    private async void ExtractAll_Click(object sender, RoutedEventArgs e)
    {
        var dest = await _picker.PickFolderAsync(((App)Application.Current).MainWindow);
        if (string.IsNullOrEmpty(dest)) return;

        try
        {
            await ViewModel.ExtractAllAsync(dest, null, CancellationToken.None);
            await _dialogs.ShowMessageAsync(
                "Распаковка завершена",
                $"Архив успешно извлечён в:\n{dest}",
                XamlRoot);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Ошибка распаковки", ex.Message, XamlRoot);
        }
    }

    private async void GoUp_Click(object sender, RoutedEventArgs e) => await ViewModel.GoUpAsync();

    private async void Entries_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is ArchiveEntryViewModel vm && vm.IsDirectory)
        {
            await ViewModel.NavigateToAsync(vm.Path);
        }
    }

    private async void Breadcrumb_Clicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Index == 0)
        {
            await ViewModel.NavigateToAsync(string.Empty);
            return;
        }

        var parts = ViewModel.CurrentFolder
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Take(args.Index);
        await ViewModel.NavigateToAsync(string.Join('/', parts));
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var first = items.FirstOrDefault(i => i is StorageFile);
        if (first is StorageFile file)
        {
            await ViewModel.OpenAsync(file.Path);
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = "Открыть архив";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }
    }

    /// <summary>
    /// Drag selected entries out of the app. We extract them lazily into temp on demand.
    /// </summary>
    private async void Entries_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var entries = e.Items.OfType<ArchiveEntryViewModel>()
            .Where(v => !v.IsDirectory).ToList();
        if (entries.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        var deferral = e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.Properties.Title = "Файлы из XZip";

        var temp = Path.Combine(Path.GetTempPath(), "XZip", "drag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        var files = new List<StorageFile>();
        foreach (var entry in entries)
        {
            try
            {
                var safeName = Path.GetFileName(entry.Name);
                var destPath = Path.Combine(temp, safeName);

                await using var src = await ViewModel.OpenEntryStreamAsync(entry, CancellationToken.None);
                if (src is null) continue;
                await using (var dst = File.Create(destPath))
                {
                    await src.CopyToAsync(dst);
                }
                var sf = await StorageFile.GetFileFromPathAsync(destPath);
                files.Add(sf);
            }
            catch
            {
                // Skip files that fail to extract.
            }
        }

        if (files.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        e.Data.SetStorageItems(files);
    }
}
