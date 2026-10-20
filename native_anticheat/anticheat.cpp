// native_anticheat/anticheat.cpp
// Simple native anticheat helper (user-mode).
// Build (x64 recommended):
//   cl /EHsc /O2 anticheat.cpp advapi32.lib
// Run as Administrator for best results.

#include <windows.h>
#include <string>
#include <vector>
#include <thread>
#include <mutex>
#include <sstream>
#include <iostream>
#include <algorithm>
#include <iomanip>
#include <TlHelp32.h>

typedef LONG NTSTATUS;

struct Entry {
    DWORD pid;
    UINT64 address;
    SIZE_T size;
    bool active;
};

static std::vector<Entry> g_entries;
static std::mutex g_mutex;
static volatile bool g_running = true;
static const std::string PIPE_NAME = "\\\\.\\pipe\\SlotProtectPipe";

// Attempt to enable SeDebugPrivilege for the current process
bool EnableDebugPrivilege()
{
    HANDLE hToken = NULL;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &hToken))
        return false;

    TOKEN_PRIVILEGES tp;
    LUID luid;
    if (!LookupPrivilegeValueA(NULL, "SeDebugPrivilege", &luid)) {
        CloseHandle(hToken);
        return false;
    }

    tp.PrivilegeCount = 1;
    tp.Privileges[0].Luid = luid;
    tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;

    BOOL res = AdjustTokenPrivileges(hToken, FALSE, &tp, sizeof(tp), NULL, NULL);
    CloseHandle(hToken);
    return res && GetLastError() == ERROR_SUCCESS;
}

// Forward declarations
void ReapplyProtectionLoop(bool verbose);
void NamedPipeServer(bool verbose);
void HandleScanLoop(bool verbose);

void ReapplyProtectionLoop(bool verbose)
{
    while (g_running)
    {
        std::vector<Entry> copy;
        {
            std::lock_guard<std::mutex> lk(g_mutex);
            copy = g_entries;
        }

        for (auto &e : copy)
        {
            HANDLE h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, FALSE, e.pid);
            if (!h)
            {
                if (verbose) std::cerr << "[native] OpenProcess failed for PID " << e.pid << " (err " << GetLastError() << ")\n";
                continue;
            }

            DWORD oldProtect = 0;
            BOOL vp = VirtualProtectEx(h, (LPVOID)(uintptr_t)e.address, e.size, PAGE_NOACCESS, &oldProtect);
            if (!vp)
            {
                if (verbose) std::cerr << "[native] VirtualProtectEx(PAGE_NOACCESS) failed for PID " << e.pid << " addr 0x" << std::hex << e.address << " err " << std::dec << GetLastError() << "\n";
            }
            else
            {
                if (verbose) std::cerr << "[native] Set PAGE_NOACCESS for PID " << e.pid << " addr 0x" << std::hex << e.address << " old=0x" << oldProtect << std::dec << "\n";
            }

            MEMORY_BASIC_INFORMATION mbi;
            SIZE_T q = VirtualQueryEx(h, (LPCVOID)(uintptr_t)e.address, &mbi, sizeof(mbi));
            if (q == 0)
            {
                if (verbose) std::cerr << "[native] VirtualQueryEx failed for PID " << e.pid << " err " << GetLastError() << "\n";
            }
            else
            {
                DWORD currentProt = mbi.Protect;
                if (currentProt != PAGE_NOACCESS)
                {
                    if (verbose) std::cerr << "[native] Detected unexpected protection for PID " << e.pid << " addr 0x" << std::hex << e.address << " prot=0x" << currentProt << std::dec << "\n";
                    VirtualProtectEx(h, (LPVOID)(uintptr_t)e.address, e.size, PAGE_NOACCESS, &oldProtect);
                }
            }

            CloseHandle(h);
        }

        // extra: detect suspicious processes and attempt to suspend/terminate them
        PROCESSENTRY32 pe; pe.dwSize = sizeof(pe);
        HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap != INVALID_HANDLE_VALUE)
        {
            if (Process32First(snap, &pe))
            {
                do
                {
                    std::string name = pe.szExeFile;
                    std::transform(name.begin(), name.end(), name.begin(), ::tolower);
                    const char* suspicious[] = { "cheatengine", "x64dbg", "x32dbg", "ollydbg", "ida", "ida64", "processhacker", "procexp", "scylla" };
                    for (auto s : suspicious)
                    {
                        if (name.find(s) != std::string::npos)
                        {
                            if (verbose) std::cerr << "[native] Suspicious process found: " << pe.szExeFile << " (PID " << pe.th32ProcessID << ")\n";
                            HANDLE hp = OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_TERMINATE | PROCESS_QUERY_INFORMATION, FALSE, pe.th32ProcessID);
                            if (hp)
                            {
                                // try to suspend using NtSuspendProcess if available
                                HMODULE ntdll = GetModuleHandleA("ntdll.dll");
                                if (ntdll)
                                {
                                    typedef NTSTATUS (NTAPI* NtSuspendProcessType)(HANDLE);
                                    NtSuspendProcessType NtSuspendProcess = (NtSuspendProcessType)GetProcAddress(ntdll, "NtSuspendProcess");
                                    if (NtSuspendProcess)
                                    {
                                        NtSuspendProcess(hp);
                                        if (verbose) std::cerr << "[native] Suspended " << pe.szExeFile << "\n";
                                    }
                                    else
                                    {
                                        TerminateProcess(hp, 1);
                                        if (verbose) std::cerr << "[native] Terminated " << pe.szExeFile << "\n";
                                    }
                                }
                                CloseHandle(hp);
                            }
                            break;
                        }
                    }
                } while (Process32Next(snap, &pe));
            }
            CloseHandle(snap);
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(400));
    }
}

