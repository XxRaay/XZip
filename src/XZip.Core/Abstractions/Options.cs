namespace XZip.Core.Abstractions;

public enum ConflictPolicy
{
    Overwrite,
    Skip,
    Rename,
    Fail,
}

public sealed class ExtractOptions
{
    public ConflictPolicy Conflict { get; init; } = ConflictPolicy.Overwrite;

    public bool PreservePaths { get; init; } = true;

    public bool PreserveTimestamps { get; init; } = true;

    /// <summary>Limit extraction to entries whose key matches this filter (null = all).</summary>
    public Func<ArchiveEntry, bool>? Filter { get; init; }

    public string? Password { get; init; }
}

public sealed class CreateOptions
{
    public ArchiveFormat Format { get; init; } = ArchiveFormat.Zip;

    /// <summary>Compression level (0 = store, 9 = max). Mapped per-format.</summary>
    public int CompressionLevel { get; init; } = 5;

    public string? Password { get; init; }

    public bool UseAesEncryption { get; init; }

    /// <summary>Maximum parallelism for formats that support per-entry parallel compression.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
}

/// <summary>
/// One source item to add into a new archive. Either a file or directory.
/// </summary>
public sealed class SourceItem
{
    public required string AbsolutePath { get; init; }

    /// <summary>Path stored inside the archive (forward slashes, no leading slash).</summary>
    public required string EntryPath { get; init; }

    public required bool IsDirectory { get; init; }

    public long Size { get; init; }

    public DateTime LastModified { get; init; } = DateTime.UtcNow;

    public static SourceItem FromFile(string absolutePath, string entryPath)
    {
        var fi = new FileInfo(absolutePath);
        return new SourceItem
        {
            AbsolutePath = absolutePath,
            EntryPath = entryPath.Replace('\\', '/').TrimStart('/'),
            IsDirectory = false,
            Size = fi.Exists ? fi.Length : 0,
            LastModified = fi.Exists ? fi.LastWriteTimeUtc : DateTime.UtcNow,
        };
    }

    public static SourceItem FromDirectory(string absolutePath, string entryPath) => new()
    {
        AbsolutePath = absolutePath,
        EntryPath = entryPath.Replace('\\', '/').TrimStart('/').TrimEnd('/') + "/",
        IsDirectory = true,
        Size = 0,
        LastModified = Directory.Exists(absolutePath) ? Directory.GetLastWriteTimeUtc(absolutePath) : DateTime.UtcNow,
    };
}
