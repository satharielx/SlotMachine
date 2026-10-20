using System;
using System.Collections.Generic;
using System.Linq;

namespace slot
{
    internal sealed class SpinResult
    {
        public decimal Win { get; set; }
        public int ScatterReels { get; set; }
        public int Coins { get; set; }
        public List<int[]> WinningPaths { get; } = new List<int[]>();
    }

    internal static class SlotRules
    {
        public const int MaximumFreeSpins = 50;
        public const int MaximumMultiplier = 5;
        public const int CoinTrigger = 6;
        public const int CoinChancePercent = 12;
        public const int BonusBuyMultiplier = 50;
        public const decimal TargetRtp = 0.989m;
        public const string RegularSymbols = "345AKC";
        public static readonly decimal[] BetOptions = { 1.50m, 3m, 7.50m, 15m, 30m, 75m, 150m };
        private static readonly int[] LowPayouts = { 6, 15, 45 };
        private static readonly int[] PremiumPayouts = { 15, 36, 120 };
        private static readonly int[] CrownPayouts = { 30, 90, 300 };
        private static readonly int[] WildPayouts = { 45, 150, 450 };
        private static readonly string[] Strips = new[] {
            "W45AKW45COOW45AKSOO345AKO345AK345W345AKO345CAK345S345AKO345AK345",
            "KW45AOW45AK345COO345AKSOO345KA345O345AKW345AK345S345CAKO345AK345",
            "AW45KW45OW45CAK345SOO345AKOO345KA345W345AK345O345SKA345CO345AK345",
            "W45KASOOW45AK345O345CAK345OO345AKW345KA345S345AKO345CAK345O345AK",
            "KW45ACW45AKOW45SOO345KA345OO345AK345W345CAK345O345AKS345AK345O345"
        }.Select(BuildBaseStrip).ToArray();
        private static readonly string[] FreeStrips = new[] {
            "345AK345COO345AKSOO345AKO345AK345W345AKO345CAK345S345AKO345AK345",
            "K345AO345AK345COO345AKSOO345KA345O345AKW345AK345S345CAKO345AK345",
            "A345K345O345CAK345SOO345AKOO345KA345W345AK345O345SKA345CO345AK345",
            "345KASOO345AK345O345CAK345OO345AKW345KA345S345AKO345CAK345O345AK",
            "K345AC345AKO345SOO345KA345OO345AK345W345CAK345O345AKS345AK345O345"
        }.Select(BuildFreeStrip).ToArray();
        private static string BuildBaseStrip(string strip)
        {
            int position = strip.IndexOf('3');
            string moreWilds = strip.Substring(0, position) + "W" + strip.Substring(position + 1);
            return string.Concat(Enumerable.Repeat(strip, 13)) + string.Concat(Enumerable.Repeat(moreWilds, 87));
        }

        private static string BuildFreeStrip(string strip) =>
            string.Concat(Enumerable.Repeat(strip, 57)) + string.Concat(Enumerable.Repeat(strip.Replace('W', '3'), 43));

        internal static IReadOnlyList<string> ReelStrips => Array.AsReadOnly(Strips);
        internal static IReadOnlyList<string> FreeReelStrips => Array.AsReadOnly(FreeStrips);
        private static readonly int[][] Paylines = {
            new[] {1,1,1,1,1}, new[] {0,0,0,0,0}, new[] {2,2,2,2,2},
            new[] {0,1,2,1,0}, new[] {2,1,0,1,2}, new[] {0,0,1,2,2},
            new[] {2,2,1,0,0}, new[] {1,0,0,0,1}, new[] {1,2,2,2,1},
            new[] {0,1,0,1,0}, new[] {2,1,2,1,2}, new[] {1,2,1,0,1},
            new[] {1,0,1,2,1}, new[] {0,1,1,1,0}, new[] {2,1,1,1,2}
        };

