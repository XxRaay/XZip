namespace XZip.Core.Tests.Helpers;

/// <summary>
/// Disposable temporary directory used by tests.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir(string? prefix = null)
    {
        var p = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"xzip-tests-{prefix ?? "tmp"}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(p);
        Path = p;
    }

    public string FilePath(string name) => System.IO.Path.Combine(Path, name);

    public string CreateFile(string name, string content)
    {
        var full = FilePath(name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public string CreateBinaryFile(string name, byte[] content)
    {
        var full = FilePath(name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
