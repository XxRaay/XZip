namespace XZip.Core.Abstractions;

/// <summary>
/// Progress snapshot reported during long archive operations.
/// </summary>
public readonly record struct ArchiveProgress(
    string CurrentItem,
    long ProcessedBytes,
    long TotalBytes,
    int ProcessedItems,
    int TotalItems,
    double BytesPerSecond,
    TimeSpan Elapsed)
{
    public double Percentage => TotalBytes == 0 ? 0 : Math.Clamp((double)ProcessedBytes / TotalBytes, 0, 1);

    public TimeSpan Eta
    {
        get
        {
            if (BytesPerSecond <= 0 || TotalBytes <= 0) return TimeSpan.Zero;
            var remaining = Math.Max(0, TotalBytes - ProcessedBytes);
            return TimeSpan.FromSeconds(remaining / BytesPerSecond);
        }
    }
}