        public static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        public static SlotReels Generate(IRandomSource random, bool free = false)
        {
            var symbols = new char[3, 5];
            for (int column = 0; column < 5; column++)
            {
                string strip = (free ? FreeStrips : Strips)[column];
                int stop = random.NextInt(strip.Length);
                for (int row = 0; row < 3; row++) symbols[row, column] = strip[(stop + row) % strip.Length];
            }
            return Board(symbols);
        }

        public static SlotReels Board(char[,] symbols)
        {
            if (symbols.GetLength(0) != 3 || symbols.GetLength(1) != 5) throw new ArgumentException("Expected a 3 by 5 board.");
            var grid = new SlotReels();
            for (int row = 0; row < 3; row++)
            {
                var reel = new SlotReel();
                for (int column = 0; column < 5; column++) reel.Add(new SlotItem { Index = column, Type = symbols[row, column] });
                grid.Add(reel);
            }
            return grid;
        }

        public static char[,] Symbols(SlotReels grid)
        {
            Validate(grid);
            var symbols = new char[3, 5];
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 5; column++) symbols[row, column] = grid.Reels[row].Items[column].Type;
            return symbols;
        }

        public static int Payout(char symbol, int matches)
        {
            if (matches < 3 || matches > 5) return 0;
            int index = matches - 3;
            if (symbol == 'W') return WildPayouts[index];
            if (symbol == 'C') return CrownPayouts[index];
            if (symbol == 'A' || symbol == 'K') return PremiumPayouts[index];
            if (symbol == '3' || symbol == '4' || symbol == '5') return LowPayouts[index];
            return 0;
        }

        public static SpinResult Evaluate(SlotReels grid, decimal bet, int multiplier = 1)
        {
            Validate(grid);
            if (bet <= 0 || bet != Money(bet)) throw new ArgumentOutOfRangeException(nameof(bet));
            if (multiplier < 1 || multiplier > MaximumMultiplier) throw new ArgumentOutOfRangeException(nameof(multiplier));
            var result = new SpinResult();
            foreach (int[] path in Paylines)
            {
                decimal best = 0;
                int bestMatches = 0;
                foreach (char target in RegularSymbols + "W")
                {
                    int matches = 0;
                    for (int column = 0; column < 5; column++)
                    {
                        char symbol = grid.Reels[path[column]].Items[column].Type;
                        if (symbol != target && (target == 'W' || symbol != 'W')) break;
                        matches++;
                    }
                    decimal payout = Payout(target, matches) * (bet / 15m) * multiplier;
                    if (payout > best || (payout == best && matches > bestMatches))
                    {
                        best = payout;
                        bestMatches = matches;
                    }
                }
                if (best == 0) continue;
                result.Win += best;
                result.WinningPaths.Add(path.Take(bestMatches).ToArray());
            }
            result.Win = Money(result.Win);
            for (int column = 0; column < 5; column++)
            {
                bool scatter = false;
                for (int row = 0; row < 3; row++)
                {
                    char symbol = grid.Reels[row].Items[column].Type;
                    scatter |= symbol == 'S';
                    if (symbol == 'O') result.Coins++;
                }
                if (scatter) result.ScatterReels++;
            }
            return result;
        }

        public static int FreeSpinAward(int scatterReels) => scatterReels == 3 ? 8 : scatterReels == 4 ? 12 : scatterReels == 5 ? 20 : 0;

        public static decimal DrawCoin(IRandomSource random, decimal bet)
        {
            int draw = random.NextInt(1000);
            int factor = draw < 700 ? 1 : draw < 900 ? 2 : draw < 970 ? 3 : draw < 990 ? 5 : draw < 998 ? 10 : 25;
            return Money(bet * factor);
        }

        private static void Validate(SlotReels grid)
        {
            if (grid.Length != 3 || grid.Reels.Any(row => row.Items.Count != 5))
                throw new ArgumentException("A spin must contain three rows of five symbols.", nameof(grid));
            if (grid.Reels.SelectMany(row => row.Items).Any(item => !(RegularSymbols + "WSO").Contains(item.Type)))
                throw new ArgumentException("Unknown reel symbol.", nameof(grid));
        }
    }
}
