using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Text.RegularExpressions;
using System.Linq.Expressions;

namespace slot
{
    public class Program
    {
        static void Main(string[] args)
        {
            PlayerBase player = new PlayerBase();
            SlotMachine slot = new SlotMachine(player);
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
                   
                }
                if (command.StartsWith("generateSlot"))
                {
                    Clear();
                    string[] arguments = command.Split();
                    if (player.Balance >= double.Parse(arguments[1]))
                    {

                        SlotReels generated = slot.GenerateSpin();
                        slot.PrintSlotSpin(generated);
                        Program.PrintLine($"{new string(' ', 21)}Bet amount: ${arguments[1]};     Balance Available: ${player.Balance}      Coin Value: ${double.Parse(arguments[1]) / 5} for each line.");
                        slot.CalculateWins(generated, double.Parse(arguments[1]));

                    }
                    else PrintLine("Insufficent balance!");
                    
                }
                Thread.Sleep(1);
            }
        }
        public static void PrintLine(string line) => Console.WriteLine(line);

        public static void Clear() => Console.Clear();
    }
}
