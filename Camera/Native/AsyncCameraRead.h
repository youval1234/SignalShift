// One outstanding physical-camera read and at most one completed frame.
// The callback owns this state, never the reader or frame generator.
#pragma once
struct CameraReadResult
{
    HRESULT status = S_OK;
    DWORD flags = 0;
    bool timedOut = false;
    wil::com_ptr_nothrow<IMFSample> sample;
};
class AsyncCameraRead
{
public:
    static constexpr ULONGLONG TimeoutMs = 1500;
    static constexpr ULONGLONG MaximumFrameAgeMs = 500;
    bool Begin(ULONGLONG now)
    {
        winrt::slim_lock_guard lock(m_lock);
        if (m_stopped || m_pending || m_sample || FAILED(m_status)) return false;
        m_pending = true; m_requestedAt = now; return true;
    }
    void Complete(HRESULT status, DWORD flags, IMFSample* sample, ULONGLONG now)
    {
        winrt::slim_lock_guard lock(m_lock);
        if (m_stopped || !m_pending) return;
        m_pending = false; m_status = status; m_flags = flags;
        if (SUCCEEDED(status) && !(flags & TerminalFlags)) m_sample = sample;
        m_receivedAt = now;
    }
    CameraReadResult Take(ULONGLONG now)
    {
        winrt::slim_lock_guard lock(m_lock);
        CameraReadResult result;
        result.status = m_status; result.flags = m_flags;
        result.timedOut = m_pending && now - m_requestedAt >= TimeoutMs;
        if (m_sample && now - m_receivedAt <= MaximumFrameAgeMs) result.sample.attach(m_sample.detach());
        else m_sample.reset();
        m_flags = 0;
        return result;
    }
    void Stop()
    {
        winrt::slim_lock_guard lock(m_lock);
        m_stopped = true; m_pending = false; m_status = MF_E_SHUTDOWN; m_sample.reset();
    }
    static constexpr DWORD TerminalFlags = MF_SOURCE_READERF_ERROR | MF_SOURCE_READERF_ENDOFSTREAM | MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED;
private:
    winrt::slim_mutex m_lock;
    bool m_pending = false, m_stopped = false;
    HRESULT m_status = S_OK;
    DWORD m_flags = 0;
    ULONGLONG m_requestedAt = 0, m_receivedAt = 0;
    wil::com_ptr_nothrow<IMFSample> m_sample;
};
struct CameraReaderCallback : winrt::implements<CameraReaderCallback, IMFSourceReaderCallback>
{
    explicit CameraReaderCallback(std::shared_ptr<AsyncCameraRead> state) : m_state(std::move(state)) {}
    IFACEMETHODIMP OnReadSample(HRESULT status, DWORD, DWORD flags, LONGLONG, IMFSample* sample) noexcept override
    {
        m_state->Complete(status, flags, sample, GetTickCount64()); return S_OK;
    }
    IFACEMETHODIMP OnEvent(DWORD, IMFMediaEvent*) noexcept override { return S_OK; }
    IFACEMETHODIMP OnFlush(DWORD) noexcept override { return S_OK; }
private:
    std::shared_ptr<AsyncCameraRead> m_state;
};
