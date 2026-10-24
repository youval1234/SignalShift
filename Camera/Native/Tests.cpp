#include "pch.h"
#include <cstdio>

// Uses generated samples only. No camera access or audio playback.
bool TestAsyncState()
{
    wil::com_ptr_nothrow<IMFSample> first, second;
    if (FAILED(MFCreateSample(&first)) || FAILED(MFCreateSample(&second))) return false;
    AsyncCameraRead state;
    if (!state.Begin(100) || state.Begin(101)) return false;
    state.Complete(S_OK, 0, first.get(), 120);
    // A second unsolicited completion must not replace/queue the first sample.
    state.Complete(S_OK, 0, second.get(), 121);
    auto ready=state.Take(122);
    if (ready.sample.get()!=first.get() || ready.timedOut || state.Take(123).sample.get()!=first.get()) return false;
    // A webcam delivering less than 30 fps repeats its latest current frame.
    if (!state.Begin(124) || state.Take(150).sample.get()!=first.get()) return false;
    state.Complete(S_OK, 0, nullptr, 160); // A stream tick must not refresh frame age.
    if (state.Take(621).sample) return false;
    if (!state.Begin(700) || state.Take(2199).timedOut || !state.Take(2200).timedOut) return false;
    state.Stop(); state.Complete(S_OK, 0, second.get(), 1701);
    auto stopped=state.Take(1702);
    if (stopped.sample || stopped.status!=MF_E_SHUTDOWN || state.Begin(1703)) return false;
    AsyncCameraRead next;
    if (!next.Begin(1800)) return false;
    next.Complete(S_OK, 0, second.get(), 1801);
    state.Complete(S_OK, 0, first.get(), 1802);
    if (next.Take(1803).sample.get()!=second.get() || state.Take(1804).sample) return false;
    if (!next.Begin(1900)) return false;
    next.Complete(S_OK, 0, first.get(), 1901);
    if (next.Take(2402).sample) return false; // Old completed frames are discarded.
    if (!next.Begin(2500)) return false;
    next.Complete(E_ACCESSDENIED, 0, first.get(), 2501);
    auto error=next.Take(2502);
    if (error.status!=E_ACCESSDENIED || error.sample || next.Begin(2503)) return false;
    for (auto flag : {MF_SOURCE_READERF_ENDOFSTREAM, MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED, MF_SOURCE_READERF_ERROR}) {
        AsyncCameraRead terminal; terminal.Begin(0); terminal.Complete(S_OK, flag, first.get(), 1);
        auto result=terminal.Take(2);
        if (result.sample || !(result.flags & AsyncCameraRead::TerminalFlags) || terminal.Begin(3)) return false;
    }
    AsyncCameraRead stress;
    for (ULONGLONG i=0; i<10000; ++i) {
        if (!stress.Begin(i)) return false;
        stress.Complete(S_OK, 0, i%2?first.get():second.get(), i);
        auto expected=i%2?first.get():second.get();
        if (stress.Take(i).sample.get()!=expected || stress.Take(i).sample.get()!=expected) return false;
    }
    auto owned=std::make_shared<AsyncCameraRead>(); std::weak_ptr<AsyncCameraRead> weak=owned;
    auto callback=winrt::make_self<CameraReaderCallback>(owned);
    owned.reset(); if (weak.expired()) return false;
    callback=nullptr; if (!weak.expired()) return false;
    puts("PASS: one-read backpressure, timeout, stale/late-frame rejection, error and format changes, 10000 bounded state cycles, callback lifetime release.");
    return true;
}
bool TestRealAsyncReader()
{
    wchar_t directory[MAX_PATH]{}, path[MAX_PATH]{};
    if (!GetTempPathW(ARRAYSIZE(directory),directory) || !GetTempFileNameW(directory,L"ssf",0,path)) return false;
    auto cleanup=wil::scope_exit([&] { DeleteFileW(path); });
    // One second of generated silent PCM, not recorded or played.
    std::vector<BYTE> wav(16044,0);
    memcpy(wav.data(),"RIFF",4); memcpy(wav.data()+8,"WAVEfmt ",8); memcpy(wav.data()+36,"data",4);
    auto word=[&](size_t offset, unsigned value, size_t bytes) { for (size_t i=0;i<bytes;++i) wav[offset+i]=static_cast<BYTE>(value>>(i*8)); };
    word(4,16036,4); word(16,16,4); word(20,1,2); word(22,1,2); word(24,8000,4);
    word(28,16000,4); word(32,2,2); word(34,16,2); word(40,16000,4);
    wil::unique_hfile file(CreateFileW(path,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr));
    DWORD written=0;
    if (!file || !WriteFile(file.get(),wav.data(),static_cast<DWORD>(wav.size()),&written,nullptr) || written!=wav.size()) return false;
    file.reset();
    auto state=std::make_shared<AsyncCameraRead>();
    auto callback=winrt::make_self<CameraReaderCallback>(state);
    wil::com_ptr_nothrow<IMFAttributes> attrs;
    wil::com_ptr_nothrow<IMFSourceReader> reader;
    if (FAILED(MFCreateAttributes(&attrs,1)) || FAILED(attrs->SetUnknown(MF_SOURCE_READER_ASYNC_CALLBACK,callback.get())) ||
        FAILED(MFCreateSourceReaderFromURL(path,attrs.get(),&reader))) return false;
    const auto started=GetTickCount64();
    if (!state->Begin(started) || FAILED(reader->ReadSample(MF_SOURCE_READER_FIRST_AUDIO_STREAM,0,nullptr,nullptr,nullptr,nullptr))) return false;
    bool passed=false;
    while (GetTickCount64()-started<2000) {
        auto ready=state->Take(GetTickCount64());
        if (FAILED(ready.status) || ready.timedOut || (ready.flags & AsyncCameraRead::TerminalFlags)) break;
        if (ready.sample) { DWORD size=0; passed=SUCCEEDED(ready.sample->GetTotalLength(&size)) && size>0; break; }
        Sleep(5);
    }
    state->Stop(); reader->Flush(MF_SOURCE_READER_ALL_STREAMS); reader.reset();
    if (passed) puts("PASS: actual asynchronous Media Foundation reader callback with generated PCM fixture; no camera hardware or playback.");
    return passed;
}
bool TestCadence()
{
    DWORD before=0, after=0; GetProcessHandleCount(GetCurrentProcess(),&before);
    FrameCadence cadence; auto started=GetTickCount64();
    for (int i=0; i<16; ++i) { cadence.Wait(); cadence.Delivered(); }
    auto elapsed=GetTickCount64()-started;
    cadence.Reset(); GetProcessHandleCount(GetCurrentProcess(),&after);
    if (elapsed<450 || elapsed>3000 || after>before+2) return false;
    puts("PASS: paced virtual output without busy polling; timer released on reset.");
    return true;
}
bool TestControlLifetime()
{
    auto source=winrt::make_self<winrt::WindowsSample::implementation::SimpleMediaSource>();
    if (FAILED(source->Initialize(nullptr))) return false;
    KSPROPERTY property{}; property.Set=PROPSETID_SIMPLEMEDIASOURCE_CUSTOMCONTROL;
    property.Id=KSPROPERTY_SIMPLEMEDIASOURCE_CUSTOMCONTROL_COLORING; property.Flags=KSPROPERTY_TYPE_GET;
    KSPROPERTY_SIMPLEMEDIASOURCE_CUSTOMCONTROL_COLORMODE_S value{}; ULONG returned=99;
    if (source->KsProperty(nullptr,sizeof(property),&value,sizeof(value),&returned)!=E_POINTER || returned!=0) return false;
    if (source->KsProperty(&property,sizeof(property),&value,sizeof(value),nullptr)!=E_POINTER) return false;
    if (source->KsProperty(&property,sizeof(property)-1,&value,sizeof(value),&returned)!=E_INVALIDARG) return false;
    if (source->KsProperty(&property,sizeof(property),nullptr,0,&returned)!=HRESULT_FROM_WIN32(ERROR_MORE_DATA) || returned!=sizeof(value)) return false;
    if (source->KsProperty(&property,sizeof(property),&value,sizeof(value)-1,&returned)!=HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER)) return false;
    if (FAILED(source->KsProperty(&property,sizeof(property),&value,sizeof(value),&returned)) || returned!=sizeof(value)) return false;
    if (FAILED(source->Shutdown())) return false;
    if (source->KsProperty(&property,sizeof(property),&value,sizeof(value),&returned)!=MF_E_SHUTDOWN) return false;
    puts("PASS: camera control pointer/buffer validation, valid GET and shutdown rejection; no capture or registration.");
    return true;
}
int wmain()
{
    auto hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED); if (FAILED(hr)) return 1;
    auto apartment=wil::scope_exit([] { CoUninitialize(); });
    hr=MFStartup(MF_VERSION); if (FAILED(hr)) return 2;
    auto shutdown=wil::scope_exit([] { MFShutdown(); });
    if (!TestControlLifetime()) { puts("FAIL: camera control lifetime"); return 16; }
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
    if (SUCCEEDED(generator.CreateFrame(frame.data(),static_cast<DWORD>(frame.size()),LONG_MAX,0))) return 12;
    MFSetAttributeSize(type.get(),MF_MT_FRAME_SIZE,1920,1080);
    if (SUCCEEDED(generator.Initialize(type.get()))) return 11;
    if (!TestAsyncState()) { puts("FAIL: asynchronous camera state"); return 13; }
    if (!TestRealAsyncReader()) { puts("FAIL: Media Foundation async callback integration"); return 14; }
    if (!TestCadence()) { puts("FAIL: frame pacing or timer release"); return 15; }
    puts("PASS: 2000 no-camera frames, NV12 black, no stale pixels, guard bytes intact, invalid sizes/strides/formats rejected. Hardware switching not tested.");
    return 0;
}
