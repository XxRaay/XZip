namespace XZip.Core.Internal;

/// <summary>
/// Helpers to neutralise zip-slip and other path traversal vectors when extracting archives.
/// </summary>
internal static class PathSafety
{
    /// <summary>
    /// Compute a safe absolute destination for an archive entry, ensuring it stays inside <paramref name="rootDirectory"/>.
    /// Throws <see cref="IOException"/> if the entry tries to escape the root.
    /// </summary>
    public static string ResolveSafeDestination(string rootDirectory, string entryPath)
    {
        var sanitized = (entryPath ?? string.Empty)
            .Replace('\\', '/')
            .TrimStart('/');

        var rootFull = Path.GetFullPath(rootDirectory);
        var combined = Path.GetFullPath(Path.Combine(rootFull, sanitized));

        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Refusing to extract entry '{entryPath}' outside of '{rootDirectory}'.");
        }

        return combined;
    }

    /// <summary>
    /// Resolve a non-clobbering destination by appending " (1)", " (2)", etc. when the file already exists.
    /// </summary>
    public static string GetUniquePath(string desiredPath)
    {
        if (!File.Exists(desiredPath) && !Directory.Exists(desiredPath)) return desiredPath;

        var dir = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }

        return desiredPath;
    }
}
