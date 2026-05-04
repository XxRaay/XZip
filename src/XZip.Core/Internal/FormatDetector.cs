using XZip.Core.Abstractions;

namespace XZip.Core.Internal;

/// <summary>
/// Inspects a file by magic bytes (and falls back to extension) to identify the archive format.
/// </summary>
internal static class FormatDetector
{
    public static ArchiveFormat Detect(string path)
    {
        var fromExt = DetectFromExtension(path);

        try
        {
            if (File.Exists(path))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Span<byte> head = stackalloc byte[16];
                var read = fs.Read(head);
                var fromMagic = DetectFromMagic(head[..read]);

                // If both agree, easy. If magic is gzip/bz2 but extension hints at tar.* — trust the extension,
                // since a tarball cannot be told apart from its plain compressed cousin from the first 16 bytes.
                if (fromMagic == ArchiveFormat.GZip && fromExt == ArchiveFormat.TarGz) return ArchiveFormat.TarGz;
                if (fromMagic == ArchiveFormat.BZip2 && fromExt == ArchiveFormat.TarBz2) return ArchiveFormat.TarBz2;

                if (fromMagic != ArchiveFormat.Unknown) return fromMagic;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return fromExt;
    }

    public static ArchiveFormat DetectFromExtension(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".zip")) return ArchiveFormat.Zip;
        if (name.EndsWith(".7z")) return ArchiveFormat.SevenZip;
        if (name.EndsWith(".tar.gz") || name.EndsWith(".tgz")) return ArchiveFormat.TarGz;
        if (name.EndsWith(".tar.bz2") || name.EndsWith(".tbz2") || name.EndsWith(".tbz")) return ArchiveFormat.TarBz2;
        if (name.EndsWith(".tar")) return ArchiveFormat.Tar;
        if (name.EndsWith(".gz")) return ArchiveFormat.GZip;
        if (name.EndsWith(".bz2")) return ArchiveFormat.BZip2;
        return ArchiveFormat.Unknown;
    }

    public static ArchiveFormat DetectFromMagic(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4 &&
            head[0] == 'P' && head[1] == 'K' &&
            (head[2] == 0x03 || head[2] == 0x05 || head[2] == 0x07))
            return ArchiveFormat.Zip;

        if (head.Length >= 6 &&
            head[0] == 0x37 && head[1] == 0x7A &&
            head[2] == 0xBC && head[3] == 0xAF &&
            head[4] == 0x27 && head[5] == 0x1C)
            return ArchiveFormat.SevenZip;

        if (head.Length >= 3 &&
            head[0] == 0x1F && head[1] == 0x8B && head[2] == 0x08)
            return ArchiveFormat.GZip; // could be tar.gz; refined later by trying to read a tar header

        if (head.Length >= 3 &&
            head[0] == 'B' && head[1] == 'Z' && head[2] == 'h')
            return ArchiveFormat.BZip2;

        return ArchiveFormat.Unknown;
    }
}
