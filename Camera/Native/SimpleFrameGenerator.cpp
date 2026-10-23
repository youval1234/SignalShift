// SignalShift frame adapter. No frame queue or retained frame history.
#include "pch.h"
HRESULT SimpleFrameGenerator::Initialize(IMFMediaType* type)
{
    RETURN_IF_FAILED(MFGetAttributeSize(type, MF_MT_FRAME_SIZE, &m_width, &m_height));
    GUID subtype{}; RETURN_IF_FAILED(type->GetGUID(MF_MT_SUBTYPE, &subtype));
    RETURN_HR_IF(MF_E_INVALIDMEDIATYPE, subtype != MFVideoFormat_NV12 || m_width != 1280 || m_height != 720);
    m_type = type;
    return S_OK;
}
void SimpleFrameGenerator::Close()
{
    m_reader.reset();
    if (m_source) { m_source->Shutdown(); m_source.reset(); }
}
std::wstring SimpleFrameGenerator::ReadSelection()
{
    // Selection is a bounded UTF-16 string with a fixed binary magic/version.
    wil::unique_hfile file(CreateFileW(m_config.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
    if (!file) return {};
    DWORD size = GetFileSize(file.get(), nullptr);
    if (size < 8 || size > 8192 || size % 2) return {};
    wchar_t buffer[4096]{}; DWORD read = 0;
    if (!ReadFile(file.get(), buffer, size, &read, nullptr) || read != size || buffer[0] != 0x5353 || buffer[1] != 1 || buffer[size/2-1] != 0) return {};
    const auto count = wcsnlen_s(buffer + 2, size/2-2);
    if (!count || count >= size/2-2) return {};
    std::wstring id(buffer + 2, count);
    if (id.find(VIRTUALCAMERAMEDIASOURCE_CLSID) != std::wstring::npos) return {};
    return id;
}
HRESULT SimpleFrameGenerator::Open(const std::wstring& id)
{
    Close();
    wil::com_ptr_nothrow<IMFAttributes> attrs;
    RETURN_IF_FAILED(MFCreateAttributes(&attrs, 2));
    RETURN_IF_FAILED(attrs->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID));
    RETURN_IF_FAILED(attrs->SetString(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, id.c_str()));
    RETURN_IF_FAILED(MFCreateDeviceSource(attrs.get(), &m_source));
    RETURN_IF_FAILED(MFCreateAttributes(&attrs, 2));
    RETURN_IF_FAILED(attrs->SetUINT32(MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, TRUE));
    RETURN_IF_FAILED(attrs->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE));
    RETURN_IF_FAILED(MFCreateSourceReaderFromMediaSource(m_source.get(), attrs.get(), &m_reader));
    RETURN_IF_FAILED(m_reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, FALSE));
    RETURN_IF_FAILED(m_reader->SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM, TRUE));
    // Request a stable virtual format; let the native video processor resize/convert.
    wil::com_ptr_nothrow<IMFMediaType> output;
    RETURN_IF_FAILED(MFCreateMediaType(&output));
    RETURN_IF_FAILED(output->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
    RETURN_IF_FAILED(output->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12));
    RETURN_IF_FAILED(MFSetAttributeSize(output.get(), MF_MT_FRAME_SIZE, m_width, m_height));
    RETURN_IF_FAILED(m_reader->SetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, nullptr, output.get()));
    return S_OK;
}
HRESULT SimpleFrameGenerator::CreateFrame(BYTE* destination, DWORD length, LONG pitch, ULONG)
{
    RETURN_HR_IF(E_INVALIDARG, !destination || pitch < (LONG)m_width || length < (DWORD)pitch * m_height * 3 / 2);
    // Fail closed to black. Never retain the previous camera image after a switch,
    // unplug, disabled permission, stop, or failed negotiation.
    for (UINT32 y=0; y<m_height; ++y) memset(destination + y*pitch, 16, m_width);
    for (UINT32 y=0; y<m_height/2; ++y) memset(destination + (m_height+y)*pitch, 128, m_width);
    const auto now = GetTickCount64();
    if (now - m_lastConfig >= 100 || !m_lastConfig)
    {
        m_lastConfig = now;
        auto next = ReadSelection();
        if (next != m_selected) { Close(); m_selected = std::move(next); m_retry = 0; }
    }
    if (m_selected.empty()) { Close(); return S_OK; }
    if (!m_reader)
    {
        if (now < m_retry) return S_OK;
        m_retry = now + 2000;
        if (FAILED(Open(m_selected))) { Close(); return S_OK; }
    }
    wil::com_ptr_nothrow<IMFSample> sample;
    DWORD flags = 0; LONGLONG stamp = 0;
    HRESULT hr = m_reader->ReadSample(MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, nullptr, &flags, &stamp, &sample);
    if (FAILED(hr) || (flags & (MF_SOURCE_READERF_ERROR | MF_SOURCE_READERF_ENDOFSTREAM | MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED))) { Close(); m_retry = now + 2000; return S_OK; }
    if (!sample) return S_OK;
    wil::com_ptr_nothrow<IMFMediaBuffer> buffer;
    if (FAILED(sample->ConvertToContiguousBuffer(&buffer))) return S_OK;
    BYTE* pixels = nullptr; DWORD size = 0;
    if (FAILED(buffer->Lock(&pixels, nullptr, &size))) return S_OK;
    // SourceReader's contiguous NV12 output must have the negotiated geometry.
    // A padded output is copied using its actual media-type stride.
    wil::com_ptr_nothrow<IMFMediaType> current;
    LONG sourcePitch = (LONG)m_width;
    UINT32 width=0, height=0, stride=0; GUID subtype{};
    bool valid = SUCCEEDED(m_reader->GetCurrentMediaType(MF_SOURCE_READER_FIRST_VIDEO_STREAM, &current)) &&
        SUCCEEDED(MFGetAttributeSize(current.get(), MF_MT_FRAME_SIZE, &width, &height)) && width==m_width && height==m_height &&
        SUCCEEDED(current->GetGUID(MF_MT_SUBTYPE, &subtype)) && subtype==MFVideoFormat_NV12;
    if (valid && SUCCEEDED(current->GetUINT32(MF_MT_DEFAULT_STRIDE, &stride))) sourcePitch=(LONG)stride;
    valid = valid && sourcePitch >= (LONG)m_width && size >= (DWORD)sourcePitch*m_height*3/2;
    if (valid) for (UINT32 y=0; y<m_height*3/2; ++y) memcpy(destination+y*pitch, pixels+y*sourcePitch, m_width);
    buffer->Unlock();
    return S_OK;
}
