using System.Threading.Tasks.Dataflow;

using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Writers.Zip;

using XZip.Core.Abstractions;

namespace XZip.Core.Internal;

/// <summary>
/// TPL Dataflow pipeline that reads source files in parallel, buffers them in memory,
/// and writes them sequentially into a ZIP. Order of source items is preserved.
/// </summary>
internal static class ParallelZipPipeline
{
    public static async Task RunAsync(
        string outputPath,
        IReadOnlyList<SourceItem> items,
        CreateOptions options,
        ProgressTracker tracker,
        CancellationToken cancellationToken)
    {
        await using var fs = File.Create(outputPath);
        var writerOpts = new ZipWriterOptions(CompressionType.Deflate)
        {
            DeflateCompressionLevel = MapLevel(options.CompressionLevel),
            LeaveStreamOpen = false,
        };
        using var writer = new ZipWriter(fs, writerOpts);

        var dop = Math.Max(1, options.MaxDegreeOfParallelism);

        var read = new TransformBlock<(int idx, SourceItem item), (int idx, SourceItem item, MemoryStream? body)>(
            async tuple =>
            {
                var (idx, item) = tuple;
                if (item.IsDirectory) return (idx, item, null);

                cancellationToken.ThrowIfCancellationRequested();
                var ms = new MemoryStream(checked((int)Math.Min(Math.Max(item.Size, 4096), int.MaxValue / 2)));
                await using (var src = File.OpenRead(item.AbsolutePath))
                {
                    await src.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                }
                ms.Position = 0;
                return (idx, item, (MemoryStream?)ms);
            },
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = dop,
                BoundedCapacity = dop * 2,
                EnsureOrdered = true,
                CancellationToken = cancellationToken,
            });

        var write = new ActionBlock<(int idx, SourceItem item, MemoryStream? body)>(tuple =>
        {
            var (_, item, body) = tuple;
            cancellationToken.ThrowIfCancellationRequested();
            tracker.BeginItem(item.EntryPath, item.Size);

            if (body is null)
            {
                tracker.CompleteItem();
                return;
            }

            using (body)
            {
                writer.Write(item.EntryPath, body, item.LastModified);
            }

            tracker.AddBytes(item.Size);
            tracker.CompleteItem();
        }, new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = 1,
            BoundedCapacity = dop * 2,
            CancellationToken = cancellationToken,
        });

        read.LinkTo(write, new DataflowLinkOptions { PropagateCompletion = true });

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await read.SendAsync((i, items[i]), cancellationToken).ConfigureAwait(false);
        }
        read.Complete();

        await write.Completion.ConfigureAwait(false);
        tracker.Finish();
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
}
