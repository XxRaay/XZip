using System.Collections.ObjectModel;
using System.Security.Cryptography;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Windows.ApplicationModel.Resources;

using XZip.App.Services;
using XZip.Core;
using XZip.Core.Abstractions;

namespace XZip.App.ViewModels;

public partial class ArchiveExplorerViewModel : ObservableObject, IDisposable
{
    private enum PendingActionKind
    {
        None,
        ExtractAll,
        ExtractSelected,
    }

    private readonly ArchiveService _service;
    private readonly ISettingsService _settings;
    private readonly ResourceLoader _resources = ResourceLoader.GetForViewIndependentUse();
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

    [ObservableProperty]
    private bool _isPasswordPromptVisible;

    [ObservableProperty]
    private string _passwordInput = string.Empty;

    [ObservableProperty]
    private string? _passwordPromptMessage;

    [ObservableProperty]
    private int _selectedCount;

    private bool _archiveContainsEncryptedEntries;
    private string? _archivePassword;
    private PendingActionKind _pendingAction = PendingActionKind.None;
    private string? _pendingDestination;
    private IReadOnlyList<ArchiveEntryViewModel> _pendingSelectedEntries = Array.Empty<ArchiveEntryViewModel>();

    public ObservableCollection<ArchiveEntryViewModel> VisibleEntries { get; } = new();
    public ObservableCollection<string> Breadcrumbs { get; } = new();

    public bool IsEmpty => _handle is null;
    public bool HasSelection => SelectedCount > 0;

    public ArchiveExplorerViewModel(ArchiveService service, ISettingsService settings)
    {
        _service = service;
        _settings = settings;
    }

