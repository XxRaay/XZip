using System.Globalization;
using System.Text;

using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Storage.Streams;

using XZip.App.ViewModels;

namespace XZip.App.Services;

/// <summary>
/// Decides how to render a single archive entry inside the preview pane.
/// </summary>
public static class PreviewLoader
{
    private const long TextLimit = 1L * 1024 * 1024;       // 1 MB
    private const long ImageLimit = 16L * 1024 * 1024;     // 16 MB
    private const long HexHeadBytes = 4 * 1024;            // first 4 KB

    private static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".rst", ".json", ".xml", ".yaml", ".yml", ".csv", ".tsv",
        ".cs", ".c", ".cpp", ".cc", ".h", ".hpp", ".java", ".kt", ".rs", ".go", ".py",
        ".js", ".ts", ".jsx", ".tsx", ".vue", ".html", ".htm", ".css", ".scss", ".sass",
        ".sh", ".ps1", ".bat", ".cmd", ".ini", ".cfg", ".conf", ".toml", ".env",
        ".gitignore", ".dockerignore", ".editorconfig", ".sln", ".csproj", ".vcxproj",
    };

    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".ico",
    };

    public static async Task LoadAsync(PreviewViewModel vm, ArchiveEntryViewModel entry,
        Func<CancellationToken, Task<Stream?>> openStream, CancellationToken ct)
    {
        vm.Reset();
        vm.EntryName = entry.Name;

        if (entry.IsDirectory)
        {
            vm.StatusText = "Папка";
            return;
        }

        var ext = Path.GetExtension(entry.Name);
        var size = entry.Entry.Size;

        if (TextExt.Contains(ext))
        {
            if (size > TextLimit)
            {
                vm.Kind = PreviewKind.TooLarge;
                vm.StatusText = $"Файл слишком большой ({ArchiveEntryViewModel.FormatSize(size)})";
                return;
            }
            await using var s = await openStream(ct).ConfigureAwait(false);
            if (s is null) return;
            using var reader = new StreamReader(s, detectEncodingFromByteOrderMarks: true);
            vm.TextContent = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            vm.Kind = PreviewKind.Text;
            return;
        }

        if (ImageExt.Contains(ext))
        {
            if (size > ImageLimit)
            {
                vm.Kind = PreviewKind.TooLarge;
                vm.StatusText = $"Изображение слишком большое ({ArchiveEntryViewModel.FormatSize(size)})";
                return;
            }
            await using var s = await openStream(ct).ConfigureAwait(false);
            if (s is null) return;
            var ms = new InMemoryRandomAccessStream();
            using (var dst = ms.AsStreamForWrite())
            {
                await s.CopyToAsync(dst, ct).ConfigureAwait(false);
            }
            ms.Seek(0);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(ms);
            vm.Image = bmp;
            vm.Kind = PreviewKind.Image;
            return;
        }

        // Fallback: hex dump first N bytes.
        await using (var s = await openStream(ct).ConfigureAwait(false))
        {
            if (s is null) return;
            var head = new byte[HexHeadBytes];
            var read = 0;
            int n;
            while (read < head.Length && (n = await s.ReadAsync(head.AsMemory(read), ct).ConfigureAwait(false)) > 0)
            {
                read += n;
            }
            vm.HexContent = FormatHex(head.AsSpan(0, read));
            vm.Kind = PreviewKind.Hex;
            vm.StatusText = $"Двоичный файл — показаны первые {ArchiveEntryViewModel.FormatSize(read)}";
        }
    }

    private static string FormatHex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 4);
        for (var i = 0; i < bytes.Length; i += 16)
        {
            sb.Append(i.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
            for (var j = 0; j < 16; j++)
            {
                if (i + j < bytes.Length) sb.Append(bytes[i + j].ToString("X2")).Append(' ');
                else sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (var j = 0; j < 16 && i + j < bytes.Length; j++)
            {
                var c = (char)bytes[i + j];
                sb.Append(c is >= ' ' and < (char)0x7F ? c : '.');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
