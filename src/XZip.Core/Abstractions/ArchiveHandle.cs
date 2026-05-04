namespace XZip.Core.Abstractions;

/// <summary>
/// Disposable handle representing an opened archive. Provider-specific state lives in <see cref="State"/>.
/// </summary>
public sealed class ArchiveHandle : IAsyncDisposable
{
    public required string Path { get; init; }

    public required ArchiveFormat Format { get; init; }

    public required IArchiveProvider Provider { get; init; }

    /// <summary>Provider-private state (e.g. an open SharpCompress archive).</summary>
    public required object State { get; init; }

    public string? Password { get; init; }

    public ValueTask DisposeAsync()
    {
        if (State is IAsyncDisposable ad) return ad.DisposeAsync();
        if (State is IDisposable d) d.Dispose();
        return ValueTask.CompletedTask;
    }
}
