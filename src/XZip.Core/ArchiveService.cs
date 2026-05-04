using XZip.Core.Abstractions;
using XZip.Core.Internal;

namespace XZip.Core;

/// <summary>
/// Top-level facade for archive operations. Dispatches to the right
/// <see cref="IArchiveProvider"/> based on file detection.
/// </summary>
public sealed class ArchiveService
{
    private readonly IReadOnlyList<IArchiveProvider> _providers;

    public ArchiveService(IEnumerable<IArchiveProvider> providers)
    {
        _providers = providers.ToList();
        if (_providers.Count == 0)
            throw new ArgumentException("At least one provider is required.", nameof(providers));
    }

    public IReadOnlyList<IArchiveProvider> Providers => _providers;

    /// <summary>
    /// Returns all archive formats that any registered provider can write.
    /// </summary>
    public IReadOnlyCollection<ArchiveFormat> WritableFormats =>
        _providers.SelectMany(p => p.WritableFormats).Distinct().ToList();

    public ArchiveFormat Probe(string path) => FormatDetector.Detect(path);

    public IArchiveProvider GetProviderFor(string path)
    {
        var provider = _providers.FirstOrDefault(p => p.CanRead(path));
        return provider ?? throw new NotSupportedException($"No provider can read '{path}'.");
    }

    public IArchiveProvider GetProviderFor(ArchiveFormat format)
    {
        var provider = _providers.FirstOrDefault(p => p.WritableFormats.Contains(format));
        return provider ?? throw new NotSupportedException($"No provider supports writing {format}.");
    }

    public Task<ArchiveHandle> OpenAsync(string path, string? password = null, CancellationToken ct = default)
        => GetProviderFor(path).OpenAsync(path, password, ct);

    public IAsyncEnumerable<ArchiveEntry> EnumerateAsync(ArchiveHandle handle, CancellationToken ct = default)
        => handle.Provider.EnumerateAsync(handle, ct);

    public Task<Stream> OpenEntryAsync(ArchiveHandle handle, ArchiveEntry entry, CancellationToken ct = default)
        => handle.Provider.OpenEntryAsync(handle, entry, ct);

    public Task ExtractAsync(
        ArchiveHandle handle,
        string destinationDirectory,
        ExtractOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken ct = default)
        => handle.Provider.ExtractAsync(handle, destinationDirectory, options ?? new ExtractOptions(), progress, ct);

    public Task CreateAsync(
        string outputPath,
        IReadOnlyList<SourceItem> items,
        CreateOptions options,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken ct = default)
        => GetProviderFor(options.Format).CreateAsync(outputPath, items, options, progress, ct);

    /// <summary>
    /// Convenience overload: extract all entries to <paramref name="destination"/> with default options.
    /// </summary>
    public async Task ExtractAllAsync(string archivePath, string destination, string? password = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken ct = default)
    {
        await using var handle = await OpenAsync(archivePath, password, ct).ConfigureAwait(false);
        await ExtractAsync(handle, destination, new ExtractOptions { Password = password }, progress, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Convenience overload: walk a folder and zip everything into <paramref name="outputPath"/>.
    /// </summary>
    public Task CreateFromDirectoryAsync(
        string sourceDirectory,
        string outputPath,
        CreateOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken ct = default)
    {
        var opts = options ?? new CreateOptions();
        var root = Path.GetFullPath(sourceDirectory);
        var items = new List<SourceItem>();

        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, dir);
            items.Add(SourceItem.FromDirectory(dir, rel));
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file);
            items.Add(SourceItem.FromFile(file, rel));
        }

        return CreateAsync(outputPath, items, opts, progress, ct);
    }
}
