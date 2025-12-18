using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace slot
{
    public class PlayerBase
    {
        private const int BALANCE_OFFSET = 0;
        private const int GUARD_OFFSET = 8;
        private const int KEY_OFFSET = 16;

        private const int PROTECTED_SIZE = 4096;
        private static readonly Random Rng = new Random();

        private static IntPtr memory = IntPtr.Zero;
        private static readonly object sync = new object();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_NOACCESS = 0x01;

        public PlayerBase()
        {
            Anticheat.Start();
            Allocate();
        }

        public static void ApplyPunishment()
        {
            lock (sync)
            {
                SetTrueBalance(0.0);
                //Anticheat.Log($"[PUNISHMENT APPLIED] Balance reset to 0.0 by integrity failure.");
            }
        }

        private static double GetTrueBalance()
        {
            AllowRead();

            long obfuscatedBalance = Marshal.ReadInt64(memory, BALANCE_OFFSET);
            long obfuscatedGuard = Marshal.ReadInt64(memory, GUARD_OFFSET);
            long currentKey = Marshal.ReadInt64(memory, KEY_OFFSET);

            ReArmTrap();

            long decryptedLong = obfuscatedBalance ^ currentKey;
            long decryptedGuard = obfuscatedGuard ^ currentKey;

            if (decryptedLong + currentKey != decryptedGuard)
            {
                //Anticheat.Log("[CRITICAL TAMPER] SSOT integrity check failed! Protected memory was changed externally.");
                ApplyPunishment();
                return 0.0;
            }

            return BitConverter.Int64BitsToDouble(decryptedLong);
        }

        private static void SetTrueBalance(double value)
        {
            long newKey = (long)Rng.NextDouble() * long.MaxValue;
            long longValue = BitConverter.DoubleToInt64Bits(value);

            long obfuscatedBalance = longValue ^ newKey;
            long guardValue = longValue + newKey;
            long obfuscatedGuard = guardValue ^ newKey;

            AllowWrite();
            Marshal.WriteInt64(memory, BALANCE_OFFSET, obfuscatedBalance);
            Marshal.WriteInt64(memory, GUARD_OFFSET, obfuscatedGuard);
            Marshal.WriteInt64(memory, KEY_OFFSET, newKey);
            ReArmTrap();
        }

        public double Balance
        {
            get
            {
                lock (sync)
                {
                    return GetTrueBalance();
                }
            }
        }

        public void Add(double amount)
        {
            lock (sync)
            {
                double currentBalance = GetTrueBalance();
                currentBalance += amount;
                SetTrueBalance(currentBalance);
            }
        }

        public void Set(double value)
        {
            lock (sync)
            {
                SetTrueBalance(value);
            }
        }

        private static void Allocate()
        {
            if (memory == IntPtr.Zero)
            {
                memory = VirtualAlloc(IntPtr.Zero, PROTECTED_SIZE, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                if (memory == IntPtr.Zero)
                    throw new Exception("Failed to allocate protected memory page.");

                SetTrueBalance(1000.0);
            }
        }

        public static IntPtr GetProtectedBase() => memory;
        public static uint GetProtectedSize() => PROTECTED_SIZE;

        public static bool IsProtectedAddress(IntPtr addr)
        {
            if (memory == IntPtr.Zero) return false;

            long pageStart = memory.ToInt64();
            long pageEnd = pageStart + PROTECTED_SIZE;
            long fault = addr.ToInt64();

            return fault >= pageStart && fault < pageEnd;
        }

        public static void ReArmTrap()
        {
            if (memory != IntPtr.Zero)
            {
                VirtualProtect(memory, PROTECTED_SIZE, PAGE_NOACCESS, out _);
            }
        }

        private static void AllowRead() => VirtualProtect(memory, PROTECTED_SIZE, PAGE_READWRITE, out _);
        private static void AllowWrite() => VirtualProtect(memory, PROTECTED_SIZE, PAGE_READWRITE, out _);
    }
}