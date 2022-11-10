using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.XPath;
using System.Threading;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace slot
{
    public struct SlotReel
    {
       public SlotItem[] items;
       public int lenght;
        public SlotReel() {
            items = new SlotItem[] { };
            lenght = 0;
        }
    }
    public struct SlotItem {
        public int index;
        public char type;
    }
    public struct SlotReels
    {
        public SlotReel[] Reels;
        public int lenght;
        public SlotReels()
        {
            Reels = new SlotReel[] { };
            lenght = 0;
        }
    }

    public class SlotMachine
    {
        PlayerBase player;
        public List<char> SlotSymbols = new List<char>()
        {
            'A', 'K', '♥', '♦', '♣'
        };
        public Dictionary<char, double> WinningTable = new Dictionary<char, double>();
        public double RDP { get; set; }
        public int Lines { get; set; }
        public SlotMachine(PlayerBase player)
        {
           
            WinningTable['A'] = 10.0;
            WinningTable['K'] = 3.0;
            WinningTable['♥'] = 2.5;
            WinningTable['♦'] = 1.3;
            WinningTable['♣'] = 0.7;
            this.player = player;
        }
        public SlotReels GenerateSpin() {
            int rows = 3;
            int cols = 5;
            SlotReels result = new SlotReels();
            for (int i = 0; i < rows; i++) {
                SlotReel slotRow = new SlotReel();
                
                for (int j = 0; j < cols; j++) {
                    
                    SlotItem slotItem = new SlotItem();
                    slotItem.index = j;
                    slotItem.type = SlotSymbols[new Random().Next(SlotSymbols.Count)];
                    Array.Resize(ref slotRow.items, slotRow.items.Length + 1);
                    slotRow.items[slotRow.items.Length - 1] = slotItem;
                    slotRow.lenght++;
                    Thread.Sleep(100);
                }
                Array.Resize(ref result.Reels, result.Reels.Length + 1);
                result.Reels[result.Reels.Length - 1] = slotRow;
                 
            }
            return result;
        }
        public bool CoinflipResult() { return new Random().Next(0, 1) == 1; }
        public int FlipResult(int min, int max) { return new Random().Next(min, max); }
        public void CalculateWins(SlotReels slot, double betAmount) {
            Dictionary<int, double> winningTable = new Dictionary<int, double>();
            Dictionary<char, List<int>> repetitive = new Dictionary<char, List<int>>();
            
            if (slot.Reels.Length >= 3) {
                for (int i = 0; i < slot.Reels.Count(); i++)
                {
                    SlotReel slotRow = slot.Reels[i];
                    
                    string pattern = @"(.)\1{2,}";
                    Regex engine = new Regex(pattern);
                    string line = "";
                    int lineNumber = i + 1;
                    for (int j = 0; j < slotRow.items.Length; j++) {
                        line += slotRow.items[j].type;
                    }
                    Match match = engine.Match(line);
                    //If a candidate meets the conditions of the pattern above eg. (We have winning condition)
                    if (match.Success) {
                        char repeated = match.Value[0];
                        int timesRepeated = line.Count(x => repeated == x);
                        
                            if (timesRepeated == 3)
                            {
                                repetitive[repeated] = new List<int> { lineNumber, timesRepeated };
                                winningTable[lineNumber] = WinningTable[repeated] * betAmount;
                            }
                            else if (timesRepeated == 4) {
                                repetitive[repeated] = new List<int> { lineNumber, timesRepeated };
                                winningTable[lineNumber] = (WinningTable[repeated] * betAmount) * timesRepeated;
                            }
                            else if (timesRepeated == 5)
                            {
                                repetitive[repeated] = new List<int> { lineNumber, timesRepeated };
                                winningTable[lineNumber] = (WinningTable[repeated] * betAmount) * timesRepeated * 10;
                            }
                        
                    }
                    

                }
                Dictionary<char, List<int>> diagonals = MatchDiagonals(slot);
                Dictionary<int, double> diagsWins = new Dictionary<int, double>();
                Dictionary<char, List<int>> diagsRepetitive = new Dictionary<char, List<int>>();
                foreach (var item in diagonals)
                {
                    
                    diagsRepetitive[item.Key] = new List<int> { item.Value[0], item.Value[1] };
                    diagsWins[item.Value[0]] = (WinningTable[item.Key] * betAmount) * item.Value[0] * 40;
                }

                //If there are no winnings
                if (!(winningTable.Count > 0) && !(diagsWins.Count > 0))
                {
                    Program.PrintLine("No winnings, Try Again.");
                    player.UpdateBalance(-betAmount);
                    Program.PrintLine(player.ToString());
                }
                //If there is any win, printing out information.
                else if(winningTable.Count > 0) {
                    Program.PrintLine("");
                    Program.PrintLine(new string(' ', 49) + new string('*', 20));
                    Program.PrintLine(new string(' ', 53) + "W I N N E R   ");
                    Program.PrintLine(new string(' ', 49) + new string('*', 20));
                    Program.PrintLine("");
                    foreach (var rep in repetitive)
                    {
                        Program.PrintLine($"Won ${winningTable[rep.Value[0]]} " +
                            $"at line {rep.Value[0]} " +
                            $"by symbol {rep.Key} " +
                            $"repeated {rep.Value[1]} times");
                        player.UpdateBalance(winningTable[rep.Value[0]]);
                        
                    }
                    foreach (var rep in diagsRepetitive)
                    {
                        Program.PrintLine($"Won ${diagsWins[rep.Value[0]]} " +
                            $"at line {rep.Value[0]} " +
                            $"by symbol {rep.Key} " +
                            $"repeated {rep.Value[1]} times");
                        player.UpdateBalance(diagsWins[rep.Value[0]]);

                    }
                }
            }  
        }

        private Dictionary<char, List<int>> MatchDiagonals(SlotReels slot) {
            string[] slotReels = new string[] { };
            Dictionary<char, List<int>> result = new Dictionary<char, List<int>>();
            for (int i = 0; i < slot.Reels.Length; i++)
            {
                SlotReel current = slot.Reels[i];
                string slotReel = AsString(current);
                Array.Resize(ref slotReels, slotReels.Length + 1);
                slotReels[slotReels.Length - 1] = slotReel;
            }
            
            //Match first diagonal
            string firstDiag = "";
            for (int i = 0; i < slotReels.Length; i++)
            {
                firstDiag += slotReels[i][i * 2];
            }
            //Match second diagonal
            string secondDiag = "";
            for (int i = 0; i < slotReels.Length; i++)
            {
                string line = slotReels[i];
                if (i == slotReels.Length - 1)
                {
                    secondDiag += slotReels[i][4 / (i + 1) - 1];
                }
                else
                    secondDiag += slotReels[i][4 / (i + 1)];
            }
            string pattern = @"(.)\1{2,}";
            Regex engine = new Regex(pattern);
            Match found = engine.Match(firstDiag);
            char sym = firstDiag[0];
            if (found.Success)
            {
                result[sym] = new List<int> { 4, firstDiag.Count(x => sym == x) };
            }
            
            found = engine.Match(secondDiag);
            if (found.Success) {
                result[sym] = new List<int> { 5, secondDiag.Count(x => sym == x) };
            }
            return result;
        }
        public string AsString(SlotReel reel) {
            string final = "";
            for (int i = 0; i < reel.items.Length; i++) { 
                SlotItem currentItem = reel.items[i];
                final += currentItem.type;
            }
            return final;
        }
        public void PrintSlotSpin(SlotReels slot) {
            for (int i = 0; i < slot.Reels.Length; i++) {
                Console.WriteLine();
                Console.WriteLine();
                Console.Write(new string(' ', 45));
                for (int j = 0; j < slot.Reels[i].items.Length; j++) {
                    SlotItem slotItem = slot.Reels[i].items[j];
                    Console.Write(slotItem.type + "     ");
                }
                Console.WriteLine();
                Console.WriteLine();
            }
        }
       
    }
}