// Handle scanning using NtQuerySystemInformation to detect remote handles to registered PIDs
void HandleScanLoop(bool verbose)
{
    HMODULE ntdll = GetModuleHandleA("ntdll.dll");
    if (!ntdll)
    {
        if (verbose) std::cerr << "[native] Failed to get ntdll handle\n";
        return;
    }

    typedef NTSTATUS(WINAPI *NtQuerySysInfo_t)(ULONG, PVOID, ULONG, PULONG);
    auto NtQuerySysInfo = (NtQuerySysInfo_t)GetProcAddress(ntdll, "NtQuerySystemInformation");
    if (!NtQuerySysInfo)
    {
        if (verbose) std::cerr << "[native] Failed to get NtQuerySystemInformation\n";
        return;
    }

    const ULONG SystemHandleInformation = 16;

    while (g_running)
    {
        ULONG bufferSize = 0x10000;
        PBYTE buffer = nullptr;
        NTSTATUS status = 0;
        ULONG retLen = 0;

        for (int attempt = 0; attempt < 6; ++attempt)
        {
            buffer = (PBYTE)malloc(bufferSize);
            if (!buffer) break;
            status = NtQuerySysInfo(SystemHandleInformation, buffer, bufferSize, &retLen);
            if (status == 0) break; // success
            free(buffer);
            buffer = nullptr;
            if (status == 0xC0000004) // STATUS_INFO_LENGTH_MISMATCH
            {
                bufferSize = max(bufferSize * 2, retLen);
                continue;
            }
            else break;
        }

        if (!buffer)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(1000));
            continue;
        }

        ULONG handleCount = *(ULONG*)buffer;
        PBYTE current = buffer + sizeof(ULONG);
        // entry layout: ULONG ProcessId; UCHAR ObjectTypeNumber; UCHAR Flags; USHORT Handle; PVOID Object; ULONG GrantedAccess;
        SIZE_T entrySize = sizeof(ULONG) + 1 + 1 + 2 + sizeof(void*) + sizeof(ULONG);

        for (ULONG i = 0; i < handleCount; ++i)
        {
            PBYTE entryPtr = current + i * entrySize;
            if ((entryPtr + entrySize) > (buffer + bufferSize)) break;

            ULONG ownerPid = *(ULONG*)(entryPtr);
            UCHAR objectType = *(UCHAR*)(entryPtr + 4);
            UCHAR flags = *(UCHAR*)(entryPtr + 5);
            USHORT handleValue = *(USHORT*)(entryPtr + 6);
            uintptr_t objPtr = 0;
            ULONG grantedAccess = 0;

            if (sizeof(void*) == 8)
            {
                objPtr = *(uintptr_t*)(entryPtr + 8);
                grantedAccess = *(ULONG*)(entryPtr + 16);
            }
            else
            {
                objPtr = *(uintptr_t*)(entryPtr + 8);
                grantedAccess = *(ULONG*)(entryPtr + 12);
            }

            // check if any registered entry matches target process
            bool interesting = (grantedAccess & (PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION)) != 0;
            if (!interesting) continue;

            // duplicate handle into our process
            HANDLE ownerProc = OpenProcess(PROCESS_DUP_HANDLE, FALSE, ownerPid);
            if (!ownerProc) continue;

            HANDLE dup = NULL;
            HANDLE currentProc = GetCurrentProcess();
            BOOL ok = DuplicateHandle(ownerProc, (HANDLE)(uintptr_t)handleValue, currentProc, &dup, 0, FALSE, DUPLICATE_SAME_ACCESS);
            if (ok && dup)
            {
                DWORD targetPid = 0;
                // use GetProcessId to see which process the handle refers to
                typedef DWORD (WINAPI *GetProcessId_t)(HANDLE);
                GetProcessId_t gp = (GetProcessId_t)GetProcAddress(GetModuleHandleA("kernel32.dll"), "GetProcessId");
                if (gp) targetPid = gp(dup);

                if (targetPid != 0)
                {
                    // check against registered entries
                    std::lock_guard<std::mutex> lk(g_mutex);
                    for (auto &e : g_entries)
                    {
                        if (e.pid == targetPid)
                        {
                            std::cerr << "[native] Detected external handle to protected PID " << targetPid << " from PID " << ownerPid << " handle 0x" << std::hex << handleValue << std::dec << "\n";
                            // Mitigation: reapply PAGE_NOACCESS on protected range
                            HANDLE ht = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, FALSE, e.pid);
                            if (ht)
                            {
                                DWORD old = 0;
                                VirtualProtectEx(ht, (LPVOID)(uintptr_t)e.address, e.size, PAGE_NOACCESS, &old);
                                CloseHandle(ht);
                            }
                            // Optionally terminate owner process (best-effort)
                            HANDLE hOwnerTerm = OpenProcess(PROCESS_TERMINATE, FALSE, ownerPid);
                            if (hOwnerTerm)
                            {
                                TerminateProcess(hOwnerTerm, 1);
                                CloseHandle(hOwnerTerm);
                            }
                        }
                    }
                }

                CloseHandle(dup);
            }

            CloseHandle(ownerProc);
        }

        free(buffer);
        std::this_thread::sleep_for(std::chrono::milliseconds(400));
    }
}

