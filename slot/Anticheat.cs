using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Threading;

namespace slot
{
    internal static class Anticheat
    {
        private static Thread _scanThread;
        private static volatile bool _running;
        private static int _myPid;
        public static bool HasDebugPrivilege { get; private set; }
        private static volatile string _statusText = "Watching for balance changes.";
        private static HashSet<int> _activeDetections = new HashSet<int>();
        private static int _detectionCount;
        private static int _balanceTamperCount;
        private static volatile string[] _processLines = new string[0];
        public static IReadOnlyList<string> ProcessLines => Array.AsReadOnly(_processLines);
        public static string StatusText => Volatile.Read(ref _balanceTamperCount) == 0 ? _statusText :
            $"Balance changed outside the game. Reset to $0. ({Volatile.Read(ref _balanceTamperCount)})";

        internal static void ReportBalanceTampering()
        {
            Interlocked.Increment(ref _balanceTamperCount);
        }

        // Handle scanner settings
        private const int SystemHandleInformation = 64;
        private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
        private const uint STATUS_SUCCESS = 0x00000000;

        // Access flags of interest
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_HANDLE_INFORMATION
        {
            public IntPtr Object;
            public UIntPtr ProcessId;
            public UIntPtr Handle;
            public uint GrantedAccess;
            public ushort CreatorBackTraceIndex;
            public ushort ObjectTypeIndex;
            public uint HandleAttributes;
            public uint Reserved;
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
        private static extern bool CloseHandle(IntPtr hObject);

        public static void Start()
        {
            if (_scanThread != null)
                return;

            using (var process = Process.GetCurrentProcess()) _myPid = process.Id;
            HasDebugPrivilege = Helpers.EnableDebugPrivilege();

            _running = true;
            _scanThread = new Thread(ScanLoop)
            {
                IsBackground = true,
                Name = "AnticheatScanner"
            };
            _scanThread.Start();

        }

        public static void Stop()
        {
            _running = false;
            if (_scanThread != null && _scanThread.Join(1500)) _scanThread = null;
        }

        private static void ScanLoop()
        {
            while (_running)
            {
                try
                {
                    PlayerBase.VerifyBalanceIntegrity();
                    ScanSystemHandles();
                }
                catch (Exception ex)
                {
                    _statusText = "Couldn't check connected apps: " + ex.Message;
                }

                Thread.Sleep(250);
            }
        }

        private static void ScanSystemHandles()
        {
            int length = 0x10000;
            IntPtr buffer = Marshal.AllocHGlobal(length);
            int returnLength;
            uint status;

            using var ownProcess = Process.GetCurrentProcess();
            IntPtr ownHandle = ownProcess.Handle;
            try
            {
                while ((status = NtQuerySystemInformation(
                    SystemHandleInformation,
                    buffer,
                    length,
                    out returnLength)) == STATUS_INFO_LENGTH_MISMATCH)
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                    length = Math.Max(checked(length * 2), returnLength);
                    buffer = Marshal.AllocHGlobal(length);
                }

                if (status != STATUS_SUCCESS)
                {
                    _statusText = $"Couldn't check connected apps (0x{status:X8}).";
                    return;
                }

                ulong count = unchecked((ulong)Marshal.ReadIntPtr(buffer).ToInt64());
                int headerSize = IntPtr.Size * 2;
                IntPtr currentPtr = buffer + headerSize;
                int entrySize = Marshal.SizeOf(typeof(SYSTEM_HANDLE_INFORMATION));
                if (count > (ulong)((length - headerSize) / entrySize))
                    throw new InvalidOperationException("Invalid system handle snapshot.");
                int handleCount = (int)count;
                IntPtr processObject = IntPtr.Zero;
                for (int i = 0; i < handleCount; i++)
                {
                    var entry = Marshal.PtrToStructure<SYSTEM_HANDLE_INFORMATION>(currentPtr + i * entrySize);
                    if (entry.ProcessId.ToUInt64() == (ulong)_myPid && entry.Handle.ToUInt64() == unchecked((ulong)ownHandle.ToInt64()))
                    {
                        processObject = entry.Object;
                        break;
                    }
                }
                if (processObject == IntPtr.Zero)
                    throw new InvalidOperationException("Cannot identify the protected process in the handle snapshot.");
                var trustedOwners = new Dictionary<int, bool?>();
                var detections = new Dictionary<int, string>();
                var confirmed = new HashSet<int>();
                var ownerAccess = new SortedDictionary<int, uint>();
                var ownerHandles = new Dictionary<int, int>();

                for (int i = 0; i < handleCount; i++)
                {
                    var handleInfo =
                        Marshal.PtrToStructure<SYSTEM_HANDLE_INFORMATION>(currentPtr);
                    currentPtr += entrySize;

                    ulong rawPid = handleInfo.ProcessId.ToUInt64();
                    if (rawPid > int.MaxValue || handleInfo.Object != processObject) continue;
                    int ownerPid = (int)rawPid;
                    if (ownerPid == _myPid || ownerPid <= 4)
                        continue;

                    uint access = handleInfo.GrantedAccess;
                    bool hasRead = (access & PROCESS_VM_READ) != 0;
                    bool hasWrite = (access & PROCESS_VM_WRITE) != 0;
                    bool hasOp = (access & PROCESS_VM_OPERATION) != 0;
                    bool hasAll = (access & PROCESS_ALL_ACCESS) == PROCESS_ALL_ACCESS;

                    ownerAccess.TryGetValue(ownerPid, out uint previousAccess);
                    ownerAccess[ownerPid] = previousAccess | access;
                    ownerHandles.TryGetValue(ownerPid, out int previousCount);
                    ownerHandles[ownerPid] = previousCount + 1;

                    if (!trustedOwners.TryGetValue(ownerPid, out bool? trusted))
                    {
                        trusted = IsTrustedSystemProcess(ownerPid);
                        trustedOwners.Add(ownerPid, trusted);
                    }
                    string name = GetProcessNameSafe(ownerPid);

                    if (trusted != true)
                    {
                        detections[ownerPid] = $"{name} (PID {ownerPid})";
                        if (trusted == false) confirmed.Add(ownerPid);
                    }
                }
                var lines = new List<string>();
                foreach (var owner in ownerAccess)
                {
                    bool? trusted = trustedOwners[owner.Key];
                    string trust = trusted == true ? "recognized" : trusted == false ? "other app" : "not verified";
                    lines.Add($"{GetProcessNameSafe(owner.Key)} ({owner.Key}) - {trust}");
                    lines.Add(FormatAccess(owner.Value, ownerHandles[owner.Key]));
                }
                _processLines = lines.ToArray();
                PublishDetections(detections, confirmed);
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }

        private static string FormatAccess(uint access, int handles)
        {
            bool read = (access & PROCESS_VM_READ) != 0;
            bool write = (access & PROCESS_VM_WRITE) != 0;
            string permissions = read && write ? "Read + write" : read ? "Read only" : write ? "Write only" :
                (access & PROCESS_VM_OPERATION) != 0 ? "Memory operations" : "Attached";
            return $"{permissions} - {handles} {(handles == 1 ? "handle" : "handles")}";
        }

        private static void PublishDetections(Dictionary<int, string> detections, HashSet<int> confirmed)
        {
            foreach (int pid in confirmed)
            {
                if (!_activeDetections.Contains(pid))
                {
                    _detectionCount++;
                }
            }
            _activeDetections = new HashSet<int>(confirmed);
            if (detections.Count == 0)
            {
                _statusText = "No other apps connected.";
                return;
            }
            int firstPid = int.MaxValue;
            foreach (int pid in detections.Keys)
                if (firstPid == int.MaxValue || (confirmed.Contains(pid) && !confirmed.Contains(firstPid)) ||
                    (confirmed.Contains(pid) == confirmed.Contains(firstPid) && pid < firstPid)) firstPid = pid;
            string additional = detections.Count > 1 ? $" and {detections.Count - 1} more" : "";
            _statusText = $"Connected: {detections[firstPid]}{additional}";
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);

        private static bool? IsTrustedSystemProcess(int pid)
        {
            IntPtr process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                var path = new StringBuilder(32768);
                int size = path.Capacity;
                if (!QueryFullProcessImageName(process, 0, path, ref size)) return null;
                string fullPath = Path.GetFullPath(path.ToString());
                string name = Path.GetFileName(fullPath);
                if (string.Equals(name, "VsDebugConsole.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "DotNetDataCollector64.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "DotNetDataCollector32.exe", StringComparison.OrdinalIgnoreCase))
                    return IsTrustedVisualStudioTool(fullPath);
                if (!TrustedSystemProcesses.Contains(name)) return false;
                return string.Equals(Path.GetDirectoryName(fullPath), Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CloseHandle(process);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustFile
        {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string Path;
            public IntPtr Handle;
            public IntPtr Subject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData
        {
            public uint Size;
            public IntPtr Policy;
            public IntPtr Sip;
            public uint Ui;
            public uint Revocation;
            public uint Choice;
            public IntPtr File;
            public uint Action;
            public IntPtr State;
            public IntPtr Url;
            public uint Flags;
            public uint Context;
            public IntPtr Signature;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

        private static bool IsTrustedVisualStudioTool(string path)
        {
            string name = Path.GetFileName(path);
            if (!string.Equals(name, "VsDebugConsole.exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "DotNetDataCollector64.exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "DotNetDataCollector32.exe", StringComparison.OrdinalIgnoreCase)) return false;
            var file = new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = path };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            bool initialized = false;
            try
            {
                Marshal.StructureToPtr(file, pointer, false);
                initialized = true;
                var data = new TrustData { Size = (uint)Marshal.SizeOf(typeof(TrustData)), Ui = 2, Choice = 1, File = pointer, Flags = 0x1000 };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
                string originalName = Path.GetFileName(FileVersionInfo.GetVersionInfo(path).OriginalFilename);
                if (!string.Equals(originalName, name, StringComparison.OrdinalIgnoreCase)) return false;
                using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                return string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, false), "Microsoft Corporation", StringComparison.OrdinalIgnoreCase);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return false;
            }
            finally
            {
                if (initialized) Marshal.DestroyStructure<TrustFile>(pointer);
                Marshal.FreeHGlobal(pointer);
            }
        }

        private static readonly HashSet<string> TrustedSystemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "csrss.exe", "lsass.exe", "services.exe", "svchost.exe", "wininit.exe", "winlogon.exe", "WerFault.exe", "WerFaultSecure.exe", "conhost.exe", "taskmgr.exe", "MsMpEng.exe"
        };

        private static string GetProcessNameSafe(int pid)
        {
            try { using var process = Process.GetProcessById(pid); return process.ProcessName; }
            catch { return "unknown"; }
        }
    }
}
