using FluentAssertions;

using XZip.Core;
using XZip.Core.Abstractions;
using XZip.Core.Tests.Helpers;

namespace XZip.Core.Tests;

public class ParallelPipelineTests
{
    [Fact]
    public async Task Pipeline_PreservesContentAndOrder()
    {
        using var tmp = new TempDir("pipeline");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);

        for (var i = 0; i < 64; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourceDir, $"f{i:D3}.txt"),
                $"file {i}: " + new string('x', 4096 + i));
        }

        var archivePath = Path.Combine(tmp.Path, "p.zip");
        var service = ArchiveServiceFactory.CreateDefault();
        await service.CreateFromDirectoryAsync(sourceDir, archivePath, new CreateOptions
        {
            Format = ArchiveFormat.Zip,
            MaxDegreeOfParallelism = 8,
            CompressionLevel = 6,
        });

        var ex = Path.Combine(tmp.Path, "ex");
        await service.ExtractAllAsync(archivePath, ex);

        for (var i = 0; i < 64; i++)
        {
            var fp = Path.Combine(ex, $"f{i:D3}.txt");
            File.Exists(fp).Should().BeTrue();
            (await File.ReadAllTextAsync(fp)).Should().StartWith($"file {i}: ");
        }
    }

    [Fact]
    public async Task Pipeline_ReportsProgressAcrossThreads()
    {
        using var tmp = new TempDir("pipeline-prog");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);
        for (var i = 0; i < 16; i++)
            await File.WriteAllBytesAsync(Path.Combine(sourceDir, $"f{i}.bin"), new byte[128 * 1024]);

        var archivePath = Path.Combine(tmp.Path, "out.zip");
        var service = ArchiveServiceFactory.CreateDefault();

        var snapshots = new List<ArchiveProgress>();
        var progress = new Progress<ArchiveProgress>(p => snapshots.Add(p));

        await service.CreateFromDirectoryAsync(sourceDir, archivePath, new CreateOptions
        {
            Format = ArchiveFormat.Zip,
            MaxDegreeOfParallelism = 4,
        }, progress);

        await Task.Delay(100);
        snapshots.Should().NotBeEmpty();
        snapshots.Last().ProcessedItems.Should().BeGreaterThan(0);
    }
}
