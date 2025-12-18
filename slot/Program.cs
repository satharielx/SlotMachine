using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Text.RegularExpressions;
using System.Linq.Expressions;
using System.Media;
using System.Windows;
using System.IO;
using System.Windows.Media;

namespace slot
{
    public class Program
    {
        static void Main(string[] args)
        {
            PlayerBase player = new PlayerBase();
            SlotMachine slot = new SlotMachine(player);

            // Initialize Soundtrack
            MediaPlayer mp = new MediaPlayer();
            try
            {
                mp.Open(new Uri(Path.Combine(Directory.GetCurrentDirectory(), "sounds", "soundtrack.wav")));
                mp.Play();
                mp.Volume = 0.5;
            }
            catch {  }

            string command = "";
            Console.WriteLine("--- SLOT GAME ---");
            Console.WriteLine("Commands: 'play' (Interactive UI), 'addBalance', 'volume <0-100>', 'END'");

            while ((command = Console.ReadLine()) != "END")
            {
                switch (command)
                {
                    case "play":

                        Clear();
                        slot.RunGameLoop();
                        break;

                    case "addBalance":
                        player.Add(20.00);
                        Console.WriteLine($"Player has ${player.Balance} in their account.");
                        break;

                    case "animationTest":
                        slot.AnimateRoll();
                        break;
                }
                if (command.StartsWith("volume"))
                {
                    string[] arguments = command.Split();
                    if (arguments.Length == 2)
                    {
                        mp.Volume = double.Parse(arguments[1]) / 100.0;
                        Program.PrintLine($"Volume set to {arguments[1]}%");
                    }
                }

                Thread.Sleep(1);
            }
        }

        public static void PrintLine(string line) => Console.WriteLine(line);
        public static void PrintInline(string smth) => Console.Write(smth);
        public static void Clear() => Console.Clear();
    }
}