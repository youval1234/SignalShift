// SignalShift controller ABI. The tray loads this only when camera support is used.
#include "pch.h"
struct CameraSession {
    wil::com_ptr_nothrow<IMFVirtualCamera> camera;
    CO_MTA_USAGE_COOKIE mta = nullptr;
    ~CameraSession() { camera.reset(); if (mta) CoDecrementMTAUsage(mta); }
};
extern "C" __declspec(dllexport) HRESULT __cdecl CameraEnumerate(void (__cdecl *callback)(const wchar_t*, const wchar_t*)) noexcept
{
    if (!callback) return E_POINTER;
    RETURN_IF_FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED));
    auto apartment = wil::scope_exit([] { CoUninitialize(); });
    auto hr = MFStartup(MF_VERSION); if (FAILED(hr)) return hr;
    auto shutdown = wil::scope_exit([] { MFShutdown(); });
    try {
        wil::com_ptr_nothrow<IMFAttributes> attrs;
        RETURN_IF_FAILED(MFCreateAttributes(&attrs, 1));
        RETURN_IF_FAILED(attrs->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID));
        IMFActivate** devices = nullptr; UINT32 count=0;
        RETURN_IF_FAILED(MFEnumDeviceSources(attrs.get(), &devices, &count));
        auto release = wil::scope_exit([&] { for (UINT32 i=0; i<count; ++i) devices[i]->Release(); CoTaskMemFree(devices); });
        for (UINT32 i=0; i<count; ++i) {
            UINT32 hardware=0;
            if (FAILED(devices[i]->GetUINT32(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_HW_SOURCE, &hardware)) || !hardware) continue;
            wil::unique_cotaskmem_string id, name; UINT32 length=0;
            if (FAILED(devices[i]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, &id, &length)) ||
                FAILED(devices[i]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME, &name, &length))) continue;
            // Do not allow wrapping our own output or another virtual source.
            if (wcsstr(name.get(), L"SignalShift") || wcsstr(id.get(), VIRTUALCAMERAMEDIASOURCE_CLSID)) continue;
            callback(id.get(), name.get());
        }
        return S_OK;
    } catch (...) { return winrt::to_hresult(); }
}
extern "C" __declspec(dllexport) HRESULT __cdecl CameraStart(const wchar_t* configPath, void** result) noexcept
{
    if (!configPath || !result) return E_POINTER; *result = nullptr;
    RETURN_IF_FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED));
    auto apartment = wil::scope_exit([] { CoUninitialize(); });
    RETURN_IF_FAILED(MFStartup(MF_VERSION));
    auto cleanup = wil::scope_exit([] { MFShutdown(); });
    try {
        auto session = std::make_unique<CameraSession>();
        RETURN_IF_FAILED(CoIncrementMTAUsage(&session->mta));
        RETURN_IF_FAILED(MFCreateVirtualCamera(MFVirtualCameraType_SoftwareCameraSource, MFVirtualCameraLifetime_Session,
            MFVirtualCameraAccess_CurrentUser, L"SignalShift Camera", VIRTUALCAMERAMEDIASOURCE_CLSID, nullptr, 0, &session->camera));
        RETURN_IF_FAILED(session->camera->SetString(SIGNALSHIFT_CONFIG_PATH, configPath));
        RETURN_IF_FAILED(session->camera->Start(nullptr));
        *result = session.release(); cleanup.release(); return S_OK;
    } catch (...) { return winrt::to_hresult(); }
}
extern "C" __declspec(dllexport) void __cdecl CameraStop(void* value) noexcept
{
    if (!value) return;
    const auto hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    auto apartment = wil::scope_exit([&] { if (SUCCEEDED(hr)) CoUninitialize(); });
    auto session = std::unique_ptr<CameraSession>(static_cast<CameraSession*>(value));
    if (session->camera) { session->camera->Stop(); session->camera->Remove(); session->camera->Shutdown(); }
    session.reset(); MFShutdown();
}
extern HINSTANCE g_hInst;
extern "C" __declspec(dllexport) HRESULT __stdcall DllRegisterServer() noexcept
{
    wchar_t path[32768]{};
    if (!GetModuleFileNameW(g_hInst, path, ARRAYSIZE(path))) return HRESULT_FROM_WIN32(GetLastError());
    HKEY key=nullptr;
    auto error = RegCreateKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Classes\\CLSID\\{D25FA9DE-541A-4F52-A436-57A8FAEE97AE}\\InprocServer32", 0, nullptr, 0, KEY_SET_VALUE | KEY_WOW64_64KEY, nullptr, &key, nullptr);
    if (error != ERROR_SUCCESS) return HRESULT_FROM_WIN32(error);
    auto close = wil::scope_exit([&] { RegCloseKey(key); });
    error = RegSetValueExW(key, nullptr, 0, REG_SZ, reinterpret_cast<BYTE*>(path), static_cast<DWORD>((wcslen(path)+1)*sizeof(wchar_t)));
    if (error == ERROR_SUCCESS) error = RegSetValueExW(key, L"ThreadingModel", 0, REG_SZ, reinterpret_cast<const BYTE*>(L"Both"), sizeof(L"Both"));
    return HRESULT_FROM_WIN32(error);
}
extern "C" __declspec(dllexport) HRESULT __stdcall DllUnregisterServer() noexcept
{
    wchar_t module[32768]{}, registered[32768]{}; DWORD size=sizeof(registered);
    if (!GetModuleFileNameW(g_hInst, module, ARRAYSIZE(module))) return HRESULT_FROM_WIN32(GetLastError());
    auto read = RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Classes\\CLSID\\{D25FA9DE-541A-4F52-A436-57A8FAEE97AE}\\InprocServer32", nullptr, RRF_RT_REG_SZ, nullptr, registered, &size);
    if (read == ERROR_FILE_NOT_FOUND) return S_OK;
    if (read != ERROR_SUCCESS) return HRESULT_FROM_WIN32(read);
    if (_wcsicmp(module, registered)!=0) return HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH);
    auto error = RegDeleteTreeW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Classes\\CLSID\\{D25FA9DE-541A-4F52-A436-57A8FAEE97AE}");
    return error == ERROR_FILE_NOT_FOUND ? S_OK : HRESULT_FROM_WIN32(error);
}
