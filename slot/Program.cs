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
            SoundPlayer sp = new SoundPlayer(Directory.GetCurrentDirectory() + @"\sounds\soundtrack.wav");
            MediaPlayer mp = new MediaPlayer();
            mp.Open(new Uri(Directory.GetCurrentDirectory() + @"\sounds\soundtrack.wav"));
            mp.Play();
            player.PrintProcedures();
            string command = "";

            while ((command = Console.ReadLine()) != "END")
            {
                switch (command)
                {
                    case "addBalance":
                        player.UpdateBalance(20.00);
                        Console.WriteLine(player.ToString());
                        break;
                    case "animationTest":
                        slot.AnimateRoll(10);
                        break;
                   
                }
                if (command.StartsWith("generateSlot"))
                {
                    string[] arguments = command.Split();
                    string sub = "";
                    double betAmount = 0.0;
                    if (arguments.Length < 2)
                    {
                        
                        sub = command.Length > 12 ? command.Substring(command.IndexOf("generateSlot") + 12) : "";
                        betAmount = (sub != "" && sub != string.Empty && sub != null) ? double.Parse(sub) : 0.0;
                        if (sub.Length < 1 || betAmount < 1) Program.PrintLine($"Usage: generateSlot <betAmount>");
                        else {
                            if (player.Balance >= betAmount)
                            {
                                Clear();
                                SlotReels generated = slot.GenerateSpin();
                                slot.AnimateRoll(5);
                                Clear();
                                slot.PrintSlotSpin(generated);
                                Program.PrintLine($"{new string(' ', 21)}Bet amount: ${betAmount};     Balance Available: ${player.Balance}      Coin Value: ${betAmount / 5} for each line.");
                                Program.PrintLine($"{new string(' ', 21)}Reels checksum: {generated.ToKeychain<SlotReels>().AsString()}");
                                slot.CalculateWins(generated, betAmount);
                                Program.PrintLine($"Total Balance after winnings: ${player.Balance}");
                            }
                            else PrintLine("Insufficent balance!");
                        }
                    }
                    else if (arguments.Length == 2){
                        betAmount = double.Parse(arguments[1]);
                        if (player.Balance >= betAmount)
                        {
                            Clear();
                            SlotReels generated = slot.GenerateSpin();
                            slot.AnimateRoll(5);
                            Clear();
                            slot.PrintSlotSpin(generated);
                            Program.PrintLine($"{new string(' ', 21)}Bet amount: ${betAmount};     Balance Available: ${player.Balance}      Coin Value: ${betAmount / 5} for each line.");
                            Program.PrintLine($"{new string(' ', 21)}Reels checksum: {generated.ToKeychain<SlotReels>().AsString()}");
                            slot.CalculateWins(generated, double.Parse(arguments[1]));
                            Program.PrintLine($"Total Balance after winnings: ${player.Balance}");
                        }
                        else PrintLine("Insufficent balance!");
                    }
                    

                }
                else if (command.StartsWith("volume")) {
                    string[] arguments = command.Split();

                    if (arguments.Length == 2) {
                        mp.Volume = double.Parse(arguments[1]) / 100.0f;
                        Program.PrintLine($"Volume set to {arguments[1]}");
                    }
                }
                Thread.Sleep(1);
            }
        }
        public static void PrintLine(string line) => Console.WriteLine(line);
        public static void PrintInline(string smth) => Console.Write(smth);
        
        public static void Clear() => Console.Clear();
        public static void Soundtrack() { 
            
        }
    }
}
