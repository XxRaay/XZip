using XZip.Core.Abstractions;
using XZip.Core.Providers;

namespace XZip.Core;

/// <summary>
/// Factory for the default <see cref="ArchiveService"/> with all built-in providers wired up.
/// </summary>
public static class ArchiveServiceFactory
{
    /// <summary>
    /// Build the default service with all built-in providers: ZIP, 7z/RAR (read), TAR / TAR.GZ / TAR.BZ2.
    /// </summary>
    public static ArchiveService CreateDefault()
    {
        var providers = new List<IArchiveProvider>
        {
            new ZipArchiveProvider(),
            new SevenZipArchiveProvider(),
            new RarArchiveProvider(),
            new TarArchiveProvider(),
        };
        return new ArchiveService(providers);
    }
}
