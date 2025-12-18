using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace slot
{

    public struct SlotItem { public int Index { get; set; } public char Type { get; set; } }
    public struct SlotReel
    {
        private List<SlotItem> _items;
        public IReadOnlyList<SlotItem> Items => _items ??= new List<SlotItem>();
        public void Add(SlotItem item) { _items ??= new List<SlotItem>(); _items.Add(item); }
    }
    public struct SlotReels
    {
        private List<SlotReel> _reels;
        public IReadOnlyList<SlotReel> Reels => _reels ??= new List<SlotReel>();
        public void Add(SlotReel reel) { _reels ??= new List<SlotReel>(); _reels.Add(reel); }
        public int Length => Reels?.Count ?? 0;
    }


    public class SlotMachine
    {
        private readonly PlayerBase _player;
        private static readonly ThreadLocal<Random> Rng = new ThreadLocal<Random>(() => new Random());
        private SlotReels _preGeneratedResult;

        public int FreeSpinsLeft { get; private set; }
        private bool IsInFreeSpinMode => FreeSpinsLeft > 0;

        private readonly double[] _betOptions = { 1.50, 3.00, 7.50, 15.00, 30.00, 75.00, 150.00 };
        private int _betIndex = 1;
        public double CurrentBet => _betOptions[_betIndex];

        private readonly int[][] _paylines = new int[][] {
            new[] {1,1,1,1,1}, new[] {0,0,0,0,0}, new[] {2,2,2,2,2},
            new[] {0,1,2,1,0}, new[] {2,1,0,1,2}, new[] {0,0,1,2,2},
            new[] {2,2,1,0,0}, new[] {1,0,0,0,1}, new[] {1,2,2,2,1},
            new[] {0,1,0,1,0}, new[] {2,1,2,1,2}, new[] {1,2,1,0,1},
            new[] {1,0,1,2,1}, new[] {0,1,1,1,0}, new[] {2,1,1,1,2}
        };

        public SlotMachine(PlayerBase player)
        {
            _player = player;
            _preGeneratedResult = GenerateSpin();
        }

        public SlotReels GenerateSpin()
        {
            var res = new SlotReels();
            for (int r = 0; r < 3; r++)
            {
                var row = new SlotReel();
                for (int c = 0; c < 5; c++)
                {
                    double rng = Rng.Value.NextDouble();
                    char t = rng < 0.04 ? 'W' : rng < 0.07 ? 'S' : "AK345"[Rng.Value.Next(5)];
                    row.Add(new SlotItem { Index = c, Type = t });
                }
                res.Add(row);
            }
            return res;
        }

        public void AnimateRoll()
        {
            _preGeneratedResult = GenerateSpin();
            int totalFrames = 35;
            int[] stops = { 8, 14, 20, 26, 32 };

            for (int f = 0; f < totalFrames; f++)
            {
                DrawUIFrame();
                for (int r = 0; r < 3; r++)
                {
                    Console.SetCursorPosition(44, 4 + r);
                    for (int c = 0; c < 5; c++)
                    {
                        if (f > stops[c]) PrintSymbol(_preGeneratedResult.Reels[r].Items[c].Type);
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write("AK345"[Rng.Value.Next(5)] + "   ");
                        }
                    }
                }
                Thread.Sleep(30);
            }
        }

        public void CalculateWins()
        {
            double totalWin = 0;
            int multiplier = IsInFreeSpinMode ? 3 : 1;
            var winPaths = new List<int[]>();

            foreach (var path in _paylines)
            {
                char first = _preGeneratedResult.Reels[path[0]].Items[0].Type;
                if (first == 'S') continue;
                int matches = 1; bool wild = (first == 'W');
                for (int i = 1; i < 5; i++)
                {
                    char n = _preGeneratedResult.Reels[path[i]].Items[i].Type;
                    if (n == first || n == 'W') { matches++; if (n == 'W') wild = true; }
                    else break;
                }
                if (matches >= 3)
                {
                    double lineWin = (matches * 1.5) * (CurrentBet / 15.0) * multiplier;
                    if (wild && first != 'W') lineWin *= 2;
                    totalWin += lineWin;
                    winPaths.Add(path.Take(matches).ToArray());
                }
            }

            if (totalWin > 0)
            {
                _player.Add(totalWin);
                HighlightWins(winPaths);
                Console.SetCursorPosition(44, 7);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"★ WIN: ${totalWin:N2} ★");
            }
            else if (!IsInFreeSpinMode) _player.Add(-CurrentBet);

            int scatterCount = _preGeneratedResult.Reels.SelectMany(x => x.Items).Count(i => i.Type == 'S');
            if (scatterCount >= 3)
            {
                RunScatterBonus();
                FreeSpinsLeft += 15;
            }
        }

        private void RunScatterBonus()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n\n\n" + new string(' ', 40) + "!!! SCATTER BONUS !!!");
            Console.WriteLine(new string(' ', 35) + "Pick a Treasure Jar (1, 2, or 3):");

            var key = Console.ReadKey(true).Key;
            double bonus = CurrentBet * (Rng.Value.Next(5, 15));
            _player.Add(bonus);

            Console.WriteLine("\n" + new string(' ', 42) + $"REVEALED: ${bonus:N2}!");
            Console.WriteLine(new string(' ', 38) + "+ 15 FREE SPINS GRANTED!");
            Thread.Sleep(2500);
        }

        private void HighlightWins(List<int[]> paths)
        {
            for (int i = 0; i < 2; i++)
            {
                foreach (var p in paths)
                {
                    for (int c = 0; c < p.Length; c++)
                    {
                        Console.SetCursorPosition(44 + (c * 4), 4 + p[c]);
                        Console.BackgroundColor = ConsoleColor.DarkRed;
                        PrintSymbol(_preGeneratedResult.Reels[p[c]].Items[c].Type);
                    }
                }
                Thread.Sleep(200); Console.ResetColor(); DrawResultStatic(); Thread.Sleep(200);
            }
        }

        private void DrawResultStatic()
        {
            if (_preGeneratedResult.Length == 0) return;
            for (int r = 0; r < 3; r++)
            {
                Console.SetCursorPosition(44, 4 + r);
                for (int c = 0; c < 5; c++) PrintSymbol(_preGeneratedResult.Reels[r].Items[c].Type);
            }
        }

        public void RunGameLoop()
        {
            while (true)
            {
                DrawUIFrame();
                DrawResultStatic();

                if (IsInFreeSpinMode)
                {
                    Console.SetCursorPosition(40, 12);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("AUTO-SPINNING FREE SPINS...");
                    Thread.Sleep(1200);
                    FreeSpinsLeft--;
                    AnimateRoll();
                    CalculateWins();
                    continue;
                }

                var k = Console.ReadKey(true).Key;
                if (k == ConsoleKey.Spacebar)
                {
                    if (_player.Balance >= CurrentBet) { AnimateRoll(); CalculateWins(); }
                }
                else if (k == ConsoleKey.UpArrow) _betIndex = Math.Min(_betIndex + 1, _betOptions.Length - 1);
                else if (k == ConsoleKey.DownArrow) _betIndex = Math.Max(_betIndex - 1, 0);
                else if (k == ConsoleKey.Z) _player.Add(500); // Take Loan
                else if (k == ConsoleKey.F) { if (_player.Balance >= CurrentBet * 50.0) { _player.Add(-(CurrentBet * 50.0)); AnimateRoll(); FreeSpinsLeft += 15; CalculateWins(); } }
                else if (k == ConsoleKey.Q) break;
            }
        }

        private void DrawUIFrame()
        {
            Console.SetCursorPosition(0, 0);
            Console.ForegroundColor = IsInFreeSpinMode ? ConsoleColor.Cyan : ConsoleColor.Yellow;
            string title = IsInFreeSpinMode ? $"FREE SPINS: {FreeSpinsLeft}" : "  SLOT MACHINE  ";
            Console.WriteLine("\n" + new string(' ', 38) + "╔═════════════════════════════════╗");
            Console.WriteLine(new string(' ', 38) + $"║ {title.PadLeft(20).PadRight(31)} ║");
            Console.WriteLine(new string(' ', 38) + "╠═════════════════════════════════╣");
            for (int i = 0; i < 3; i++) Console.WriteLine(new string(' ', 38) + "║                                 ║");
            Console.WriteLine(new string(' ', 38) + "╚═════════════════════════════════╝");

            Console.SetCursorPosition(40, 8); Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($" BALANCE: ${_player.Balance:N2} ".PadRight(30));
            Console.SetCursorPosition(40, 9); Console.ForegroundColor = ConsoleColor.White;
            Console.Write($" CURRENT BET: ${CurrentBet:N2} ".PadRight(30));
            Console.SetCursorPosition(32, 11); Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("[SPACE] Spin | [↑/↓] Change Bet");
            Console.SetCursorPosition(32, 13);
            Console.WriteLine("[Z] Take Loan (if balance is 0) | [F] Buy Bonus | [Q] Quit");
        }

        private void PrintSymbol(char t)
        {
            Console.ForegroundColor = t switch { 'W' => ConsoleColor.Yellow, 'S' => ConsoleColor.Magenta, 'A' => ConsoleColor.Red, _ => ConsoleColor.White };
            Console.Write(t + "   ");
        }
    }
}