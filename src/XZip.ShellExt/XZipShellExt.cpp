// XZipShellExt.cpp - File Explorer context menu integration for XZip.
//
// Implements IExplorerCommand-based commands so the same DLL works in:
//  * Windows 10 1809+ legacy (right-click) context menu.
//  * Windows 11 modern context menu (when registered via Package.appxmanifest
//    using the windows.fileExplorerContextMenus extension category).
//
// This file deliberately uses the lean Win32 + WRL approach so the resulting
// DLL has no MFC / ATL / .NET dependency.  It is designed to be built with
// Visual Studio 2022 or the Windows SDK ClangCL toolset.

#include <windows.h>
#include <objbase.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>
#include <string>
#include <string_view>
#include <vector>

#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "shell32.lib")

using namespace Microsoft::WRL;

#define RETURN_IF_FAILED(hr) do { HRESULT _hr = (hr); if (FAILED(_hr)) return _hr; } while(0)

// {3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C12} - Extract Here
class __declspec(uuid("3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C12"))
ExtractHereCommand;
// {3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C13} - Extract To Folder
class __declspec(uuid("3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C13"))
ExtractToFolderCommand;
// {3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C14} - Add To Archive
class __declspec(uuid("3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C14"))
AddToArchiveCommand;
// {3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C15} - Open In XZip
class __declspec(uuid("3E5B8C12-1B4D-4A77-9A78-3F0B2D8B9C15"))
OpenInXZipCommand;

namespace
{
    // Returns the directory where this DLL lives so we can locate xzip-helper.exe next to it.
    std::wstring GetModuleDirectory()
    {
        wchar_t buf[MAX_PATH] = {};
        HMODULE hMod = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                           GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCWSTR>(&GetModuleDirectory), &hMod);
        if (hMod) GetModuleFileNameW(hMod, buf, MAX_PATH);
        std::wstring path = buf;
        auto pos = path.find_last_of(L"\\/");
        return pos == std::wstring::npos ? L"" : path.substr(0, pos);
    }

    HRESULT LaunchHelper(const std::wstring& verb, const std::vector<std::wstring>& args)
    {
        std::wstring exe = GetModuleDirectory() + L"\\xzip-helper.exe";

        std::wstring cmd = L"\"" + exe + L"\" " + verb;
        for (const auto& a : args)
        {
            cmd += L" \"" + a + L"\"";
        }

        STARTUPINFOW si{ sizeof(si) };
        PROCESS_INFORMATION pi{};
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_HIDE;

        std::vector<wchar_t> mutableCmd(cmd.begin(), cmd.end());
        mutableCmd.push_back(L'\0');

        if (!CreateProcessW(nullptr, mutableCmd.data(), nullptr, nullptr, FALSE,
                            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return S_OK;
    }

    HRESULT LaunchAppByProtocol(const std::wstring& path)
    {
        std::wstring url = L"xzip://open?path=" + path;
        SHELLEXECUTEINFOW info{};
        info.cbSize = sizeof(info);
        info.fMask = SEE_MASK_NOASYNC;
        info.lpVerb = L"open";
        info.lpFile = url.c_str();
        info.nShow = SW_SHOWNORMAL;
        return ShellExecuteExW(&info) ? S_OK : HRESULT_FROM_WIN32(GetLastError());
    }

    bool IsArchiveFile(const std::wstring& path)
    {
        const wchar_t* exts[] = { L".zip", L".7z", L".rar", L".tar", L".tgz", L".gz", L".bz2", L".tbz2", L".tbz" };
        for (auto* ext : exts)
        {
            if (PathMatchSpecW(path.c_str(), (std::wstring(L"*") + ext).c_str())) return true;
        }
        // tar.gz / tar.bz2 are not handled by PathMatchSpec for the double extension; check manually
        return path.size() > 7 &&
            (_wcsicmp(path.c_str() + path.size() - 7, L".tar.gz") == 0 ||
             (path.size() > 8 && _wcsicmp(path.c_str() + path.size() - 8, L".tar.bz2") == 0));
    }

    HRESULT CollectPaths(IShellItemArray* items, std::vector<std::wstring>& out)
    {
        if (!items) return E_INVALIDARG;
        DWORD count = 0;
        RETURN_IF_FAILED(items->GetCount(&count));
        for (DWORD i = 0; i < count; ++i)
        {
            ComPtr<IShellItem> item;
            if (FAILED(items->GetItemAt(i, &item))) continue;
            wchar_t* path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path)
            {
                out.emplace_back(path);
                CoTaskMemFree(path);
            }
        }
        return S_OK;
    }
}

