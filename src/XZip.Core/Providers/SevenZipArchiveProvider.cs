using System.Runtime.CompilerServices;

using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

using XZip.Core.Abstractions;
using XZip.Core.Internal;

namespace XZip.Core.Providers;

/// <summary>
/// Read-only 7z provider via SharpCompress.
/// 7z creation is not supported by SharpCompress, so <see cref="CreateAsync"/> throws.
/// </summary>
public sealed class SevenZipArchiveProvider : IArchiveProvider
{
    public IReadOnlyCollection<ArchiveFormat> WritableFormats { get; } = Array.Empty<ArchiveFormat>();

    public bool CanRead(string path) => Probe(path) == ArchiveFormat.SevenZip;

    public ArchiveFormat Probe(string path) =>
        FormatDetector.Detect(path) == ArchiveFormat.SevenZip ? ArchiveFormat.SevenZip : ArchiveFormat.Unknown;

    public Task<ArchiveHandle> OpenAsync(string path, string? password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var opts = new ReaderOptions { Password = password, LeaveStreamOpen = false };
        var archive = SevenZipArchive.Open(path, opts);
        return Task.FromResult(new ArchiveHandle
        {
            Path = path,
            Format = ArchiveFormat.SevenZip,
            Provider = this,
            State = archive,
            Password = password,
        });
    }

    public async IAsyncEnumerable<ArchiveEntry> EnumerateAsync(
        ArchiveHandle handle,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var archive = (SevenZipArchive)handle.State;
        foreach (var e in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Map(e);
            await Task.Yield();
        }
    }

    public Task<Stream> OpenEntryAsync(ArchiveHandle handle, ArchiveEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var e = (SevenZipArchiveEntry)entry.Token;
        return Task.FromResult(e.OpenEntryStream());
    }

    public Task ExtractAsync(
        ArchiveHandle handle,
        string destinationDirectory,
        ExtractOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var archive = (SevenZipArchive)handle.State;
        var entries = archive.Entries.ToList();
        var tracker = new ProgressTracker(progress);
        var totalBytes = entries.Where(e => !e.IsDirectory).Sum(e => Math.Max(e.Size, 0));
        tracker.SetTotal(totalBytes, entries.Count);

        return Task.Run(() =>
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (options.Filter is not null)
                {
                    var mapped = Map(entry);
                    if (!options.Filter(mapped)) continue;
                }

                var safeKey = (entry.Key ?? string.Empty).Replace('\\', '/');
                var destination = PathSafety.ResolveSafeDestination(destinationDirectory, safeKey);

                tracker.BeginItem(safeKey, entry.Size);

                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(destination);
                    tracker.CompleteItem();
                    continue;
                }

                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                if (File.Exists(destination))
                {
                    switch (options.Conflict)
                    {
                        case ConflictPolicy.Skip: tracker.CompleteItem(); continue;
                        case ConflictPolicy.Fail: throw new IOException($"Destination already exists: {destination}");
                        case ConflictPolicy.Rename: destination = PathSafety.GetUniquePath(destination); break;
                        case ConflictPolicy.Overwrite: default: File.Delete(destination); break;
                    }
                }

                using (var inStream = entry.OpenEntryStream())
                using (var outFile = File.Create(destination))
                {
                    var buffer = new byte[81920];
                    int n;
                    while ((n = inStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        outFile.Write(buffer, 0, n);
                        tracker.AddBytes(n);
                    }
                }

                if (options.PreserveTimestamps && entry.LastModifiedTime is { } lwt)
                {
                    try { File.SetLastWriteTime(destination, lwt); } catch { }
                }

                tracker.CompleteItem();
            }

            tracker.Finish();
        }, cancellationToken);
    }

    public Task CreateAsync(
        string outputPath,
        IReadOnlyList<SourceItem> items,
        CreateOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("7z creation is not supported by the bundled engine. Use ZIP or TAR.GZ instead.");

    private static ArchiveEntry Map(SevenZipArchiveEntry e) => new()
    {
        Key = e.Key ?? string.Empty,
        FullPath = e.Key ?? string.Empty,
        Name = Path.GetFileName(e.Key ?? string.Empty),
        IsDirectory = e.IsDirectory,
        IsEncrypted = e.IsEncrypted,
        Size = e.Size,
        CompressedSize = e.CompressedSize,
        LastModified = e.LastModifiedTime,
        Crc32 = (uint?)e.Crc,
        Token = e,
    };
}
