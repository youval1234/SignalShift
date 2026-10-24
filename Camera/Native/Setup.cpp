// Optional machine-wide COM registration. Only run by the user's explicit setup action.
#include <windows.h>
#include <shlobj.h>
#include <string>
#include <filesystem>
#include <vector>
#include <bcrypt.h>
#include "CameraExpectedHash.h"
#pragma comment(lib,"bcrypt")
#pragma comment(lib,"shell32")
#pragma comment(lib,"ole32")
bool MatchesBuild(const std::filesystem::path& path)
{
    HANDLE file=CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if (file==INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER size{}; bool valid=GetFileSizeEx(file,&size) && size.QuadPart>0 && size.QuadPart<=32*1024*1024;
    std::vector<unsigned char> data(valid?static_cast<size_t>(size.QuadPart):0);
    DWORD read=0;
    valid=valid && ReadFile(file,data.data(),static_cast<DWORD>(data.size()),&read,nullptr) && read==data.size();
    CloseHandle(file);
    unsigned char hash[32]{};
    return valid && BCryptHash(BCRYPT_SHA256_ALG_HANDLE,nullptr,0,data.data(),static_cast<ULONG>(data.size()),hash,sizeof(hash))>=0 && memcmp(hash,ExpectedCameraHash,sizeof(hash))==0;
}
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR command, int)
{
    HRESULT hr = S_OK;
    try {
        PWSTR base=nullptr; hr=SHGetKnownFolderPath(FOLDERID_ProgramFiles, 0, nullptr, &base);
        if (FAILED(hr)) throw hr;
        std::filesystem::path target=std::filesystem::path(base)/L"SignalShift"/L"Camera"/L"2.0.0";
        CoTaskMemFree(base);
        auto destination=target/L"SignalShift.Camera.dll";
        const bool uninstall = wcscmp(command, L"--uninstall")==0;
        if (!uninstall) {
            wchar_t executable[32768]{}; if (!GetModuleFileNameW(nullptr,executable,ARRAYSIZE(executable))) throw HRESULT_FROM_WIN32(GetLastError());
            auto source=std::filesystem::path(executable).parent_path()/L"SignalShift.Camera.dll";
            if (!MatchesBuild(source)) throw HRESULT_FROM_WIN32(ERROR_DATA_CHECKSUM_ERROR);
            std::filesystem::create_directories(target);
            if (!CopyFileW(source.c_str(),destination.c_str(),FALSE)) throw HRESULT_FROM_WIN32(GetLastError());
        }
        if (!MatchesBuild(destination)) throw HRESULT_FROM_WIN32(ERROR_DATA_CHECKSUM_ERROR);
        HMODULE module=LoadLibraryExW(destination.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!module) throw HRESULT_FROM_WIN32(GetLastError());
        auto registration=reinterpret_cast<HRESULT(__stdcall*)()>(GetProcAddress(module,uninstall?"DllUnregisterServer":"DllRegisterServer"));
        hr=registration?registration():E_NOINTERFACE; FreeLibrary(module);
        if (FAILED(hr)) throw hr;
        if (uninstall && !DeleteFileW(destination.c_str())) throw HRESULT_FROM_WIN32(GetLastError());
        MessageBoxW(nullptr,uninstall?L"The camera component was removed.":L"Camera component installed. Return to SignalShift and enable SignalShift Camera.",L"SignalShift Camera",MB_OK|MB_ICONINFORMATION);
        return 0;
    } catch (HRESULT error) { hr=error; }
      catch (...) { hr=E_FAIL; }
    wchar_t message[512]{};
    swprintf_s(message,L"Camera setup could not finish (0x%08X). If Windows blocked this unsigned preview, use a trusted signed build when available. Stop applications using the camera before upgrading or removing it. Windows security settings have not been changed.",static_cast<unsigned>(hr));
    MessageBoxW(nullptr,message,L"SignalShift Camera",MB_OK|MB_ICONERROR);
    return 1;
}
