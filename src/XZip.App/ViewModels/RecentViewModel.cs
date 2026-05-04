using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using XZip.App.Services;

namespace XZip.App.ViewModels;

public partial class RecentViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public ObservableCollection<RecentItem> Items { get; } = new();

    public RecentViewModel(ISettingsService settings)
    {
        _settings = settings;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        Items.Clear();
        foreach (var path in _settings.RecentArchives)
        {
            var exists = File.Exists(path);
            Items.Add(new RecentItem
            {
                Path = path,
                Name = Path.GetFileName(path),
                Folder = Path.GetDirectoryName(path) ?? "",
                Exists = exists,
                Size = exists ? new FileInfo(path).Length : 0,
            });
        }
    }

    [RelayCommand]
    public void Clear()
    {
        _settings.ClearRecent();
        Refresh();
    }
}

public sealed class RecentItem
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Folder { get; init; }
    public required bool Exists { get; init; }
    public long Size { get; init; }
    public string SizeText => Size > 0 ? ArchiveEntryViewModel.FormatSize(Size) : "—";
}
