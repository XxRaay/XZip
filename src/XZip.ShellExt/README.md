# XZip.ShellExt

A native (C++/WRL) DLL that adds File Explorer context-menu items for `.zip`, `.7z`, `.tar`,
`.tar.gz`, `.tar.bz2`, `.gz`, `.bz2` files and for any selected file/folder.

## Commands

| CLSID                                  | Title                       | Action                                                                  |
| -------------------------------------- | --------------------------- | ----------------------------------------------------------------------- |
| `3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C12` | Извлечь сюда (XZip)         | runs `xzip-helper extract-here <path>`                                  |
| `3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C13` | Извлечь в подпапку (XZip)   | runs `xzip-helper extract-to <path>`                                    |
| `3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C14` | Добавить в архив XZip…      | runs `xzip-helper add <out.zip> <selected>`                             |
| `3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C15` | Открыть в XZip              | launches `xzip://open?path=<path>` (handled by `XZip.App`)              |

## Build

The DLL targets the Visual C++ v143 toolset and `WindowsTargetPlatformVersion=10.0`.

```powershell
msbuild src\XZip.ShellExt\XZip.ShellExt.vcxproj -p:Configuration=Release -p:Platform=x64
```

The compiled `XZip.ShellExt.dll` ships next to `xzip-helper.exe` so the helper can be invoked
without an absolute path lookup beyond `GetModuleDirectory()`.

## Registration

* **Windows 11 modern context menu**: register through `Package.appxmanifest` (single-project MSIX
  in `XZip.App`) using the `<desktop4:Extension Category="windows.fileExplorerContextMenus">`
  block — see Phase 8 packaging notes in the plan.
* **Windows 10 1809+ classic context menu**: register the same DLL by adding `<comServer>` and
  `<fileTypeAssociation>` entries that point at the four CLSIDs above.

Both registrations are part of the MSIX package so installing the package wires up Win10 and Win11
behaviour in one go.
