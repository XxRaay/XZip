using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using XZip.App.Services;
using XZip.Core;
using XZip.Core.Abstractions;

namespace XZip.App.ViewModels;

public partial class CreateArchiveViewModel : ObservableObject
{
    private readonly ArchiveService _service;
    private readonly ISettingsService _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    private int _itemCount;

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private string? _outputPath;

    [ObservableProperty]
    private ArchiveFormat _format = ArchiveFormat.Zip;

    [ObservableProperty]
    private int _compressionLevel = 5;

    [ObservableProperty]
    private int _maxParallelism = Environment.ProcessorCount;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string? _progressItem;

    [ObservableProperty]
    private string? _progressSpeed;

    [ObservableProperty]
    private string? _progressEta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;
    public bool HasItems => ItemCount > 0;

    public ObservableCollection<string> SourcePaths { get; } = new();

    public IReadOnlyList<ArchiveFormat> AvailableFormats => new[]
    {
        ArchiveFormat.Zip,
        ArchiveFormat.Tar,
        ArchiveFormat.TarGz,
        ArchiveFormat.TarBz2,
        ArchiveFormat.SevenZip,
    };

    public CreateArchiveViewModel(ArchiveService service, ISettingsService settings)
    {
        _service = service;
        _settings = settings;
        Format = settings.DefaultFormat;
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        long size = 0;
        var count = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || SourcePaths.Contains(p)) continue;
            SourcePaths.Add(p);
            try
            {
                if (Directory.Exists(p))
                {
                    foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    {
                        try { size += new FileInfo(f).Length; count++; } catch { }
                    }
                }
                else if (File.Exists(p))
                {
                    size += new FileInfo(p).Length;
                    count++;
                }
            }
            catch { /* best effort */ }
        }
        TotalSize += size;
        ItemCount += count;
    }

    public void Clear()
    {
        SourcePaths.Clear();
        ItemCount = 0;
        TotalSize = 0;
    }

    [RelayCommand(CanExecute = nameof(CanCompress))]
    public async Task CompressAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(OutputPath) || SourcePaths.Count == 0) return;

        IsBusy = true;
        try
        {
            var items = BuildItems();

            var progress = new Progress<ArchiveProgress>(p =>
            {
                ProgressPercentage = p.Percentage * 100;
                ProgressItem = p.CurrentItem;
                ProgressSpeed = $"{ArchiveEntryViewModel.FormatSize((long)p.BytesPerSecond)}/s";
                ProgressEta = p.Eta.TotalSeconds > 0 ? $"~{p.Eta:mm\\:ss}" : "";
            });

            await _service.CreateAsync(OutputPath, items, new CreateOptions
            {
                Format = Format,
                CompressionLevel = CompressionLevel,
                MaxDegreeOfParallelism = MaxParallelism,
            }, progress, ct);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCompress() => HasItems && !IsBusy && !string.IsNullOrEmpty(OutputPath);

    partial void OnIsBusyChanged(bool value) => CompressCommand.NotifyCanExecuteChanged();
    partial void OnItemCountChanged(int value) => CompressCommand.NotifyCanExecuteChanged();
    partial void OnOutputPathChanged(string? value) => CompressCommand.NotifyCanExecuteChanged();

    private IReadOnlyList<SourceItem> BuildItems()
    {
        var list = new List<SourceItem>();
        foreach (var p in SourcePaths)
        {
            if (Directory.Exists(p))
            {
                var root = Path.GetFullPath(p);
                var rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
                list.Add(SourceItem.FromDirectory(root, rootName));
                foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.Combine(rootName, Path.GetRelativePath(root, d));
                    list.Add(SourceItem.FromDirectory(d, rel));
                }
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.Combine(rootName, Path.GetRelativePath(root, f));
                    list.Add(SourceItem.FromFile(f, rel));
                }
            }
            else if (File.Exists(p))
            {
                list.Add(SourceItem.FromFile(p, Path.GetFileName(p)));
            }
        }
        return list;
    }
}
