#include "pch.h"
#include <cstdio>
int wmain()
{
    auto hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED); if (FAILED(hr)) return 1;
    auto apartment=wil::scope_exit([] { CoUninitialize(); });
    hr=MFStartup(MF_VERSION); if (FAILED(hr)) return 2;
    auto shutdown=wil::scope_exit([] { MFShutdown(); });
    wil::com_ptr_nothrow<IMFMediaType> type;
    if (FAILED(MFCreateMediaType(&type))) return 3;
    type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video); type->SetGUID(MF_MT_SUBTYPE,MFVideoFormat_NV12);
    MFSetAttributeSize(type.get(),MF_MT_FRAME_SIZE,1280,720);
    SimpleFrameGenerator generator;
    if (FAILED(generator.Initialize(type.get()))) return 4;
    std::vector<BYTE> frame(1280*720*3/2+64,0xAB);
    for (int i=0; i<2000; ++i) if (FAILED(generator.CreateFrame(frame.data(),static_cast<DWORD>(frame.size()-64),1280,0))) return 5;
    for (size_t i=0; i<1280*720; ++i) if (frame[i]!=16) return 6;
    for (size_t i=1280*720; i<1280*720*3/2; ++i) if (frame[i]!=128) return 7;
    for (size_t i=frame.size()-64; i<frame.size(); ++i) if (frame[i]!=0xAB) return 8;
    if (SUCCEEDED(generator.CreateFrame(frame.data(),10,1280,0))) return 9;
    if (SUCCEEDED(generator.CreateFrame(frame.data(),static_cast<DWORD>(frame.size()),-1280,0))) return 10;
    MFSetAttributeSize(type.get(),MF_MT_FRAME_SIZE,1920,1080);
    if (SUCCEEDED(generator.Initialize(type.get()))) return 11;
    puts("PASS: 2000 no-camera frames, NV12 black, no stale pixels, guard bytes intact, invalid sizes/strides/formats rejected. Hardware switching not tested.");
    return 0;
}
