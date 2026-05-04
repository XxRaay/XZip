using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using XZip.App.Services;
using XZip.Core;
using XZip.Core.Abstractions;

namespace XZip.App.ViewModels;

public partial class ArchiveExplorerViewModel : ObservableObject, IDisposable
{
    private readonly ArchiveService _service;
    private readonly ISettingsService _settings;
    private ArchiveHandle? _handle;
    private List<ArchiveEntryViewModel> _allEntries = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _archivePath;

    [ObservableProperty]
    private string? _archiveName;

    [ObservableProperty]
    private string _currentFolder = "";

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _searchText;

    public ObservableCollection<ArchiveEntryViewModel> VisibleEntries { get; } = new();
    public ObservableCollection<string> Breadcrumbs { get; } = new();

    public bool IsEmpty => _handle is null;

    public ArchiveExplorerViewModel(ArchiveService service, ISettingsService settings)
    {
        _service = service;
        _settings = settings;
    }

    [RelayCommand]
    public async Task OpenAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        IsBusy = true;
        StatusText = "Открытие архива…";

        try
        {
            await CloseAsync();

            _handle = await _service.OpenAsync(path);
            ArchivePath = path;
            ArchiveName = Path.GetFileName(path);

            var list = new List<ArchiveEntryViewModel>();
            await foreach (var e in _service.EnumerateAsync(_handle))
            {
                list.Add(new ArchiveEntryViewModel(e));
            }

            _allEntries = list;
            CurrentFolder = "";
            UpdateBreadcrumbs();
            ApplyFilter();
            _settings.AddRecent(path);
            StatusText = $"{list.Count} элементов · {ArchiveEntryViewModel.FormatSize(new FileInfo(path).Length)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    public Task NavigateToAsync(string folder)
    {
        CurrentFolder = (folder ?? string.Empty).Replace('\\', '/').TrimStart('/');
        UpdateBreadcrumbs();
        ApplyFilter();
        return Task.CompletedTask;
    }

    [RelayCommand]
    public Task GoUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentFolder)) return Task.CompletedTask;
        var idx = CurrentFolder.TrimEnd('/').LastIndexOf('/');
        CurrentFolder = idx > 0 ? CurrentFolder[..idx] : "";
        UpdateBreadcrumbs();
        ApplyFilter();
        return Task.CompletedTask;
    }

    public async Task ExtractAllAsync(string destination, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        if (_handle is null) return;
        IsBusy = true;
        StatusText = "Распаковка…";
        try
        {
            await _service.ExtractAsync(_handle, destination, new ExtractOptions(), progress, ct);
            StatusText = "Готово";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<Stream?> OpenEntryStreamAsync(ArchiveEntryViewModel vm, CancellationToken ct)
    {
        if (_handle is null) return null;
        return await _service.OpenEntryAsync(_handle, vm.Entry, ct);
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        VisibleEntries.Clear();
        var prefix = string.IsNullOrEmpty(CurrentFolder) ? string.Empty : CurrentFolder.TrimEnd('/') + "/";
        var search = SearchText?.Trim();

        // Add directory entries that are direct children of CurrentFolder
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _allEntries)
        {
            var path = e.Path.Replace('\\', '/').TrimStart('/');
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = path[prefix.Length..];
            if (string.IsNullOrEmpty(rel)) continue;

            var slash = rel.IndexOf('/');
            if (slash >= 0)
            {
                var dirName = rel[..slash];
                if (!string.IsNullOrEmpty(search) &&
                    !dirName.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seenDirs.Add(dirName)) continue;

                var dummy = new ArchiveEntry
                {
                    Key = prefix + dirName + "/",
                    FullPath = prefix + dirName + "/",
                    Name = dirName,
                    IsDirectory = true,
                    IsEncrypted = false,
                    Token = new object(),
                };
                VisibleEntries.Add(new ArchiveEntryViewModel(dummy));
            }
            else if (!e.IsDirectory)
            {
                if (!string.IsNullOrEmpty(search) &&
                    !rel.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                VisibleEntries.Add(e);
            }
        }
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(ArchiveName ?? "");
        if (string.IsNullOrEmpty(CurrentFolder)) return;
        foreach (var p in CurrentFolder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            Breadcrumbs.Add(p);
        }
    }

    public async Task CloseAsync()
    {
        if (_handle is not null)
        {
            await _handle.DisposeAsync();
            _handle = null;
        }
        ArchivePath = null;
        ArchiveName = null;
        _allEntries.Clear();
        VisibleEntries.Clear();
        Breadcrumbs.Clear();
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose()
    {
        _ = CloseAsync();
    }
}
