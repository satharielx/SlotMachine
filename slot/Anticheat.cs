using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security.Principal;

namespace slot
{
    public static class Anticheat
    {
        // Synchronize access to log file to avoid interleaved writes from multiple threads
        private static readonly object LogLock = new object();
        private static string LogPath = "";

        // VEH and debugging detection use native Win32 APIs via P/Invoke
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr AddVectoredExceptionHandler(uint First, IntPtr Handler);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll")]
        private static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, out bool pbDebuggerPresent);

        // Constants used by VEH and memory protection calls
        private const uint EXCEPTION_ACCESS_VIOLATION = 0xC0000005;
        private const int EXCEPTION_CONTINUE_SEARCH = 0;
        private const int EXCEPTION_CONTINUE_EXECUTION = -1;
        private const uint PAGE_READWRITE = 0x04;

        // Minimal native structures required to inspect exception info coming from VEH
        [StructLayout(LayoutKind.Sequential)]
        private struct EXCEPTION_POINTERS
        {
            public IntPtr ExceptionRecord;
            public IntPtr ContextRecord;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EXCEPTION_RECORD
        {
            public uint ExceptionCode;
            public uint ExceptionFlags;
            public IntPtr ExceptionRecord;
            public IntPtr ExceptionAddress;
            public uint NumberParameters;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)]
            public IntPtr[] ExceptionInformation;
        }

        // Delegate signature for the vectored exception handler
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int VectoredExceptionHandler(IntPtr ExceptionInfo);
        private static VectoredExceptionHandler _handlerDelegate;
        private static volatile bool TrapTriggered = false;

        // Initialize anticheat subsystems: logging, VEH registration and background workers.
        // This method is intended to be idempotent and safe to call during startup.
        public static void Start()
        {
            try
            {
                LogPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SlotMachine", "anticheat.log");
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            }
            catch { LogPath = "anticheat.log"; }

            Log($"[START] ANTICHEAT STARTED — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            try
            {
                // Register a vectored exception handler. We keep a managed delegate reference
                // to prevent the GC from collecting the native callback pointer.
                _handlerDelegate = new VectoredExceptionHandler(ExceptionHandler);
                AddVectoredExceptionHandler(1, Marshal.GetFunctionPointerForDelegate(_handlerDelegate));
                Log("[OK] Memory access trap ARMED (VEH)");
            }
            catch (Exception ex)
            {
                // Don't let setup failure crash the app; log and continue in degraded mode.
                Log($"[ERROR] Setup failed: {ex.Message}");
            }

            // Background thread that re-applies PAGE_NOACCESS shortly after an allowed R/W
            new Thread(ReArmThread) { IsBackground = true }.Start();

            // Background worker that periodically checks for attached debuggers
            ThreadPool.QueueUserWorkItem(_ => DebuggerWatchdog());
        }

        // Vectored exception handler: unlocks our protected page when an access violation
        // targets the page we manage. After unlocking we signal a short-lived window for
        // the intended read/write to complete and then re-lock the page in the background.
        private static int ExceptionHandler(IntPtr ExceptionInfoPtr)
        {
            try
            {
                var pointers = Marshal.PtrToStructure<EXCEPTION_POINTERS>(ExceptionInfoPtr);
                var record = Marshal.PtrToStructure<EXCEPTION_RECORD>(pointers.ExceptionRecord);

                if (record.ExceptionCode == EXCEPTION_ACCESS_VIOLATION && record.NumberParameters >= 2)
                {
                    int accessType = (int)record.ExceptionInformation[0];
                    IntPtr faultAddr = record.ExceptionInformation[1];

                    // If the fault address is within our protected region, temporarily make it writable
                    if (PlayerBase.IsProtectedAddress(faultAddr))
                    {
                        VirtualProtect(PlayerBase.GetProtectedBase(), PlayerBase.GetProtectedSize(), PAGE_READWRITE, out _);

                        string type = accessType == 0 ? "READ" : "WRITE";
                        Log($"[VEH CAUGHT] Internal/External {type} → Protected SSOT @ 0x{faultAddr:X16}");

                        // Signal the re-arm thread to restore protection after a brief delay
                        TrapTriggered = true;

                        // Ask the OS to retry the faulting instruction now that memory is writable
                        return EXCEPTION_CONTINUE_EXECUTION;
                    }
                }
            }
            catch
            {
                // If anything unexpected happens while handling the exception, allow normal
                // exception dispatching to continue so the process behavior remains predictable.
                return EXCEPTION_CONTINUE_SEARCH;
            }

            return EXCEPTION_CONTINUE_SEARCH;
        }

        // Background thread that restores PAGE_NOACCESS after a short window where memory
        // was intentionally made writable. This reduces the attack surface (short window).
        private static void ReArmThread()
        {
            while (true)
            {
                if (TrapTriggered)
                {
                    Thread.Sleep(5);
                    TrapTriggered = false;
                    PlayerBase.ReArmTrap();
                }
                Thread.Sleep(10);
            }
        }

        // Periodically checks for local and remote debuggers and logs detections.
        // This is a best-effort detector and intentionally noisy for visibility during development.
        private static void DebuggerWatchdog()
        {
            while (true)
            {
                try
                {
                    if (IsDebuggerPresent() || Debugger.IsAttached)
                        Log("[DETECTED] Debugger attached");
                    if (CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, out bool dbg) && dbg)
                        Log("[DETECTED] Remote debugger");
                    Thread.Sleep(1000);
                }
                catch { Thread.Sleep(1000); }
            }
        }

        // Thread-safe logging helper. We deliberately swallow all exceptions to avoid
        // interfering with the host application's control flow in production environments.
        public static void Log(string msg)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}";
                lock (LogLock)
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
                Console.WriteLine(line);
            }
            catch { }
        }
    }
}