void NamedPipeServer(bool verbose)
{
    while (g_running)
    {
        HANDLE hPipe = CreateNamedPipeA(
            PIPE_NAME.c_str(),
            PIPE_ACCESS_INBOUND | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
            PIPE_UNLIMITED_INSTANCES,
            4096, 4096,
            0, nullptr);

        if (hPipe == INVALID_HANDLE_VALUE)
        {
            std::cerr << "[native] CreateNamedPipe failed: " << GetLastError() << "\n";
            std::this_thread::sleep_for(std::chrono::seconds(1));
            continue;
        }

        BOOL connected = ConnectNamedPipe(hPipe, nullptr) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
        if (!connected)
        {
            CloseHandle(hPipe);
            continue;
        }

        char buf[1024]; DWORD read = 0;
        BOOL ok = ReadFile(hPipe, buf, (DWORD)sizeof(buf) - 1, &read, nullptr);
        if (ok && read > 0)
        {
            buf[read] = 0;
            std::istringstream iss(buf);
            std::string cmd;
            iss >> cmd;
            if (_stricmp(cmd.c_str(), "REGISTER") == 0)
            {
                DWORD pid; unsigned long long addr; size_t size;
                if (iss >> pid >> std::hex >> addr >> std::dec >> size)
                {
                    Entry en; en.pid = pid; en.address = addr; en.size = size; en.active = true;
                    {
                        std::lock_guard<std::mutex> lk(g_mutex);
                        g_entries.push_back(en);
                    }
                    if (verbose) std::cerr << "[native] Registered pid=" << pid << " addr=0x" << std::hex << addr << " size=" << std::dec << size << "\n";
                }
            }
            else if (_stricmp(cmd.c_str(), "UNREGISTER") == 0)
            {
                DWORD pid; unsigned long long addr;
                if (iss >> pid >> std::hex >> addr)
                {
                    std::lock_guard<std::mutex> lk(g_mutex);
                    g_entries.erase(std::remove_if(g_entries.begin(), g_entries.end(), [&](const Entry &e) { return e.pid == pid && e.address == addr; }), g_entries.end());
                    if (verbose) std::cerr << "[native] Unregistered pid=" << pid << " addr=0x" << std::hex << addr << "\n";
                }
            }
            else if (_stricmp(cmd.c_str(), "QUIT") == 0)
            {
                g_running = false;
            }
        }

        FlushFileBuffers(hPipe);
        DisconnectNamedPipe(hPipe);
        CloseHandle(hPipe);
    }
}

int main(int argc, char** argv)
{
    bool verbose = true;
    std::cerr << "[native] anticheat helper starting\n";
    EnableDebugPrivilege();
    std::thread reap(ReapplyProtectionLoop, verbose);
    std::thread pipe(NamedPipeServer, verbose);
    std::thread scan(HandleScanLoop, verbose);

    std::cerr << "[native] Named pipe: " << PIPE_NAME << "\n";
    std::cerr << "[native] Send: REGISTER <pid> <addrHex> <sizeDec> or UNREGISTER <pid> <addrHex>\n";

    reap.join();
    pipe.join();
    scan.join();
    return 0;
}
