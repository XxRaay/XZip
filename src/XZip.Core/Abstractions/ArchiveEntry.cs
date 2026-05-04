namespace XZip.Core.Abstractions;

/// <summary>
/// Read-only descriptor of a single entry inside an archive.
/// </summary>
public sealed class ArchiveEntry
{
    public required string Key { get; init; }

    public required string FullPath { get; init; }

    public required string Name { get; init; }

    public required bool IsDirectory { get; init; }

    public required bool IsEncrypted { get; init; }

    public long Size { get; init; }

    public long CompressedSize { get; init; }

    public DateTime? LastModified { get; init; }

    public uint? Crc32 { get; init; }

    /// <summary>
    /// Provider-specific opaque identifier used to open this entry's stream.
    /// </summary>
    public required object Token { get; init; }

    public double CompressionRatio => Size == 0 ? 0 : 1.0 - ((double)CompressedSize / Size);
}
