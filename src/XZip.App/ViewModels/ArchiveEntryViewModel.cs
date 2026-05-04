using CommunityToolkit.Mvvm.ComponentModel;

using XZip.Core.Abstractions;

namespace XZip.App.ViewModels;

/// <summary>
/// Display-friendly wrapper around <see cref="ArchiveEntry"/>.
/// </summary>
public sealed partial class ArchiveEntryViewModel : ObservableObject
{
    public ArchiveEntry Entry { get; }

    public ArchiveEntryViewModel(ArchiveEntry entry)
    {
        Entry = entry;
    }

    public string Name => Entry.Name;
    public string Path => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsEncrypted => Entry.IsEncrypted;
    public string SizeText => IsDirectory ? "—" : FormatSize(Entry.Size);
    public string CompressedSizeText => IsDirectory ? "—" : FormatSize(Entry.CompressedSize);
    public string RatioText => IsDirectory || Entry.Size == 0 ? "—" : $"{Entry.CompressionRatio * 100:0.0}%";
    public string ModifiedText => Entry.LastModified is { } d ? d.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
    public string Crc32Text => Entry.Crc32 is { } c ? c.ToString("X8") : "—";
    public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE7C3";

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var units = new[] { "KB", "MB", "GB", "TB", "PB" };
        double v = bytes;
        var i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
        return $"{v:0.##} {units[i]}";
    }
}
