// native_anticheat/anticheatlib.cpp
// Native DLL for lower-latency registration. Exports RegisterRange / UnregisterRange.
// Build with: cl /LD /O2 anticheatlib.cpp

#include <windows.h>
#include <vector>
#include <mutex>
#include <algorithm>
#include <string>
#include <iostream>

struct Entry { DWORD pid; UINT64 address; SIZE_T size; };
static std::vector<Entry> g_entries;
static std::mutex g_mutex;

extern "C" __declspec(dllexport) BOOL __cdecl RegisterRange(unsigned int pid, unsigned long long address, size_t size, const char* hexHmac)
{
    // Note: HMAC verification omitted for brevity — user should implement a secure secret exchange.
    Entry e; e.pid = pid; e.address = address; e.size = size;
    {
        std::lock_guard<std::mutex> lk(g_mutex);
        g_entries.push_back(e);
    }
    return TRUE;
}

extern "C" __declspec(dllexport) BOOL __cdecl UnregisterRange(unsigned int pid, unsigned long long address, const char* hexHmac)
{
    std::lock_guard<std::mutex> lk(g_mutex);
    g_entries.erase(std::remove_if(g_entries.begin(), g_entries.end(), [&](const Entry& en) { return en.pid == pid && en.address == address; }), g_entries.end());
    return TRUE;
}

// Optional: background thread to enforce protections could be added here for in-process enforcement.
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    return TRUE;
}
