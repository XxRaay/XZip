using FluentAssertions;

using XZip.Core.Abstractions;
using XZip.Core.Internal;
using XZip.Core.Tests.Helpers;

namespace XZip.Core.Tests;

public class FormatDetectorTests
{
    [Theory]
    [InlineData("file.zip", ArchiveFormat.Zip)]
    [InlineData("FILE.ZIP", ArchiveFormat.Zip)]
    [InlineData("file.7z", ArchiveFormat.SevenZip)]
    [InlineData("file.tar", ArchiveFormat.Tar)]
    [InlineData("file.tar.gz", ArchiveFormat.TarGz)]
    [InlineData("file.tgz", ArchiveFormat.TarGz)]
    [InlineData("file.tar.bz2", ArchiveFormat.TarBz2)]
    [InlineData("file.tbz", ArchiveFormat.TarBz2)]
    [InlineData("file.gz", ArchiveFormat.GZip)]
    [InlineData("file.bz2", ArchiveFormat.BZip2)]
    [InlineData("file.txt", ArchiveFormat.Unknown)]
    public void DetectFromExtension_Works(string name, ArchiveFormat expected)
    {
        FormatDetector.DetectFromExtension(name).Should().Be(expected);
    }

    [Fact]
    public void DetectFromMagic_RecognisesZip()
    {
        ReadOnlySpan<byte> head = stackalloc byte[] { (byte)'P', (byte)'K', 0x03, 0x04 };
        FormatDetector.DetectFromMagic(head).Should().Be(ArchiveFormat.Zip);
    }

    [Fact]
    public void DetectFromMagic_Recognises7z()
    {
        ReadOnlySpan<byte> head = stackalloc byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };
        FormatDetector.DetectFromMagic(head).Should().Be(ArchiveFormat.SevenZip);
    }

    [Fact]
    public void DetectFromMagic_RecognisesGzip()
    {
        ReadOnlySpan<byte> head = stackalloc byte[] { 0x1F, 0x8B, 0x08, 0x00 };
        FormatDetector.DetectFromMagic(head).Should().Be(ArchiveFormat.GZip);
    }

    [Fact]
    public void DetectFromMagic_RecognisesBzip2()
    {
        ReadOnlySpan<byte> head = stackalloc byte[] { (byte)'B', (byte)'Z', (byte)'h', (byte)'9' };
        FormatDetector.DetectFromMagic(head).Should().Be(ArchiveFormat.BZip2);
    }

    [Fact]
    public void Detect_PrefersMagicOverExtension()
    {
        using var tmp = new TempDir("detect");
        var path = tmp.CreateBinaryFile("looks-like.txt",
            new byte[] { (byte)'P', (byte)'K', 0x03, 0x04, 0, 0, 0, 0 });

        FormatDetector.Detect(path).Should().Be(ArchiveFormat.Zip);
    }
}
