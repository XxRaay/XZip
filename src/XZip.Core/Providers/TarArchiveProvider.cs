using System.Runtime.CompilerServices;

using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;

using XZip.Core.Abstractions;
using XZip.Core.Internal;

namespace XZip.Core.Providers;

/// <summary>
/// TAR provider. Supports plain TAR, TAR.GZ and TAR.BZ2 for both reading and writing.
/// </summary>
public sealed class TarArchiveProvider : IArchiveProvider
{
    public IReadOnlyCollection<ArchiveFormat> WritableFormats { get; } = new[]
    {
        ArchiveFormat.Tar,
        ArchiveFormat.TarGz,
        ArchiveFormat.TarBz2,
    };

    public bool CanRead(string path)
    {
        var f = Probe(path);
        return f is ArchiveFormat.Tar or ArchiveFormat.TarGz or ArchiveFormat.TarBz2;
    }

    public ArchiveFormat Probe(string path)
    {
        var f = FormatDetector.Detect(path);
        return f switch
        {
            ArchiveFormat.Tar or ArchiveFormat.TarGz or ArchiveFormat.TarBz2 => f,
            _ => ArchiveFormat.Unknown,
        };
    }

    public async Task<ArchiveHandle> OpenAsync(string path, string? password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var format = Probe(path);
        if (format == ArchiveFormat.Unknown)
            throw new InvalidDataException($"Not a TAR-family archive: {path}");

        TarArchive archive;
        if (format == ArchiveFormat.Tar)
        {
            archive = TarArchive.Open(path, new ReaderOptions { LeaveStreamOpen = false });
        }
        else
        {
            // tar.gz / tar.bz2 — decompress to a memory stream first; SharpCompress' TarArchive
            // requires a seekable stream of plain TAR.
            await using var fs = File.OpenRead(path);
            await using Stream decoded = format switch
            {
                ArchiveFormat.TarGz => new GZipStream(fs, CompressionMode.Decompress),
                ArchiveFormat.TarBz2 => new BZip2Stream(fs, CompressionMode.Decompress, decompressConcatenated: false),
                _ => fs,
            };
            var ms = new MemoryStream();
            await decoded.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            ms.Position = 0;
            archive = TarArchive.Open(ms, new ReaderOptions { LeaveStreamOpen = false });
        }

        return new ArchiveHandle
        {
            Path = path,
            Format = format,
            Provider = this,
            State = archive,
        };
    }

    public async IAsyncEnumerable<ArchiveEntry> EnumerateAsync(
        ArchiveHandle handle,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var archive = (TarArchive)handle.State;
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
        var e = (TarArchiveEntry)entry.Token;
        return Task.FromResult(e.OpenEntryStream());
    }

    public Task ExtractAsync(
        ArchiveHandle handle,
        string destinationDirectory,
        ExtractOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var archive = (TarArchive)handle.State;
        var entries = archive.Entries.ToList();
        var tracker = new ProgressTracker(progress);
        tracker.SetTotal(entries.Where(e => !e.IsDirectory).Sum(e => Math.Max(e.Size, 0)), entries.Count);

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
    {
        var tracker = new ProgressTracker(progress);
        tracker.SetTotal(items.Where(i => !i.IsDirectory).Sum(i => i.Size), items.Count);

        return Task.Run(() =>
        {
            using var fs = File.Create(outputPath);
            using Stream wrapped = options.Format switch
            {
                ArchiveFormat.TarGz => new GZipStream(fs, CompressionMode.Compress, MapDeflateLevel(options.CompressionLevel)),
                ArchiveFormat.TarBz2 => new BZip2Stream(fs, CompressionMode.Compress, decompressConcatenated: false),
                _ => fs,
            };

            var writerOptions = new TarWriterOptions(CompressionType.None, finalizeArchiveOnClose: true)
            {
                LeaveStreamOpen = true,
            };
            using var writer = new TarWriter(wrapped, writerOptions);

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

    private static CompressionLevel MapDeflateLevel(int level) => level switch
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

    private static ArchiveEntry Map(TarArchiveEntry e) => new()
    {
        Key = e.Key ?? string.Empty,
        FullPath = e.Key ?? string.Empty,
        Name = Path.GetFileName(e.Key ?? string.Empty),
        IsDirectory = e.IsDirectory,
        IsEncrypted = false,
        Size = e.Size,
        CompressedSize = e.CompressedSize,
        LastModified = e.LastModifiedTime,
        Crc32 = null,
        Token = e,
    };
}
