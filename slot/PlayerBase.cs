using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using System.Net;

namespace slot
{
    public class PlayerBase
    {
        private IntPtr balanceMemory;
        private double previous = 0.0;
        private double current = 0.0;
        private AnticheatTypes.Keychain previosKey = new AnticheatTypes.Keychain();
        private Anticheat anticheat = new Anticheat();
        private AnticheatTypes.Keychain HashKeyStroke;
        private bool changingBalance = false;
         

        public string Username { get; private set; }

        public PlayerBase()
        {
            
            AllocateMemory();
            HashKeyStroke = Balance.ToKeychain<double>();
            Thread antiCheating = new Thread(OnChange);
            anticheat.AddProcedure("BalanceCheck", antiCheating);
        }

        public double Balance
        {
            get
            {
                return
                     GetBalance();
            }

        }

        private double GetBalance() {
            if (balanceMemory == IntPtr.Zero)
            {
                
                AllocateMemory();
                return Marshal.PtrToStructure<double>(balanceMemory);
            }
            else return Marshal.PtrToStructure<double>(balanceMemory);
        }

        private void AllocateMemory()
        {
            if (balanceMemory == IntPtr.Zero)
            {
                balanceMemory = Marshal.AllocHGlobal(Marshal.SizeOf<double>());
                Marshal.StructureToPtr(current, balanceMemory, false);
                previous = Marshal.PtrToStructure<double>(balanceMemory);
                if (VirtualProtect(balanceMemory, (UIntPtr)Marshal.SizeOf<double>(), MemoryProtection.Readonly, out _))
                {

                    Program.PrintLine($"[*] SAC: Memory protected successfully!");
                    IsMemoryProtected();
                }
                else {
                    Marshal.GetLastWin32Error();
                }
                
            }
            
        }

        public void IsMemoryProtected() {
            MEMORY_BASIC_INFORMATION mbi;

            if (VirtualQuery(balanceMemory, out mbi, (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))))

            {

                if (mbi.Protect == 0x04)

                {

                    Console.WriteLine("MEMORY CHECK PASSED! BALANCE VARIABLE IS PROTECTED!");

                }

                else

                {

                    Console.WriteLine("MEMORY CHECK PASSED! BALANCE VARIABLE IS NOT PROTECTED!");

                }

            }
        }

        public void PrintProcedures() => anticheat.ShowAllProcedures();

        public void UpdateBalance(double amount)
        {
            changingBalance = true;
            VirtualProtect(balanceMemory, (UIntPtr)Marshal.SizeOf<double>(), MemoryProtection.ReadWrite, out _);
            Thread.Sleep(1000);
            double old = GetBalance();
            previous = old;
            current += old + amount;
            
            Marshal.StructureToPtr(current, balanceMemory, false);
            HashKeyStroke = Balance.ToKeychain<double>();
            
            changingBalance = false;
            current = 0.0;
            VirtualProtect(balanceMemory, (UIntPtr)Marshal.SizeOf<double>(), MemoryProtection.Readonly, out _);
        }

        public void SetBalance(double amount)
        {
            VirtualProtect(balanceMemory, (UIntPtr)Marshal.SizeOf<double>(), MemoryProtection.ReadWrite, out _);
            Thread.Sleep(1000);
            double old = GetBalance();
            previous = old;
            current = amount;

            Marshal.StructureToPtr(current, balanceMemory, false);
            HashKeyStroke = Balance.ToKeychain<double>();
            old = GetBalance();
            previous = old;
            changingBalance = false;
            current = 0.0;
            VirtualProtect(balanceMemory, (UIntPtr)Marshal.SizeOf<double>(), MemoryProtection.Readonly, out _);
        }

        public override string ToString()
        {
            return $"Player has ${Balance} in their account.\nHash key of value: {HashKeyStroke.hash}";
        }

        private void OnChange()
        {
            while (true)
            {
                
                if (balanceMemory == IntPtr.Zero)
                {
                    Program.PrintLine($"[*] SAC: Error - Invalid memory pointer.");
                    Marshal.FreeHGlobal(balanceMemory);
                    return;
                }

                double temp = Marshal.PtrToStructure<double>(balanceMemory);
                AnticheatTypes.Keychain tempKey = temp.ToKeychain<double>();

                if (HashKeyStroke.hash != tempKey.hash)
                {
                   
                        Program.PrintLine($"[*] SAC: A change in address ({balanceMemory.ToInt64():X2}) balance value detected. Previous value was set.");
                        SetBalance(0.0);
                        Program.PrintLine(this.ToString());
                    
                    
                }

                Thread.Sleep(1000);
            }
        }

        [DllImport("kernel32.dll")]

        static extern bool VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);


        [DllImport("kernel32.dll")]

        static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, MemoryProtection flNewProtect, out uint lpflOldProtect);


        [StructLayout(LayoutKind.Sequential)]

        struct MEMORY_BASIC_INFORMATION

        {

            public IntPtr BaseAddress;

            public IntPtr AllocationBase;

            public uint AllocationProtect;

            public uint RegionSize;

            public uint State;

            public uint Protect;

            public uint Type;

        }

        [Flags]
        private enum MemoryProtection : uint
        {
            NoAccess = 0x01,
            Readonly = 0x04,  
            ReadWrite = 0x08, 
            ExecuteRead = 0x20,
            ExecuteReadWrite = 0x40
        }

        ~PlayerBase()
        {
            Marshal.FreeHGlobal(balanceMemory);
        }
    }
}

