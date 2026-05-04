using XZip.Core;
using XZip.Core.Abstractions;

namespace XZip.Helper;

/// <summary>
/// Headless companion executable used by the shell extension to perform fast, non-interactive
/// operations (extract here / add to archive) without spinning up the full WinUI app.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var verb = args[0].ToLowerInvariant();
        var rest = args[1..];
        var service = ArchiveServiceFactory.CreateDefault();

        try
        {
            return verb switch
            {
                "extract" => await ExtractAsync(service, rest),
                "extract-here" => await ExtractHereAsync(service, rest),
                "extract-to" => await ExtractToFolderAsync(service, rest),
                "add" => await AddAsync(service, rest),
                "list" => await ListAsync(service, rest),
                "probe" => Probe(service, rest),
                _ => Unknown(verb),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"xzip-helper: {ex.Message}");
            return 2;
        }
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"xzip-helper: unknown verb '{verb}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  xzip-helper extract <archive> <destination>");
        Console.Error.WriteLine("  xzip-helper extract-here <archive>");
        Console.Error.WriteLine("  xzip-helper extract-to <archive>            (creates <name>/ next to archive)");
        Console.Error.WriteLine("  xzip-helper add <output.zip> <input...>");
        Console.Error.WriteLine("  xzip-helper list <archive>");
        Console.Error.WriteLine("  xzip-helper probe <archive>");
    }

    private static async Task<int> ExtractAsync(ArchiveService service, string[] args)
    {
        if (args.Length < 2) { PrintUsage(); return 1; }
        var archive = args[0];
        var dest = args[1];
        await service.ExtractAllAsync(archive, dest, null, NewProgress());
        Console.WriteLine();
        Console.WriteLine($"Done -> {dest}");
        return 0;
    }

    private static async Task<int> ExtractHereAsync(ArchiveService service, string[] args)
    {
        if (args.Length < 1) { PrintUsage(); return 1; }
        var archive = args[0];
        var dest = Path.GetDirectoryName(Path.GetFullPath(archive)) ?? Environment.CurrentDirectory;
        await service.ExtractAllAsync(archive, dest, null, NewProgress());
        Console.WriteLine();
        Console.WriteLine($"Done -> {dest}");
        return 0;
    }

    private static async Task<int> ExtractToFolderAsync(ArchiveService service, string[] args)
    {
        if (args.Length < 1) { PrintUsage(); return 1; }
        var archive = args[0];
        var parent = Path.GetDirectoryName(Path.GetFullPath(archive)) ?? Environment.CurrentDirectory;
        var name = Path.GetFileNameWithoutExtension(archive);
        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var dest = Path.Combine(parent, name);
        Directory.CreateDirectory(dest);
        await service.ExtractAllAsync(archive, dest, null, NewProgress());
        Console.WriteLine();
        Console.WriteLine($"Done -> {dest}");
        return 0;
    }

    private static async Task<int> AddAsync(ArchiveService service, string[] args)
    {
        if (args.Length < 2) { PrintUsage(); return 1; }
        var output = args[0];
        var inputs = args[1..];

        var items = new List<SourceItem>();
        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                var rootName = Path.GetFileName(input.TrimEnd(Path.DirectorySeparatorChar));
                items.Add(SourceItem.FromDirectory(input, rootName));
                foreach (var d in Directory.EnumerateDirectories(input, "*", SearchOption.AllDirectories))
                {
                    items.Add(SourceItem.FromDirectory(d,
                        Path.Combine(rootName, Path.GetRelativePath(input, d))));
                }
                foreach (var f in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
                {
                    items.Add(SourceItem.FromFile(f,
                        Path.Combine(rootName, Path.GetRelativePath(input, f))));
                }
            }
            else if (File.Exists(input))
            {
                items.Add(SourceItem.FromFile(input, Path.GetFileName(input)));
            }
        }

        var format = OutputFormat(output);
        await service.CreateAsync(output, items, new CreateOptions
        {
            Format = format,
            CompressionLevel = 5,
            MaxDegreeOfParallelism = Environment.ProcessorCount,
        }, NewProgress());

        Console.WriteLine();
        Console.WriteLine($"Done -> {output}");
        return 0;
    }

    private static async Task<int> ListAsync(ArchiveService service, string[] args)
    {
        if (args.Length < 1) { PrintUsage(); return 1; }
        await using var handle = await service.OpenAsync(args[0]);
        await foreach (var e in service.EnumerateAsync(handle))
        {
            Console.WriteLine($"{(e.IsDirectory ? "D" : "F")}  {e.Size,12}  {e.FullPath}");
        }
        return 0;
    }

    private static int Probe(ArchiveService service, string[] args)
    {
        if (args.Length < 1) { PrintUsage(); return 1; }
        var f = service.Probe(args[0]);
        Console.WriteLine(f.ToString());
        return f == ArchiveFormat.Unknown ? 3 : 0;
    }

    private static ArchiveFormat OutputFormat(string outputPath)
    {
        var lower = outputPath.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz")) return ArchiveFormat.TarGz;
        if (lower.EndsWith(".tar.bz2") || lower.EndsWith(".tbz2") || lower.EndsWith(".tbz")) return ArchiveFormat.TarBz2;
        if (lower.EndsWith(".tar")) return ArchiveFormat.Tar;
        return ArchiveFormat.Zip;
    }

    private static IProgress<ArchiveProgress> NewProgress()
    {
        var lastLen = 0;
        return new Progress<ArchiveProgress>(p =>
        {
            var pct = p.Percentage * 100;
            var line = $"\r[{pct,5:0.0}%] {p.ProcessedItems}/{p.TotalItems} {p.CurrentItem}";
            if (line.Length < lastLen) line = line.PadRight(lastLen);
            lastLen = line.Length;
            Console.Out.Write(line);
        });
    }
}
