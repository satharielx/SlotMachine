using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static int Main(string[] args)
    {
        bool interactive = args.Length == 0;
        int result;
        try
        {
            if (interactive) args = PromptForTest();
            result = args == null ? 0 : Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            result = 1;
        }
        if (interactive && !Console.IsInputRedirected)
        {
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
        return result;
    }

    private static string[] PromptForTest()
    {
        var games = Process.GetProcessesByName("slot");
        try
        {
            if (games.Length == 0)
            {
                Console.WriteLine("Start the game first, then reopen this tool.");
                return null;
            }
            int pid;
            if (games.Length == 1)
            {
                pid = games[0].Id;
                Console.WriteLine($"Found game PID {pid}.");
            }
            else
            {
                foreach (var game in games) Console.WriteLine($"Game PID: {game.Id}");
                while (true)
                {
                    Console.Write("Choose game PID: ");
                    string input = Console.ReadLine();
                    if (input == null) return null;
                    if (int.TryParse(input, out pid) && Array.Exists(games, game => game.Id == pid)) break;
                    Console.WriteLine("Choose one of the listed PIDs.");
                }
            }
            string mode;
            while (true)
            {
                Console.Write("Test: [1] Attach [2] Read [3] Write [Q] Quit (default 1): ");
                string input = Console.ReadLine();
                if (input == null) return null;
                input = input.Trim().ToLowerInvariant();
                if (input == "q") return null;
                mode = input == "" || input == "1" || input == "attach" ? "attach" :
                    input == "2" || input == "read" ? "read" : input == "3" || input == "write" ? "write" : null;
                if (mode != null) break;
                Console.WriteLine("Choose 1, 2, 3, or Q.");
            }
            if (mode == "attach") return new[] { pid.ToString(), mode };
            Console.WriteLine("Enter testAddress in the game's command prompt to get its balance address.");
            Console.Write("Balance address (hex): ");
            string address = Console.ReadLine();
            if (address == null) return null;
            if (mode == "read") return new[] { pid.ToString(), mode, address.Trim() };
            Console.Write("Balance to attempt writing: ");
            string balance = Console.ReadLine();
            return balance == null ? null : new[] { pid.ToString(), mode, address.Trim(), balance.Trim() };
        }
        finally
        {
            foreach (var game in games) game.Dispose();
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int pid) ||
            (args[1] != "attach" && args[1] != "read" && args[1] != "write"))
        {
            Console.WriteLine("Usage: SlotAnticheatTest <pid> attach|read|write [hex-address] [balance]");
            Console.WriteLine("In the game command prompt, enter testAddress to get its PID and balance address.");
            return 1;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            using var target = Process.GetProcessById(pid);
            string path = target.MainModule.FileName;
            if (!string.Equals(Path.GetFileName(path), "slot.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The target must be slot.exe.");
            var metadata = System.Reflection.AssemblyName.GetAssemblyName(path);
            if (metadata.Name != "slot" || FileVersionInfo.GetVersionInfo(path).OriginalFilename != "slot.exe")
                throw new InvalidOperationException("The target is not the expected game executable.");

            IntPtr address = IntPtr.Zero;
            double balance = 0;
            if (args[1] != "attach")
            {
                if (args.Length < 3) throw new ArgumentException("A hexadecimal balance address is required.");
                string value = args[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[2].Substring(2) : args[2];
                if (!long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed) || parsed <= 0)
                    throw new ArgumentException("Invalid hexadecimal address.");
                address = new IntPtr(parsed);
            }
            if (args[1] == "write" && (args.Length != 4 || !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out balance) ||
                double.IsNaN(balance) || double.IsInfinity(balance) || balance < 0))
                throw new ArgumentException("Supply a finite, nonnegative balance.");

            uint access = args[1] == "write" ? 0x38u : 0x10u;
            handle = OpenProcess(access, false, pid);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot attach. Run the tool at the same elevation as the game.");
            Console.WriteLine($"Attached to game PID {pid} with access 0x{access:X}. Test process PID: {Process.GetCurrentProcess().Id}.");

            if (args[1] == "read")
            {
                byte[] bytes = new byte[24];
                bool success = ReadProcessMemory(handle, address, bytes, new UIntPtr(24), out UIntPtr read);
                int error = success ? 0 : Marshal.GetLastWin32Error();
                Console.WriteLine($"Read: {success}, bytes: {read.ToUInt64()}, Windows error: {error}");
                if (success && read.ToUInt64() == 24)
                {
                    long key = BitConverter.ToInt64(bytes, 16);
                    Console.WriteLine($"Decoded balance: {BitConverter.Int64BitsToDouble(BitConverter.ToInt64(bytes, 0) ^ key)}");
                }
            }
            else if (args[1] == "write")
            {
                byte[] bytes = BitConverter.GetBytes(balance);
                bool success = WriteProcessMemory(handle, address, bytes, new UIntPtr(8), out UIntPtr written);
                int error = success ? 0 : Marshal.GetLastWin32Error();
                Console.WriteLine($"Raw balance write: {success}, bytes: {written.ToUInt64()}, Windows error: {error}");
                Console.WriteLine("The protected page should block this write. If it succeeds, the balance integrity check should reject it.");
            }

            Console.WriteLine("Holding the handle for 10 seconds. Watch the game's anticheat output and balance.");
            Thread.Sleep(10000);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }
}
