using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace slot
{
    internal static class Anticheat
    {
        private static Thread _scanThread;
        private static volatile bool _running;
        private static int _myPid;

        // Simple blacklist by process name (exe filenames)
        private static readonly HashSet<string> BlacklistedProcesses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "cheatengine.exe",
                "cheatengine-x86_64.exe",
                "x64dbg.exe",
                "ollydbg.exe",
                "ida.exe",
                "scylla_x64.exe",
                "scylla_x86.exe"
            };

        // Handle scanner settings
        private const int SystemHandleInformation = 16;
        private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
        private const uint STATUS_SUCCESS = 0x00000000;

        // Access flags of interest
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;

        private const uint PROCESS_QUERY_INFO = 0x0400;
        private const uint PROCESS_DUP_HANDLE = 0x0040;

        private const uint DUPLICATE_SAME_ACCESS = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_HANDLE_INFORMATION
        {
            public int ProcessId;
            public byte ObjectTypeNumber;
            public byte Flags;
            public ushort Handle;
            public IntPtr Object;
            public uint GrantedAccess;
        }

        [DllImport("ntdll.dll")]
        private static extern uint NtQuerySystemInformation(
            int systemInformationClass,
            IntPtr systemInformation,
            int systemInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint desiredAccess,
            bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwOptions);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern int GetProcessId(IntPtr hProcess);

        public static void Start()
        {
            _myPid = Process.GetCurrentProcess().Id;

            if (!Helpers.EnableDebugPrivilege())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ANTICHEAT] Requires Administrator + SeDebugPrivilege. Exiting.");
                Console.ResetColor();
                Environment.Exit(-1);
            }

            if (_scanThread != null)
                return;

            _running = true;
            _scanThread = new Thread(ScanLoop)
            {
                IsBackground = true,
                Name = "AnticheatScanner"
            };
            _scanThread.Start();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[ANTICHEAT] Started (PID: {_myPid}) admin handle scanner.");
            Console.ResetColor();
        }

        public static void Stop()
        {
            _running = false;
        }

        private static void ScanLoop()
        {
            while (_running)
            {
                try
                {
                    ScanBlacklistedProcesses();
                    ScanSystemHandles();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[ANTICHEAT] Scan error: " + ex.Message);
                    Console.ResetColor();
                }

                Thread.Sleep(2000);
            }
        }

        // --- Layer 1: simple process blacklist --------------------------------

        private static void ScanBlacklistedProcesses()
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName + ".exe"; // ProcessName has no .exe
                    if (BlacklistedProcesses.Contains(name) && proc.Id != _myPid)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[ANTICHEAT] Blacklisted process detected: {name} (PID {proc.Id}).");
                        Console.ResetColor();
                        PlayerBase.ApplyPunishment();
                    }
                }
                catch
                {
                    // ignore access denied processes
                }
            }
        }

        // --- Layer 2: handle scanner ------------------------------------------

        private enum HandleCheckResult
        {
            NotOurs,
            IsOurs,
            CannotOpenProcess,
            CannotDuplicate,
            GetPidFailed
        }

        private static void ScanSystemHandles()
        {
            int length = 0x10000;
            IntPtr buffer = Marshal.AllocHGlobal(length);
            int returnLength;
            uint status;

            try
            {
                while ((status = NtQuerySystemInformation(
                    SystemHandleInformation,
                    buffer,
                    length,
                    out returnLength)) == STATUS_INFO_LENGTH_MISMATCH)
                {
                    Marshal.FreeHGlobal(buffer);
                    length = returnLength;
                    buffer = Marshal.AllocHGlobal(length);
                }

                if (status != STATUS_SUCCESS)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[ANTICHEAT] NtQuerySystemInformation failed: 0x{status:X8}");
                    Console.ResetColor();
                    return;
                }

                int handleCount = Marshal.ReadInt32(buffer);   // 4-byte count
                IntPtr currentPtr = buffer + 4;
                int entrySize = Marshal.SizeOf(typeof(SYSTEM_HANDLE_INFORMATION));

                for (int i = 0; i < handleCount; i++)
                {
                    var handleInfo =
                        Marshal.PtrToStructure<SYSTEM_HANDLE_INFORMATION>(currentPtr);
                    currentPtr += entrySize;

                    int ownerPid = handleInfo.ProcessId;
                    if (ownerPid == _myPid || ownerPid <= 4)
                        continue;

                    uint access = handleInfo.GrantedAccess;
                    bool hasRead = (access & PROCESS_VM_READ) != 0;
                    bool hasWrite = (access & PROCESS_VM_WRITE) != 0;
                    bool hasOp = (access & PROCESS_VM_OPERATION) != 0;
                    bool hasAll = (access & PROCESS_ALL_ACCESS) == PROCESS_ALL_ACCESS;

                    if (!hasRead && !hasWrite && !hasOp && !hasAll)
                        continue;

                    var res = IsHandlePointingToUs(ownerPid, handleInfo.Handle);
                    string name = GetProcessNameSafe(ownerPid);

                    if (res == HandleCheckResult.IsOurs)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine();
                        Console.WriteLine("[ANTICHEAT] External memory handle detected!");
                        Console.WriteLine($"  Source: {name} (PID {ownerPid})");
                        Console.WriteLine($"  Handle: 0x{handleInfo.Handle:X}");
                        Console.WriteLine($"  Access: 0x{access:X8} (READ={hasRead}, WRITE={hasWrite}, OP={hasOp}, ALL={hasAll})");
                        Console.ResetColor();

                        PlayerBase.ApplyPunishment();
                        return;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static HandleCheckResult IsHandlePointingToUs(int ownerPid, ushort handleValue)
        {
            IntPtr hOwner = IntPtr.Zero;
            IntPtr hDup = IntPtr.Zero;

            try
            {
                hOwner = OpenProcess(
                    PROCESS_QUERY_INFO | PROCESS_DUP_HANDLE,
                    false,
                    ownerPid);

                if (hOwner == IntPtr.Zero)
                    return HandleCheckResult.CannotOpenProcess;

                if (!DuplicateHandle(
                        hOwner,
                        new IntPtr(handleValue),
                        Process.GetCurrentProcess().Handle,
                        out hDup,
                        0,
                        false,
                        DUPLICATE_SAME_ACCESS))
                {
                    return HandleCheckResult.CannotDuplicate;
                }

                int targetPid = GetProcessId(hDup);
                if (targetPid == 0)
                    return HandleCheckResult.GetPidFailed;

                return targetPid == _myPid
                    ? HandleCheckResult.IsOurs
                    : HandleCheckResult.NotOurs;
            }
            finally
            {
                if (hDup != IntPtr.Zero) CloseHandle(hDup);
                if (hOwner != IntPtr.Zero) CloseHandle(hOwner);
            }
        }

        private static string GetProcessNameSafe(int pid)
        {
            try { return Process.GetProcessById(pid).ProcessName; }
            catch { return "unknown"; }
        }
    }
}
