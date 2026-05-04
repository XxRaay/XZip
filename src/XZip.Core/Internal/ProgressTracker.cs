using System.Diagnostics;

using XZip.Core.Abstractions;

namespace XZip.Core.Internal;

/// <summary>
/// Aggregates byte/item counters and emits <see cref="ArchiveProgress"/> snapshots
/// throttled to roughly one update per <see cref="ReportEvery"/>.
/// Thread-safe.
/// </summary>
internal sealed class ProgressTracker
{
    private readonly IProgress<ArchiveProgress>? _sink;
    private readonly Stopwatch _stopwatch;
    private readonly object _lock = new();
    private readonly TimeSpan _reportEvery;

    private long _processedBytes;
    private int _processedItems;
    private long _totalBytes;
    private int _totalItems;
    private string _currentItem = string.Empty;
    private DateTime _lastReport = DateTime.MinValue;

    public ProgressTracker(IProgress<ArchiveProgress>? sink, TimeSpan? reportEvery = null)
    {
        _sink = sink;
        _stopwatch = Stopwatch.StartNew();
        _reportEvery = reportEvery ?? TimeSpan.FromMilliseconds(80);
    }

    public TimeSpan ReportEvery => _reportEvery;

    public void SetTotal(long bytes, int items)
    {
        lock (_lock)
        {
            _totalBytes = bytes;
            _totalItems = items;
        }
    }

    public void BeginItem(string name, long size)
    {
        lock (_lock)
        {
            _currentItem = name;
        }
        Report(force: true);
    }

    public void AddBytes(long bytes)
    {
        if (bytes <= 0) return;
        Interlocked.Add(ref _processedBytes, bytes);
        Report();
    }

    public void CompleteItem()
    {
        Interlocked.Increment(ref _processedItems);
        Report();
    }

    public void Finish() => Report(force: true);

    private void Report(bool force = false)
    {
        if (_sink is null) return;

        ArchiveProgress snapshot;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (!force && now - _lastReport < _reportEvery) return;
            _lastReport = now;

            var elapsed = _stopwatch.Elapsed;
            var seconds = elapsed.TotalSeconds;
            var bps = seconds > 0 ? _processedBytes / seconds : 0;

            snapshot = new ArchiveProgress(
                CurrentItem: _currentItem,
                ProcessedBytes: Interlocked.Read(ref _processedBytes),
                TotalBytes: _totalBytes,
                ProcessedItems: _processedItems,
                TotalItems: _totalItems,
                BytesPerSecond: bps,
                Elapsed: elapsed);
        }

        _sink.Report(snapshot);
    }
}
