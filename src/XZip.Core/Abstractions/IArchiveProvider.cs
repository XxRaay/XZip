namespace XZip.Core.Abstractions;

/// <summary>
/// Format-agnostic abstraction for reading and writing archives.
/// One implementation per format family (ZIP, 7z, TAR/GZ/BZ2, ...).
/// </summary>
public interface IArchiveProvider
{
    /// <summary>
    /// Formats this provider can produce. Used by <c>ArchiveService</c> to pick a writer.
    /// </summary>
    IReadOnlyCollection<ArchiveFormat> WritableFormats { get; }

    /// <summary>
    /// Returns true if the provider thinks it can read the given path
    /// (by extension and/or by magic bytes).
    /// </summary>
    bool CanRead(string path);

    /// <summary>
    /// Detect the concrete format of a file. Returns <see cref="ArchiveFormat.Unknown"/> if unsure.
    /// </summary>
    ArchiveFormat Probe(string path);

    /// <summary>
    /// Open an archive for reading.
    /// </summary>
    Task<ArchiveHandle> OpenAsync(string path, string? password, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerate the archive's entries lazily.
    /// </summary>
    IAsyncEnumerable<ArchiveEntry> EnumerateAsync(ArchiveHandle handle, CancellationToken cancellationToken);

    /// <summary>
    /// Open a stream over the contents of one entry. The returned stream is only valid
    /// while <paramref name="handle"/> is alive.
    /// </summary>
    Task<Stream> OpenEntryAsync(ArchiveHandle handle, ArchiveEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Extract entries to <paramref name="destinationDirectory"/>.
    /// </summary>
    Task ExtractAsync(
        ArchiveHandle handle,
        string destinationDirectory,
        ExtractOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a new archive at <paramref name="outputPath"/> from the given source items.
    /// </summary>
    Task CreateAsync(
        string outputPath,
        IReadOnlyList<SourceItem> items,
        CreateOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken);
}
