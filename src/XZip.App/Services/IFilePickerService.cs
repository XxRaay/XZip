using Microsoft.UI.Xaml;

using Windows.Storage.Pickers;

using WinRT.Interop;

namespace XZip.App.Services;

public interface IFilePickerService
{
    Task<string?> PickArchiveToOpenAsync(Window window);
    Task<string?> PickAnyFileToAddAsync(Window window);
    Task<IReadOnlyList<string>> PickFilesToAddAsync(Window window);
    Task<string?> PickFolderAsync(Window window);
    Task<string?> PickArchiveSaveAsync(Window window, string defaultName, string extension, string formatLabel);
}

public sealed class FilePickerService : IFilePickerService
{
    public async Task<string?> PickArchiveToOpenAsync(Window window)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            ViewMode = PickerViewMode.List,
        };
        foreach (var ext in new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz" })
            picker.FileTypeFilter.Add(ext);

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickAnyFileToAddAsync(Window window)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<IReadOnlyList<string>> PickFilesToAddAsync(Window window)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(f => f.Path).ToList();
    }

    public async Task<string?> PickFolderAsync(Window window)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string?> PickArchiveSaveAsync(Window window, string defaultName, string extension, string formatLabel)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            SuggestedFileName = defaultName,
        };
        picker.FileTypeChoices.Add(formatLabel, new List<string> { extension });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