    [RelayCommand]
    public async Task OpenAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        HidePasswordPrompt();
        IsBusy = true;
        StatusText = T("Status_OpeningArchive");

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
            _archiveContainsEncryptedEntries = list.Any(e => e.IsEncrypted);
            CurrentFolder = "";
            UpdateBreadcrumbs();
            ApplyFilter();
            _settings.AddRecent(path);
            StatusText = $"{list.Count} элементов · {ArchiveEntryViewModel.FormatSize(new FileInfo(path).Length)}";
        }
        catch (Exception ex)
        {
            if (IsPasswordFailure(ex))
            {
                await CloseAsync();
                IsPasswordPromptVisible = true;
                PasswordPromptMessage = T("PasswordPrompt_OpenFailed");
                StatusText = T("Status_PasswordRequired");
            }
            else
            {
                StatusText = $"{T("Status_ErrorPrefix")}: {ex.Message}";
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public async Task<bool> ApplyPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(ArchivePath)) return false;
        if (string.IsNullOrWhiteSpace(PasswordInput))
        {
            PasswordPromptMessage = T("PasswordPrompt_EnterPassword");
            return false;
        }

        IsBusy = true;
        StatusText = T("Status_VerifyingPassword");

        try
        {
            var path = ArchivePath;
            var password = PasswordInput;
            var pendingAction = _pendingAction;
            var pendingDestination = _pendingDestination;
            var pendingSelectedEntries = _pendingSelectedEntries;
            await CloseAsync();
            _handle = await _service.OpenAsync(path, password);
            ArchivePath = path;
            ArchiveName = Path.GetFileName(path);

            var list = new List<ArchiveEntryViewModel>();
            await foreach (var e in _service.EnumerateAsync(_handle))
            {
                list.Add(new ArchiveEntryViewModel(e));
            }

            _allEntries = list;
            _archiveContainsEncryptedEntries = list.Any(e => e.IsEncrypted);
            if (_archiveContainsEncryptedEntries)
            {
                var probe = list.FirstOrDefault(e => e.IsEncrypted && !e.IsDirectory);
                if (probe is not null)
                {
                    await using var stream = await _service.OpenEntryAsync(_handle, probe.Entry, CancellationToken.None);
                    _ = stream.ReadByte();
                }
            }

            _archivePassword = password;
            CurrentFolder = "";
            UpdateBreadcrumbs();
            ApplyFilter();
            _settings.AddRecent(path);
            StatusText = $"{list.Count} элементов · {ArchiveEntryViewModel.FormatSize(new FileInfo(path).Length)}";
            _pendingAction = pendingAction;
            _pendingDestination = pendingDestination;
            _pendingSelectedEntries = pendingSelectedEntries;
            HidePasswordPrompt();
            await ResumePendingActionAsync();
            return true;
        }
        catch (Exception ex)
        {
            if (IsPasswordFailure(ex))
            {
                PasswordPromptMessage = T("PasswordPrompt_WrongPassword");
                StatusText = T("Status_WrongPassword");
                _archivePassword = null;
                IsPasswordPromptVisible = true;
            }
            else
            {
                HidePasswordPrompt();
                StatusText = $"{T("Status_ErrorPrefix")}: {ex.Message}";
            }

            return false;
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

    public async Task<bool> ExtractAllAsync(string destination, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        if (_handle is null) return false;
        IsBusy = true;
        StatusText = T("Status_Extracting");
        try
        {
            if (!await EnsurePasswordReadyAsync(ct))
            {
                _pendingAction = PendingActionKind.ExtractAll;
                _pendingDestination = destination;
                _pendingSelectedEntries = Array.Empty<ArchiveEntryViewModel>();
                StatusText = T("Status_PasswordRequired");
                return false;
            }

            await _service.ExtractAsync(_handle, destination, new ExtractOptions
            {
                Password = _archivePassword,
            }, progress, ct);
            StatusText = T("Status_Done");
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ExtractSelectedAsync(
        string destination,
        IReadOnlyList<ArchiveEntryViewModel> selectedEntries,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        if (_handle is null || selectedEntries.Count == 0) return false;
        IsBusy = true;
        StatusText = T("Status_ExtractingSelected");
        try
        {
            if (!await EnsurePasswordReadyAsync(ct))
            {
                _pendingAction = PendingActionKind.ExtractSelected;
                _pendingDestination = destination;
                _pendingSelectedEntries = selectedEntries.ToList();
                StatusText = T("Status_PasswordRequired");
                return false;
            }

            var selected = selectedEntries
                .Select(v => v.Path.Replace('\\', '/').TrimStart('/'))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await _service.ExtractAsync(_handle, destination, new ExtractOptions
            {
                Filter = entry =>
                {
                    var key = entry.FullPath.Replace('\\', '/').TrimStart('/');
                    foreach (var item in selected)
                    {
                        if (item.EndsWith('/'))
                        {
                            if (key.StartsWith(item, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                        else if (string.Equals(key, item, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    return false;
                },
                Password = _archivePassword,
            }, progress, ct);

            StatusText = T("Status_Done");
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<Stream?> OpenEntryStreamAsync(ArchiveEntryViewModel vm, CancellationToken ct)
    {
        if (_handle is null) return null;
        if (!await EnsurePasswordReadyAsync(ct))
        {
            StatusText = T("Status_PasswordRequired");
            return null;
        }

        return await _service.OpenEntryAsync(_handle, vm.Entry, ct);
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();
    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(HasSelection));

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
        _archiveContainsEncryptedEntries = false;
        _archivePassword = null;
        _pendingAction = PendingActionKind.None;
        _pendingDestination = null;
        _pendingSelectedEntries = Array.Empty<ArchiveEntryViewModel>();
        VisibleEntries.Clear();
        Breadcrumbs.Clear();
        SelectedCount = 0;
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose()
    {
        _ = CloseAsync();
    }

    public void HidePasswordPrompt()
    {
        IsPasswordPromptVisible = false;
        PasswordPromptMessage = null;
        PasswordInput = string.Empty;
    }

    private Task<bool> EnsurePasswordReadyAsync(CancellationToken ct)
    {
        if (!_archiveContainsEncryptedEntries)
        {
            return Task.FromResult(true);
        }

        if (!string.IsNullOrWhiteSpace(_archivePassword))
        {
            return Task.FromResult(true);
        }

        IsPasswordPromptVisible = true;
        PasswordPromptMessage = T("PasswordPrompt_ActionNeedsPassword");
        return Task.FromResult(false);
    }

    private async Task ResumePendingActionAsync()
    {
        var action = _pendingAction;
        var destination = _pendingDestination;
        var selected = _pendingSelectedEntries;

        _pendingAction = PendingActionKind.None;
        _pendingDestination = null;
        _pendingSelectedEntries = Array.Empty<ArchiveEntryViewModel>();

        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        switch (action)
        {
            case PendingActionKind.ExtractAll:
                await ExtractAllAsync(destination, null, CancellationToken.None);
                break;
            case PendingActionKind.ExtractSelected:
                await ExtractSelectedAsync(destination, selected, null, CancellationToken.None);
                break;
        }
    }

    private string T(string key)
    {
        var value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    private static bool IsPasswordFailure(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is CryptographicException)
            {
                return true;
            }

            var msg = current.Message;
            if (!string.IsNullOrWhiteSpace(msg)
                && (msg.Contains("password", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("encrypted", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("crc", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
