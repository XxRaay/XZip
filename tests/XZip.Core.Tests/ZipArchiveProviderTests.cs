using FluentAssertions;

using XZip.Core;
using XZip.Core.Abstractions;
using XZip.Core.Tests.Helpers;

namespace XZip.Core.Tests;

public class ZipArchiveProviderTests
{
    [Fact]
    public async Task CreateAndExtract_RoundTrip_PreservesContent()
    {
        using var tmp = new TempDir("zip-rt");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);

        var hello = Path.Combine(sourceDir, "hello.txt");
        await File.WriteAllTextAsync(hello, "Hello, XZip!");

        Directory.CreateDirectory(Path.Combine(sourceDir, "nested"));
        var nested = Path.Combine(sourceDir, "nested", "data.bin");
        await File.WriteAllBytesAsync(nested, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        var archivePath = Path.Combine(tmp.Path, "out.zip");
        var service = ArchiveServiceFactory.CreateDefault();

        await service.CreateFromDirectoryAsync(sourceDir, archivePath,
            new CreateOptions { Format = ArchiveFormat.Zip, MaxDegreeOfParallelism = 1 });

        File.Exists(archivePath).Should().BeTrue();
        new FileInfo(archivePath).Length.Should().BeGreaterThan(0);

        var extractDir = Path.Combine(tmp.Path, "extracted");
        await service.ExtractAllAsync(archivePath, extractDir);

        File.Exists(Path.Combine(extractDir, "hello.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "nested", "data.bin")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(extractDir, "hello.txt"))).Should().Be("Hello, XZip!");
    }

    [Fact]
    public async Task Create_Parallel_ProducesValidArchive()
    {
        using var tmp = new TempDir("zip-par");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);

        for (var i = 0; i < 32; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(sourceDir, $"f{i:00}.txt"),
                new string((char)('a' + (i % 26)), 1024));
        }

        var archivePath = Path.Combine(tmp.Path, "par.zip");
        var service = ArchiveServiceFactory.CreateDefault();

        await service.CreateFromDirectoryAsync(sourceDir, archivePath,
            new CreateOptions { Format = ArchiveFormat.Zip, MaxDegreeOfParallelism = 8 });

        var extractDir = Path.Combine(tmp.Path, "ex");
        await service.ExtractAllAsync(archivePath, extractDir);

        Directory.GetFiles(extractDir).Should().HaveCount(32);
    }

    [Fact]
    public async Task EnumerateAsync_ListsAllEntries()
    {
        using var tmp = new TempDir("zip-list");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "b.txt"), "bb");

        var archivePath = Path.Combine(tmp.Path, "list.zip");
        var service = ArchiveServiceFactory.CreateDefault();
        await service.CreateFromDirectoryAsync(sourceDir, archivePath);

        await using var handle = await service.OpenAsync(archivePath);

        var names = new List<string>();
        await foreach (var entry in service.EnumerateAsync(handle))
        {
            names.Add(entry.Name);
        }

        names.Should().Contain("a.txt").And.Contain("b.txt");
    }

    [Fact]
    public async Task OpenEntryAsync_ReturnsContent()
    {
        using var tmp = new TempDir("zip-openentry");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "data.txt"), "stream me");

        var archivePath = Path.Combine(tmp.Path, "stream.zip");
        var service = ArchiveServiceFactory.CreateDefault();
        await service.CreateFromDirectoryAsync(sourceDir, archivePath);

        await using var handle = await service.OpenAsync(archivePath);
        ArchiveEntry? entry = null;
        await foreach (var e in service.EnumerateAsync(handle))
        {
            if (e.Name == "data.txt") { entry = e; break; }
        }
        entry.Should().NotBeNull();

        await using var stream = await service.OpenEntryAsync(handle, entry!);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        text.Should().Be("stream me");
    }

    [Fact]
    public async Task ExtractAsync_ReportsProgress()
    {
        using var tmp = new TempDir("zip-prog");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);
        for (var i = 0; i < 5; i++)
            await File.WriteAllBytesAsync(Path.Combine(sourceDir, $"f{i}.bin"), new byte[64 * 1024]);

        var archivePath = Path.Combine(tmp.Path, "prog.zip");
        var service = ArchiveServiceFactory.CreateDefault();
        await service.CreateFromDirectoryAsync(sourceDir, archivePath);

        await using var handle = await service.OpenAsync(archivePath);
        var seen = new List<ArchiveProgress>();
        var progress = new Progress<ArchiveProgress>(p => seen.Add(p));

        await service.ExtractAsync(handle, Path.Combine(tmp.Path, "out"),
            new ExtractOptions(), progress);

        // Progress is async; allow a tick for marshalling.
        await Task.Delay(100);
        seen.Should().NotBeEmpty();
        seen.Last().ProcessedItems.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExtractAsync_HonoursCancellation()
    {
        using var tmp = new TempDir("zip-cancel");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);
        for (var i = 0; i < 50; i++)
            await File.WriteAllBytesAsync(Path.Combine(sourceDir, $"f{i:00}.bin"), new byte[256 * 1024]);

        var archivePath = Path.Combine(tmp.Path, "cancel.zip");
        var service = ArchiveServiceFactory.CreateDefault();
        await service.CreateFromDirectoryAsync(sourceDir, archivePath);

        await using var handle = await service.OpenAsync(archivePath);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.ExtractAsync(handle, Path.Combine(tmp.Path, "out"),
            new ExtractOptions(), null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtractAsync_PreventsZipSlip()
    {
        using var tmp = new TempDir("zip-slip");

        // Build a zip with a malicious entry name using a low-level helper:
        var archivePath = Path.Combine(tmp.Path, "evil.zip");
        await using (var fs = File.Create(archivePath))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("../escaped.txt");
            await using var w = new StreamWriter(e.Open());
            await w.WriteAsync("you should not see me");
        }

        var service = ArchiveServiceFactory.CreateDefault();
        await using var handle = await service.OpenAsync(archivePath);

        var dest = Path.Combine(tmp.Path, "out");
        var act = () => service.ExtractAsync(handle, dest, new ExtractOptions(), null);

        await act.Should().ThrowAsync<IOException>();
        File.Exists(Path.Combine(tmp.Path, "escaped.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Create_WithPasswordAndAes_RequiresPasswordToExtract()
    {
        using var tmp = new TempDir("zip-aes");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "secret.txt"), "top secret");

        var archivePath = Path.Combine(tmp.Path, "secret.zip");
        var service = ArchiveServiceFactory.CreateDefault();

        await service.CreateFromDirectoryAsync(sourceDir, archivePath, new CreateOptions
        {
            Format = ArchiveFormat.Zip,
            Password = "Pa$$w0rd!",
            UseAesEncryption = true,
            MaxDegreeOfParallelism = 8,
        });

        var badExtract = Path.Combine(tmp.Path, "bad");
        var bad = async () => await service.ExtractAllAsync(archivePath, badExtract, "wrong");
        await bad.Should().ThrowAsync<Exception>();

        var extractDir = Path.Combine(tmp.Path, "ok");
        await service.ExtractAllAsync(archivePath, extractDir, "Pa$$w0rd!");
        (await File.ReadAllTextAsync(Path.Combine(extractDir, "secret.txt"))).Should().Be("top secret");
    }
}
