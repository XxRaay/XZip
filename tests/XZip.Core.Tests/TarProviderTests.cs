using FluentAssertions;

using XZip.Core;
using XZip.Core.Abstractions;
using XZip.Core.Tests.Helpers;

namespace XZip.Core.Tests;

public class TarProviderTests
{
    [Theory]
    [InlineData(ArchiveFormat.Tar, ".tar")]
    [InlineData(ArchiveFormat.TarGz, ".tar.gz")]
    [InlineData(ArchiveFormat.TarBz2, ".tar.bz2")]
    public async Task Tar_Variants_RoundTrip(ArchiveFormat format, string ext)
    {
        using var tmp = new TempDir($"tar-{format}");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);

        await File.WriteAllTextAsync(Path.Combine(sourceDir, "alpha.txt"), "alpha");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "beta.txt"), "beta beta beta");

        var archivePath = Path.Combine(tmp.Path, "out" + ext);
        var service = ArchiveServiceFactory.CreateDefault();

        await service.CreateFromDirectoryAsync(sourceDir, archivePath, new CreateOptions
        {
            Format = format,
            CompressionLevel = 5,
        });

        File.Exists(archivePath).Should().BeTrue();
        new FileInfo(archivePath).Length.Should().BeGreaterThan(0);

        var ex = Path.Combine(tmp.Path, "ex");
        await service.ExtractAllAsync(archivePath, ex);

        File.Exists(Path.Combine(ex, "alpha.txt")).Should().BeTrue();
        File.Exists(Path.Combine(ex, "sub", "beta.txt")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(ex, "alpha.txt"))).Should().Be("alpha");
        (await File.ReadAllTextAsync(Path.Combine(ex, "sub", "beta.txt"))).Should().Be("beta beta beta");
    }

    [Fact]
    public async Task Service_Probes_TarGz()
    {
        using var tmp = new TempDir("tar-probe");
        var sourceDir = Path.Combine(tmp.Path, "src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "x.txt"), "x");

        var archivePath = Path.Combine(tmp.Path, "x.tar.gz");
        var service = ArchiveServiceFactory.CreateDefault();
        await service.CreateFromDirectoryAsync(sourceDir, archivePath,
            new CreateOptions { Format = ArchiveFormat.TarGz });

        service.Probe(archivePath).Should().BeOneOf(ArchiveFormat.TarGz, ArchiveFormat.GZip);
    }
}