// Base implementation reused by every command type.
template <typename TDerived>
class CommandBase
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand, IObjectWithSite>
{
public:
    // IExplorerCommand
    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* title) override
    {
        return SHStrDupW(static_cast<TDerived*>(this)->Title(), title);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        // No custom icon; let Explorer fall back to the package icon.
        *icon = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tooltip) override
    {
        *tooltip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override
    {
        *guid = static_cast<TDerived*>(this)->CanonicalGuid();
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        *state = static_cast<TDerived*>(this)->ShouldShow(items)
            ? ECS_ENABLED : ECS_HIDDEN;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        std::vector<std::wstring> paths;
        RETURN_IF_FAILED(CollectPaths(items, paths));
        return static_cast<TDerived*>(this)->Run(paths);
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) override
    {
        *ppEnum = nullptr;
        return E_NOTIMPL;
    }

    // IObjectWithSite (no-op; we don't host inside an explorer site)
    IFACEMETHODIMP SetSite(IUnknown*) override { return S_OK; }
    IFACEMETHODIMP GetSite(REFIID, void** ppv) override { *ppv = nullptr; return E_NOINTERFACE; }
};

// ===== Concrete commands =====

class ExtractHereCommand : public CommandBase<ExtractHereCommand>
{
public:
    LPCWSTR Title() const { return L"Extract Here (XZip)"; }
    GUID CanonicalGuid() const { return __uuidof(ExtractHereCommand); }

    bool ShouldShow(IShellItemArray* items) const
    {
        std::vector<std::wstring> paths;
        if (FAILED(CollectPaths(items, paths))) return false;
        return paths.size() == 1 && IsArchiveFile(paths[0]);
    }

    HRESULT Run(const std::vector<std::wstring>& paths) const
    {
        if (paths.size() != 1) return E_INVALIDARG;
        return LaunchHelper(L"extract-here", paths);
    }
};

class ExtractToFolderCommand : public CommandBase<ExtractToFolderCommand>
{
public:
    LPCWSTR Title() const { return L"Extract to Folder (XZip)"; }
    GUID CanonicalGuid() const { return __uuidof(ExtractToFolderCommand); }

    bool ShouldShow(IShellItemArray* items) const
    {
        std::vector<std::wstring> paths;
        if (FAILED(CollectPaths(items, paths))) return false;
        return paths.size() == 1 && IsArchiveFile(paths[0]);
    }

    HRESULT Run(const std::vector<std::wstring>& paths) const
    {
        if (paths.size() != 1) return E_INVALIDARG;
        return LaunchHelper(L"extract-to", paths);
    }
};

class AddToArchiveCommand : public CommandBase<AddToArchiveCommand>
{
public:
    LPCWSTR Title() const { return L"Add to XZip Archive..."; }
    GUID CanonicalGuid() const { return __uuidof(AddToArchiveCommand); }

    bool ShouldShow(IShellItemArray*) const { return true; }

    HRESULT Run(const std::vector<std::wstring>& paths) const
    {
        if (paths.empty()) return E_INVALIDARG;
        // Pick a default name: "<first>.zip" next to the first selected item.
        std::wstring out = paths[0];
        auto pos = out.find_last_of(L"\\/");
        std::wstring dir = pos == std::wstring::npos ? L"" : out.substr(0, pos);
        std::wstring name = pos == std::wstring::npos ? out : out.substr(pos + 1);
        std::wstring zip = (dir.empty() ? L"" : dir + L"\\") + name + L".zip";

        std::vector<std::wstring> args;
        args.push_back(zip);
        for (const auto& p : paths) args.push_back(p);
        return LaunchHelper(L"add", args);
    }
};

class OpenInXZipCommand : public CommandBase<OpenInXZipCommand>
{
public:
    LPCWSTR Title() const { return L"Open in XZip"; }
    GUID CanonicalGuid() const { return __uuidof(OpenInXZipCommand); }

    bool ShouldShow(IShellItemArray* items) const
    {
        std::vector<std::wstring> paths;
        if (FAILED(CollectPaths(items, paths))) return false;
        return paths.size() == 1 && IsArchiveFile(paths[0]);
    }

    HRESULT Run(const std::vector<std::wstring>& paths) const
    {
        if (paths.size() != 1) return E_INVALIDARG;
        return LaunchAppByProtocol(paths[0]);
    }
};

CoCreatableClass(ExtractHereCommand);
CoCreatableClass(ExtractToFolderCommand);
CoCreatableClass(AddToArchiveCommand);
CoCreatableClass(OpenInXZipCommand);

// === DLL plumbing ===

STDAPI DllGetActivationFactory(_In_ HSTRING activatableClassId, _COM_Outptr_ IActivationFactory** factory)
{
    return Module<InProc>::GetModule().GetActivationFactory(activatableClassId, factory);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, _COM_Outptr_ LPVOID FAR* ppv)
{
    return Module<InProc>::GetModule().GetClassObject(rclsid, riid, ppv);
}
