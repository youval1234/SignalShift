// Per-stream pacing; no timer is created during camera enumeration or while idle.
#pragma once
class FrameCadence
{
public:
    static constexpr LONGLONG Duration = 333333;
    void Wait()
    {
        const auto remaining = m_next - MFGetSystemTime();
        if (remaining <= 0) return;
        if (!m_timer) m_timer.reset(CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS));
        LARGE_INTEGER due{}; due.QuadPart = -std::min(remaining, Duration);
        if (m_timer && SetWaitableTimerEx(m_timer.get(), &due, 0, nullptr, nullptr, nullptr, 0))
            WaitForSingleObject(m_timer.get(), 40);
        else Sleep(static_cast<DWORD>((-due.QuadPart + 9999) / 10000));
    }
    void Delivered() { m_next = MFGetSystemTime() + Duration; }
    void Reset() { m_next = 0; m_timer.reset(); }
private:
    LONGLONG m_next = 0;
    wil::unique_handle m_timer;
};
