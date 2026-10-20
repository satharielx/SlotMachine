using System;
using System.Collections.Generic;
using System.Linq;

namespace slot
{
    internal sealed class SpinResult
    {
        public double Win { get; set; }
        public int Scatters { get; set; }
        public List<int[]> WinningPaths { get; } = new List<int[]>();
    }

    internal static class SlotRules
    {
        private static readonly int[][] Paylines = {
            new[] {1,1,1,1,1}, new[] {0,0,0,0,0}, new[] {2,2,2,2,2},
            new[] {0,1,2,1,0}, new[] {2,1,0,1,2}, new[] {0,0,1,2,2},
            new[] {2,2,1,0,0}, new[] {1,0,0,0,1}, new[] {1,2,2,2,1},
            new[] {0,1,0,1,0}, new[] {2,1,2,1,2}, new[] {1,2,1,0,1},
            new[] {1,0,1,2,1}, new[] {0,1,1,1,0}, new[] {2,1,1,1,2}
        };

        public static SpinResult Evaluate(SlotReels reels, double bet, bool freeSpin)
        {
            if (reels.Length != 3 || reels.Reels.Any(row => row.Items.Count != 5))
                throw new ArgumentException("A spin must contain three rows of five symbols.", nameof(reels));
            if (double.IsNaN(bet) || double.IsInfinity(bet) || bet <= 0)
                throw new ArgumentOutOfRangeException(nameof(bet));
            var result = new SpinResult();
            foreach (var path in Paylines)
            {
                char symbol = reels.Reels[path[0]].Items[0].Type;
                if (symbol == 'S') continue;
                bool wild = symbol == 'W';
                int matches = 1;
                for (int column = 1; column < 5; column++)
                {
                    char next = reels.Reels[path[column]].Items[column].Type;
                    if (next == 'S' || (next != symbol && next != 'W' && symbol != 'W')) break;
                    matches++;
                    wild |= next == 'W';
                    if (symbol == 'W' && next != 'W') symbol = next;
                }
                if (matches < 3) continue;
                double payout = matches * 1.5 * (bet / 15.0) * (freeSpin ? 3 : 1);
                if (wild && symbol != 'W') payout *= 2;
                result.Win += payout;
                result.WinningPaths.Add(path.Take(matches).ToArray());
            }
            result.Win = Math.Round(result.Win, 2, MidpointRounding.AwayFromZero);
            result.Scatters = reels.Reels.SelectMany(row => row.Items).Count(item => item.Type == 'S');
            return result;
        }
    }
}
