using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.XPath;
using System.Threading;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Security.Principal;
using System.CodeDom;
using System.Diagnostics.Eventing.Reader;
using System.Windows.Media;
using System.IO;

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
    public struct Vector2 {
        public int x = 0;
        public int y = 0;
        public Vector2(int x, int y) {
            this.x = x;
            this.y = y;
        } 
    }
    public class SlotMachine
    {
        PlayerBase player;
        public List<char> SlotSymbols = new List<char>()
        {
            'A', 'K', '♥', '♦', '♣', 'W'
        };
        public Dictionary<char, double> WinningTable = new Dictionary<char, double>();
        public double RDP { get; set; }
        public int Lines { get; set; }
        public SlotMachine(PlayerBase player)
        {
            WinningTable['W'] = 11.0;
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
            Dictionary<int, List<int>> wildsPlaces = new Dictionary<int, List<int>>();
            double percentToGetWild = 0.0114;
            SlotReels result = new SlotReels();
            for (int i = 0; i < rows; i++) {
                SlotReel slotRow = new SlotReel();
                wildsPlaces[i] = new List<int>();
                for (int j = 0; j < cols; j++) {

                    bool shouldHaveWild = new Random().NextDouble() <= percentToGetWild;
                    SlotItem slotItem = new SlotItem();
                    slotItem.index = j;
                    
                    slotItem.type = shouldHaveWild ? SlotSymbols[SlotSymbols.Count - 1] : SlotSymbols[new Random().Next(SlotSymbols.Count - 1)];
                    if (shouldHaveWild) wildsPlaces[i].Add(j);
                    Array.Resize(ref slotRow.items, slotRow.items.Length + 1);
                    slotRow.items[slotRow.items.Length - 1] = slotItem;
                    slotRow.lenght++;
                    Thread.Sleep(100);
                }
                Array.Resize(ref result.Reels, result.Reels.Length + 1);
                result.Reels[result.Reels.Length - 1] = slotRow;
                 
            }
            result = IssueWilds(wildsPlaces, result);
            return result;
        }
        private SlotReels IssueWilds(Dictionary<int, List<int>> wildsPlaces, SlotReels slotRoll) {

            if (wildsPlaces.Count() > 0) { 
                List<int> placesToReplace = new List<int>();
                foreach (var wild in wildsPlaces) {
                    var temp = wild.Value;
                    for (int i = 0; i < slotRoll.Reels.Length; i++) {
                        for (int j = 0; j < temp.Count(); j++) {
                            slotRoll.Reels[i].items[temp[j]].type = 'W';
                        }
                    }
                }
            } 
            return slotRoll;
        }
        public bool CoinflipResult() { return new Random().Next(0, 1) == 1; }
        public int FlipResult(int min, int max) { return new Random().Next(min, max); }
        #region Win Logic
        public void CalculateWins(SlotReels slot, double betAmount)
        {
            Dictionary<int, double> winningTable = new Dictionary<int, double>();
            Dictionary<int, KeyValuePair<int, char>> repetitive = new Dictionary<int, KeyValuePair<int, char>>();
            #region Three Horizontal Lines Logic
            if (slot.Reels.Length >= 3)
            {
                for (int i = 0; i < slot.Reels.Count(); i++)
                {
                    SlotReel slotRow = slot.Reels[i];

                    string pattern = @"(.)\1{2,}";
                    Regex engine = new Regex(pattern);
                    string line = "";
                    int lineNumber = i + 1;
                    for (int j = 0; j < slotRow.items.Length; j++)
                    {
                        line += slotRow.items[j].type;
                    }
                    Match match = engine.Match(line);
                    //If a candidate meets the conditions of the pattern above eg. (We have winning condition)
                    if (match.Success)
                    {
                        char repeated = match.Value[0];
                        int timesRepeated = line.Count(x => repeated == x);
                        //timesRepeated == 2 && 
                        if (timesRepeated == 3)
                        {
                            repetitive[lineNumber] = new KeyValuePair<int, char>(timesRepeated, repeated);
                            winningTable[lineNumber] = WinningTable[repeated] * (betAmount / 5);
                        }
                        else if (timesRepeated == 4)
                        {
                            repetitive[lineNumber] = new KeyValuePair<int, char>(timesRepeated, repeated);
                            winningTable[lineNumber] = (WinningTable[repeated] * (betAmount / 5)) * timesRepeated;
                        }
                        else if (timesRepeated == 5)
                        {
                            repetitive[lineNumber] = new KeyValuePair<int, char>(timesRepeated, repeated);
                            winningTable[lineNumber] = (WinningTable[repeated] * (betAmount / 5)) * timesRepeated * 10;
                        }

                    }
                }
                #endregion
                #region Diagonals Wins
                Dictionary<char, List<int>> diagonals = MatchDiagonals(slot);
                Dictionary<int, double> diagsWins = new Dictionary<int, double>();
                Dictionary<char, List<int>> diagsRepetitive = new Dictionary<char, List<int>>();
                foreach (var item in diagonals)
                {

                    diagsRepetitive[item.Key] = new List<int> { item.Value[0], item.Value[1] };
                    diagsWins[item.Value[0]] = (WinningTable[item.Key] * (betAmount / 5)) * item.Value[0] * 11;
                }
                #endregion
                #region Vertical Wins
                Dictionary<int, char> verticals = MatchVertical(slot);
                Dictionary<int, double> vertWins = new Dictionary<int, double>();
                Dictionary<int, char> vertRepetitive = new Dictionary<int, char>();
                foreach (var item in verticals) {
                    vertRepetitive[item.Key] = item.Value;
                    vertWins[item.Key] = (WinningTable[item.Value] * (betAmount / 5)) * 3 * 20;
                }
                #endregion
                #region No Wins
                //If there are no winnings
                if (!(winningTable.Count > 0) && !(diagsWins.Count > 0) && !(vertWins.Count > 0))
                {
                    Program.PrintLine("No winnings, Try Again.");
                    player.UpdateBalance(-betAmount);
                    Program.PrintLine(player.ToString());
                }
                #endregion
                #region Wins
                //If there is any win, printing out information.
                else if (winningTable.Count > 0 || diagsWins.Count > 0 || vertWins.Count > 0)
                {
                    List<double> winsTotal = new List<double>();
                    Program.PrintLine("");
                    Program.PrintLine(new string(' ', 49) + new string('*', 20));
                    Program.PrintLine(new string(' ', 53) + "W I N N E R   ");
                    Program.PrintLine(new string(' ', 49) + new string('*', 20));
                    Program.PrintLine("");
                    player.UpdateBalance(-betAmount);
                    
                    foreach (var rep in repetitive)
                    {
                        winsTotal.Add(winningTable[rep.Key]);
                        Program.PrintLine($"Won ${winningTable[rep.Key]} " +
                            $"at line {rep.Key} " +
                            $"by symbol {rep.Value.Value} " +
                            $"repeated {rep.Value.Key} times");
                        player.UpdateBalance(winningTable[rep.Key]);

                    }
                    foreach (var rep in diagsRepetitive)
                    {
                        winsTotal.Add(diagsWins[rep.Value[0]]);
                        Program.PrintLine($"Won ${diagsWins[rep.Value[0]]} " +
                            $"at line {rep.Value[0]} " +
                            $"by symbol {rep.Key} " +
                            $"repeated {rep.Value[1]} times");
                        player.UpdateBalance(diagsWins[rep.Value[0]]);

                    }
                    foreach (var rep in vertRepetitive) {
                        winsTotal.Add(vertWins[rep.Key]);
                        Program.PrintLine($"Won ${vertWins[rep.Key]} " +
                           $"at vertical line {rep.Key} " +
                           $"by symbol " + (rep.Value == 'W' ? "special triggered bonus from WILD symbol" : rep.Value) +
                           $" repeated {3} times");
                        player.UpdateBalance(vertWins[rep.Key]);
                    }
                    Program.PrintLine($"Total win is ${winsTotal.Sum()} with a bet amount of ${betAmount}");
                    if (winsTotal.Sum() > 500)
                    {
                        MediaPlayer mp = new MediaPlayer();
                        mp.Open(new Uri(Directory.GetCurrentDirectory() + @"\sounds\bigwin.wav"));
                        mp.Play();
                        mp.Volume = 0.6;
                        Thread.Sleep(5000);
                        mp.Stop();
                    }
                    else {
                        MediaPlayer mp = new MediaPlayer();
                        mp.Open(new Uri(Directory.GetCurrentDirectory() + @"\sounds\win.wav"));
                        mp.Play();
                        mp.Volume = 0.4;
                    }
                    #endregion
                }
            }
        }
        #region Diagonals Logic
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
        #endregion
        #region Match Vertices
        public Dictionary<int, char> MatchVertical(SlotReels slot) {
            Dictionary<int, char> result = new Dictionary<int, char>();
            //Verticals Match
            int columnsCount = 0;
            
                for (int j = 0; j < slot.Reels[0].items.Length; j++) {
                    columnsCount++;
                }
            int line = 0;
            for (int i = 0; i < columnsCount; i++) {
                string verticalRow = "";
                line = i + 1;
                for (int j = 0; j < slot.Reels.Count(); j++) {
                   verticalRow += slot.Reels[j].items[i].type;
                    
                }
                string pattern = @"(.)\1{2,}";
                Regex engine = new Regex(pattern);
                Match match = engine.Match(verticalRow);
                if (match.Success)
                {
                    result[line] = verticalRow[0];
                }
                
            }
            return result;
        }
        #endregion
        #region Misc
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
        #endregion
        #endregion
        #region Basic Animation
        public void AnimateRoll(int seconds) {
            Vector2 carretPos = new Vector2(0, 0);
            int carretIndex = -1;
            Program.Clear();
            carretIndex = 0;
            Program.PrintLine($"{new string(' ', 45)}{new string('*', 20)}");
            carretPos.y += 1;
            carretPos.x = 0;
            Program.PrintLine($"{new string(' ', 45)}{new string('*', 1)}{new string(' ', 4)} ROLLING {new string(' ', 4)}*");
            carretPos.y += 1;
            carretPos.x = 0;
            Program.PrintLine($"{new string(' ', 45)}{new string('*', 20)}");
            carretPos.y += 1;
            carretPos.x = 0;
            int timesRepeated = seconds * 2;
            Program.PrintLine($"");
            carretPos.y += 1;
            carretPos.x = 0;
            Program.PrintLine($"");
            carretPos.y += 1;
            carretPos.x = 0;
            Program.PrintLine($"");
            carretPos.y += 1;
            carretPos.x = 0;
            Vector2 begin = carretPos;
            Vector2 end = new Vector2(0, carretPos.y + 2);
            
            for (int i = 0; i < timesRepeated; i++) {

                MediaPlayer mp = new MediaPlayer();
                mp.Open(new Uri(Directory.GetCurrentDirectory() + @"\sounds\roll.wav"));
                mp.Play();
                for (int j = 0; j < 3; j++) {
                    Console.SetCursorPosition(begin.x, begin.y + j * 2);
                    Program.PrintInline($"{new string(' ', 42)}");
                    for (int k = 0; k < 5; k++) {
                        Program.PrintInline($"{GetRandomChar()}     ");
                        Thread.Sleep(20);
                    }
                    Program.PrintLine("");
                    Program.PrintLine("");
                }
            }
        }
        private char GetRandomChar() {
            char result = '0';
            string valid = "AK♥♦♣W";
            result = valid[new Random().Next(0, valid.Length - 1)];
            return result;
        }
        private string GetRandomLine(int lenght) {
            string valid = "abcdef0123456789";
            int c = 0;
            string result = "";
            while (c < lenght) {
                result += valid[new Random().Next(0, valid.Length - 1)];
                c++;
            }
            return result;
        }
        #endregion
    }
}
