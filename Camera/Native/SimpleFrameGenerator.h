// SignalShift physical-camera frame adapter. Native, capture only while streaming.
#pragma once
#include "AsyncCameraRead.h"
class SimpleFrameGenerator
{
public:
    ~SimpleFrameGenerator() { Close(); }
    void SetConfig(const wchar_t* path) { m_config = path; }
    HRESULT Initialize(IMFMediaType* type);
    HRESULT CreateFrame(BYTE* destination, DWORD length, LONG pitch, ULONG unused);
private:
    HRESULT Open(const std::wstring& id);
    void Close();
    std::wstring ReadSelection();
    std::wstring m_config, m_selected;
    ULONGLONG m_lastConfig = 0, m_retry = 0;
    wil::com_ptr_nothrow<IMFMediaSource> m_source;
    wil::com_ptr_nothrow<IMFSourceReader> m_reader;
    std::shared_ptr<AsyncCameraRead> m_async;
    wil::com_ptr_nothrow<IMFMediaType> m_type;
    UINT32 m_width = 0, m_height = 0;
    LONG m_sourcePitch = 0;
};
