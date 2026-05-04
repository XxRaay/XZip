# Architecture

## High-level

```
+--------------------+      +--------------------+
|     XZip.App       |      |     XZip.Core      |
|  WinUI 3 / .NET 8  +----->+  Archive engine    |
+--------------------+      +---------+----------+
                                      |
                              +-------+-------+
                              |               |
                       +------v------+ +------v------+
                       | SharpCompress| | System.IO.  |
                       |              | | Compression |
                       +--------------+ +-------------+

+--------------------+      +--------------------+
|   XZip.ShellExt    |      |  xzip-helper.exe   |
|  C++/WRL DLL       +----->+  (uses XZip.Core)  |
|  IExplorerCommand  |      +--------------------+
+--------------------+
```

## Core engine flow

### Open + enumerate

```
ArchiveService.OpenAsync(path)
  -> picks IArchiveProvider via FormatDetector (magic bytes + extension)
  -> provider returns ArchiveHandle (provider-specific State, e.g. ZipArchive)

ArchiveService.EnumerateAsync(handle)
  -> IAsyncEnumerable<ArchiveEntry> yields entries from the underlying archive
```

### Extract

```
ArchiveService.ExtractAsync(handle, dest, opts, progress, ct)
  -> for each entry:
       1) PathSafety.ResolveSafeDestination(dest, key) [zip-slip guard]
       2) honour ConflictPolicy (Overwrite / Skip / Rename / Fail)
       3) stream entry into a file, reporting bytes via ProgressTracker
       4) preserve LastWriteTime when requested
```

### Create (parallel ZIP)

```
ZipArchiveProvider.CreateAsync
  -> ParallelZipPipeline.RunAsync (TPL Dataflow)

  read block (parallel, dop=N)            write block (sequential, dop=1)
  ┌────────────────────────────┐        ┌──────────────────────────────┐
  │ open file → MemoryStream   │ ─push─>│ ZipWriter.Write(entry, body) │
  │   per file                 │        │ tracker.AddBytes(size)        │
  └────────────────────────────┘        └──────────────────────────────┘
                       (BoundedCapacity = 2*dop, EnsureOrdered = true)
```

The pipeline keeps source order so the resulting archive lists entries in the
same order they were submitted.

## Threading and progress

* All Core operations are wrapped in `Task.Run`, so the UI thread never blocks.
* `ProgressTracker` aggregates byte/item counters and reports `ArchiveProgress`
  snapshots throttled to ~80 ms per update via `Stopwatch`.
* Cancellation is honoured at every entry boundary and inside the inner copy
  loops via `CancellationToken.ThrowIfCancellationRequested`.

## UI

* WinUI 3 + Windows App SDK 1.7+ (single-project MSIX-ready).
* MVVM via `CommunityToolkit.Mvvm` source generators.
* Pages: `ArchiveExplorerPage`, `CreateArchivePage`, `RecentPage`, `SettingsPage`.
* Shared services: `INavigationService`, `IDialogService`, `IFilePickerService`,
  `ISettingsService`.
* `MainWindow` extends `WinUIEx.WindowEx` for size / centering helpers.
* Mica is detected at runtime; on Windows 10 we fall back to Desktop Acrylic.

## Shell integration

`XZip.ShellExt` exposes four `IExplorerCommand` classes:

* Extract Here / Extract To / Open In XZip — visible only on archive types.
* Add To Archive — visible on any file or folder.

The CLSIDs are wired up in `Package.appxmanifest`:

* `com:Extension Category="windows.comServer"` → registers the same DLL for
  Windows 10 1809+ classic context menu.
* `desktop4:Extension Category="windows.fileExplorerContextMenus"` → registers
  for the Windows 11 modern context menu.

The shell extension launches `xzip-helper.exe` for fast operations (extract /
compress) and `xzip://` protocol activation for "Open in XZip".

## Why a separate helper?

Loading the WinUI process for a quick "Extract Here" would be too heavy and
flicker the title bar / system backdrop. The headless helper:

* Has no UI dependency (only `XZip.Core`).
* Starts in well under 100 ms.
* Reports stdout progress, which the shell extension currently swallows but
  could surface as a Windows toast in a future revision.
