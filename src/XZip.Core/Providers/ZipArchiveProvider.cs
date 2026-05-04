using System.Runtime.CompilerServices;

using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

using XZip.Core.Abstractions;
using XZip.Core.Internal;

namespace XZip.Core.Providers;

/// <summary>
/// ZIP provider built on SharpCompress. Supports parallel per-entry deflate compression
/// when creating archives, and stream extraction for reading.
/// </summary>
public sealed class ZipArchiveProvider : IArchiveProvider
{
    public IReadOnlyCollection<ArchiveFormat> WritableFormats { get; } = new[] { ArchiveFormat.Zip };

    public bool CanRead(string path) => Probe(path) == ArchiveFormat.Zip;

    public ArchiveFormat Probe(string path)
    {
        var f = FormatDetector.Detect(path);
        return f == ArchiveFormat.Zip ? ArchiveFormat.Zip : ArchiveFormat.Unknown;
    }

    public Task<ArchiveHandle> OpenAsync(string path, string? password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new ReaderOptions { Password = password, LeaveStreamOpen = false };
        var archive = ZipArchive.Open(path, options);
        return Task.FromResult(new ArchiveHandle
        {
            Path = path,
            Format = ArchiveFormat.Zip,
            Provider = this,
            State = archive,
            Password = password,
        });
    }

    public async IAsyncEnumerable<ArchiveEntry> EnumerateAsync(
        ArchiveHandle handle,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var archive = (ZipArchive)handle.State;
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
        var zipEntry = (ZipArchiveEntry)entry.Token;
        return Task.FromResult(zipEntry.OpenEntryStream());
    }

    public Task ExtractAsync(
        ArchiveHandle handle,
        string destinationDirectory,
        ExtractOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var archive = (ZipArchive)handle.State;
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
                        case ConflictPolicy.Skip:
                            tracker.CompleteItem();
                            continue;
                        case ConflictPolicy.Fail:
                            throw new IOException($"Destination already exists: {destination}");
                        case ConflictPolicy.Rename:
                            destination = PathSafety.GetUniquePath(destination);
                            break;
                        case ConflictPolicy.Overwrite:
                        default:
                            File.Delete(destination);
                            break;
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
                    try { File.SetLastWriteTime(destination, lwt); } catch { /* best effort */ }
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
    {
        if (options.MaxDegreeOfParallelism > 1 && items.Count(i => !i.IsDirectory) > 1)
        {
            var tracker = new ProgressTracker(progress);
            tracker.SetTotal(items.Where(i => !i.IsDirectory).Sum(i => i.Size), items.Count);
            return Task.Run(() => ParallelZipPipeline.RunAsync(outputPath, items, options, tracker, cancellationToken), cancellationToken);
        }
        return CreateSequentialAsync(outputPath, items, options, progress, cancellationToken);
    }

    private static Task CreateSequentialAsync(
        string outputPath,
        IReadOnlyList<SourceItem> items,
        CreateOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tracker = new ProgressTracker(progress);
        tracker.SetTotal(items.Where(i => !i.IsDirectory).Sum(i => i.Size), items.Count);

        return Task.Run(() =>
        {
            using var fs = File.Create(outputPath);
            var writerOpts = new ZipWriterOptions(CompressionType.Deflate)
            {
                DeflateCompressionLevel = MapLevel(options.CompressionLevel),
                LeaveStreamOpen = false,
            };
            using var writer = new ZipWriter(fs, writerOpts);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tracker.BeginItem(item.EntryPath, item.Size);

                if (item.IsDirectory)
                {
                    tracker.CompleteItem();
                    continue;
                }

                using var src = File.OpenRead(item.AbsolutePath);
                writer.Write(item.EntryPath, src, item.LastModified);
                tracker.AddBytes(item.Size);
                tracker.CompleteItem();
            }

            tracker.Finish();
        }, cancellationToken);
    }

    /// <summary>
    /// Parallel ZIP creation: each file is deflated into a memory buffer in parallel,
    /// then written sequentially into the output archive. Preserves entry order.
    /// </summary>
    internal static Task CreateParallelAsync(
        string outputPath,
        IReadOnlyList<SourceItem> items,
        CreateOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tracker = new ProgressTracker(progress);
        tracker.SetTotal(items.Where(i => !i.IsDirectory).Sum(i => i.Size), items.Count);

        return Task.Run(async () =>
        {
            using var fs = File.Create(outputPath);
            var writerOpts = new ZipWriterOptions(CompressionType.Deflate)
            {
                DeflateCompressionLevel = MapLevel(options.CompressionLevel),
                LeaveStreamOpen = false,
            };
            using var writer = new ZipWriter(fs, writerOpts);

            var dop = Math.Max(1, options.MaxDegreeOfParallelism);

            using var semaphore = new SemaphoreSlim(dop);
            var tasks = new List<Task<(SourceItem item, MemoryStream? buffer)>>(items.Count);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.IsDirectory)
                {
                    tasks.Add(Task.FromResult<(SourceItem, MemoryStream?)>((item, null)));
                    continue;
                }

                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var ms = new MemoryStream(checked((int)Math.Min(item.Size, int.MaxValue / 2)));
                        using (var src = File.OpenRead(item.AbsolutePath))
                        {
                            src.CopyTo(ms);
                        }
                        ms.Position = 0;
                        return (item, (MemoryStream?)ms);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            foreach (var task in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (item, buffer) = await task.ConfigureAwait(false);

                tracker.BeginItem(item.EntryPath, item.Size);

                if (buffer is null)
                {
                    tracker.CompleteItem();
                    continue;
                }

                using (buffer)
                {
                    writer.Write(item.EntryPath, buffer, item.LastModified);
                }

                tracker.AddBytes(item.Size);
                tracker.CompleteItem();
            }

            tracker.Finish();
        }, cancellationToken);
    }

    private static CompressionLevel MapLevel(int level) => level switch
    {
        <= 0 => CompressionLevel.None,
        1 => CompressionLevel.BestSpeed,
        2 or 3 => CompressionLevel.Level3,
        4 => CompressionLevel.Level4,
        5 => CompressionLevel.Level5,
        6 => CompressionLevel.Level6,
        7 => CompressionLevel.Level7,
        8 => CompressionLevel.Level8,
        _ => CompressionLevel.BestCompression,
    };

    private static ArchiveEntry Map(ZipArchiveEntry e) => new()
    {
        Key = e.Key ?? string.Empty,
        FullPath = e.Key ?? string.Empty,
        Name = Path.GetFileName(e.Key ?? string.Empty),
        IsDirectory = e.IsDirectory,
        IsEncrypted = e.IsEncrypted,
        Size = e.Size,
        CompressedSize = e.CompressedSize,
        LastModified = e.LastModifiedTime,
        Crc32 = (uint)e.Crc,
        Token = e,
    };
}
