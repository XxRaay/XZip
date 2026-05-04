namespace XZip.Core.Abstractions;

/// <summary>
/// Supported archive container formats.
/// </summary>
public enum ArchiveFormat
{
    Unknown = 0,
    Zip,
    SevenZip,
    Tar,
    TarGz,
    TarBz2,
    GZip,
    BZip2,
}

public static class ArchiveFormatExtensions
{
    public static string DefaultExtension(this ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => ".zip",
        ArchiveFormat.SevenZip => ".7z",
        ArchiveFormat.Tar => ".tar",
        ArchiveFormat.TarGz => ".tar.gz",
        ArchiveFormat.TarBz2 => ".tar.bz2",
        ArchiveFormat.GZip => ".gz",
        ArchiveFormat.BZip2 => ".bz2",
        _ => string.Empty,
    };

    public static string DisplayName(this ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => "ZIP",
        ArchiveFormat.SevenZip => "7-Zip",
        ArchiveFormat.Tar => "TAR",
        ArchiveFormat.TarGz => "TAR + GZip",
        ArchiveFormat.TarBz2 => "TAR + BZip2",
        ArchiveFormat.GZip => "GZip",
        ArchiveFormat.BZip2 => "BZip2",
        _ => "Unknown",
    };

    /// <summary>
    /// True if the format supports random access to entries (otherwise it is a stream-only format like .gz).
    /// </summary>
    public static bool SupportsRandomAccess(this ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip or ArchiveFormat.SevenZip or ArchiveFormat.Tar
            or ArchiveFormat.TarGz or ArchiveFormat.TarBz2 => true,
        _ => false,
    };

    /// <summary>
    /// True if entries can be compressed independently and therefore in parallel.
    /// </summary>
    public static bool SupportsParallelCompression(this ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => true,
        _ => false,
    };
}
