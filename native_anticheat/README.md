native_anticheat helper

Build (MSVC x64):
  Open "x64 Native Tools Command Prompt for VS" and run:
    cl /EHsc /O2 anticheat.cpp advapi32.lib

Run:
  Run the produced anticheat.exe as Administrator for best results.

Description:
  - Listens on named pipe \\\.\pipe\SlotProtectPipe for messages:
      REGISTER <pid> <addrHex> <size>
      UNREGISTER <pid> <addrHex>
  - For registered ranges, repeatedly calls VirtualProtectEx(..., PAGE_NOACCESS) to make the pages non-readable for other processes.
  - Attempts to detect suspicious tools and suspend/terminate them (best effort).

Notes:
  - This user-mode helper improves robustness compared to pure managed code but cannot defend against kernel-level attackers or signed drivers with SeDebugPrivilege.
  - Use responsibly and test on controlled machines.
