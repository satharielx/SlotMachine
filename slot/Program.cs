using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media;

namespace slot
{
    public class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.Title = "Aurum | Fifteen Lines";
            var player = new PlayerBase();
            using var game = new SlotMachine(player);
            var music = new MediaPlayer();
            try
            {
                string soundtrack = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", "soundtrack.wav");
                if (File.Exists(soundtrack))
                {
                    music.Volume = 0.35;
                    music.Open(new Uri(soundtrack));
                    music.Play();
                }
                if (!Console.IsInputRedirected && !Console.IsOutputRedirected) game.RunGameLoop();
                ShowMenu();
                string command;
                while ((command = Console.ReadLine()) != null)
                {
                    command = command.Trim();
                    if (string.Equals(command, "END", StringComparison.OrdinalIgnoreCase) || string.Equals(command, "quit", StringComparison.OrdinalIgnoreCase)) break;
                    if (string.Equals(command, "play", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Console.IsInputRedirected || Console.IsOutputRedirected) Console.WriteLine("Interactive play requires a terminal.");
                        else game.RunGameLoop();
                        ShowMenu();
                    }
                    else if (string.Equals(command, "addBalance", StringComparison.OrdinalIgnoreCase))
                    {
                        player.Add(20);
                        Console.WriteLine($"Balance: ${player.Balance:N2}");
                    }
                    else if (string.Equals(command, "testAddress", StringComparison.OrdinalIgnoreCase))
                    {
                        using var process = Process.GetCurrentProcess();
                        Console.WriteLine($"PID: {process.Id}, balance address: 0x{PlayerBase.GetProtectedBase().ToInt64():X}");
                    }
                    else if (string.Equals(command, "status", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine(Anticheat.StatusText);
                        foreach (string line in Anticheat.ProcessLines) Console.WriteLine(line);
                    }
                    else if (command.StartsWith("volume", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = command.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && double.TryParse(parts[1], out double volume) && !double.IsNaN(volume) && volume >= 0 && volume <= 100)
                        {
                            music.Volume = volume / 100;
                            Console.WriteLine($"Volume: {volume:N0}%");
                        }
                        else Console.WriteLine("Use volume 0-100.");
                    }
                    else if (command.Length > 0) Console.WriteLine("Use play, addBalance, testAddress, status, volume 0-100 or END.");
                    Console.Write("aurum > ");
                }
            }
            finally
            {
                music.Close();
                Anticheat.Stop();
                Console.ResetColor();
            }
        }

        private static void ShowMenu()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n  A U R U M  /  FIFTEEN LINES\n");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("  play          Return to the game");
            Console.WriteLine("  testAddress   Balance address for the external test tool");
            Console.WriteLine("  status        Security summary and process permissions");
            Console.WriteLine("  addBalance    Add $20 practice credits");
            Console.WriteLine("  volume 0-100  Soundtrack volume");
            Console.WriteLine("  END           Close\n");
            Console.Write("aurum > ");
        }

        public static void PrintLine(string line) => Console.WriteLine(line);
        public static void PrintInline(string text) => Console.Write(text);
        public static void Clear() => Console.Clear();
    }
}
