using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using System.IO;

namespace slot
{
    // Lightweight representations for reel and item. Kept as structs for value semantics
    // but designed with simple, clear APIs to make the calling code more readable.
    public struct SlotItem
    {
        public int Index { get; set; }
        public char Type { get; set; }
    }

    public struct SlotReel
    {
        private List<SlotItem> _items;
        public IReadOnlyList<SlotItem> Items => _items ??= new List<SlotItem>();
        public int Length => Items.Count;

        public SlotReel(IEnumerable<SlotItem> items)
        {
            _items = items?.ToList() ?? new List<SlotItem>();
        }

        public void Add(SlotItem item)
        {
            if (_items == null) _items = new List<SlotItem>();
            _items.Add(item);
        }
    }

    public struct SlotReels
    {
        private List<SlotReel> _reels;
        public IReadOnlyList<SlotReel> Reels => _reels ??= new List<SlotReel>();
        public int Length => Reels.Count;

        public void Add(SlotReel reel)
        {
            if (_reels == null) _reels = new List<SlotReel>();
            _reels.Add(reel);
        }
    }

    public class SlotMachine
    {
        private readonly PlayerBase _player;
        private static readonly ThreadLocal<Random> Rng = new ThreadLocal<Random>(() => new Random());

        // Symbols and payout table are explicit and immutable after construction
        // We keep explicit W (wild) and S (scatter) semantics; baseSymbols excludes those.
        public IReadOnlyList<char> SlotSymbols { get; } = new List<char> { 'A', 'K', '3', '4', '5', 'S', 'W' };
        private IReadOnlyList<char> BaseSymbols => SlotSymbols.Where(c => c != 'W' && c != 'S').ToList();
        public IReadOnlyDictionary<char, double> WinningTable { get; }

        // Free spins state exposed so the game loop can react accordingly
        public int FreeSpins { get; private set; }

        // Free-spins specific multiplier (stacks during the bonus game). Only active while in free-spin mode.
        public int FreeSpinMultiplier { get; private set; } = 1;
        private const int MaxFreeSpinMultiplier = 10;
        private bool IsInFreeSpinMode { get; set; } = false;
        // Column index (0-based) where the multiplier indicator is shown. -1 = none.
        private int MultiplierColumnIndex { get; set; } = -1;

        public double RDP { get; set; }
        public int Lines { get; set; }

        public SlotMachine(PlayerBase player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            WinningTable = new Dictionary<char, double>
            {
                ['W'] = 11.0,
                ['A'] = 10.0,
                ['K'] = 3.0,
                ['3'] = 2.5,
                ['4'] = 1.3,
                ['5'] = 0.7
            };
        }

        // Generate a spin in row-major order (rows x columns). Uses a single RNG source
        // and avoids Thread.Sleep to be fast and deterministic in tests.
        public SlotReels GenerateSpin()
        {
            const int rows = 3;
            const int cols = 5;
            var wildsPlaces = new Dictionary<int, List<int>>();
            const double percentToGetWild = 0.0114; // small chance for wild
            const double percentToGetScatter = 0.015; // small chance for scatter

            var result = new SlotReels();

            for (int r = 0; r < rows; r++)
            {
                var row = new SlotReel();
                wildsPlaces[r] = new List<int>();

                for (int c = 0; c < cols; c++)
                {
                    bool isWild = Rng.Value.NextDouble() <= percentToGetWild;
                    bool isScatter = !isWild && Rng.Value.NextDouble() <= percentToGetScatter;

                    var item = new SlotItem
                    {
                        Index = c,
                        Type = isWild ? 'W' : isScatter ? 'S' : BaseSymbols[Rng.Value.Next(0, BaseSymbols.Count)]
                    };

                    if (isWild) wildsPlaces[r].Add(c);
                    row.Add(item);
                }

                result.Add(row);
            }

            return IssueWilds(wildsPlaces, result);
        }

        // Apply wilds to the generated matrix. The wildsPlaces map is keyed by row index
        // and contains column indices that should be replaced with the WILD symbol.
        private SlotReels IssueWilds(Dictionary<int, List<int>> wildsPlaces, SlotReels slotRoll)
        {
            if (wildsPlaces == null || wildsPlaces.Count == 0) return slotRoll;

            foreach (var kv in wildsPlaces)
            {
                int rowIdx = kv.Key;
                if (rowIdx < 0 || rowIdx >= slotRoll.Length) continue;

                var cols = kv.Value;
                for (int i = 0; i < cols.Count; i++)
                {
                    int colIdx = cols[i];
                    if (colIdx < 0 || colIdx >= slotRoll.Reels[rowIdx].Length) continue;

                    var reel = slotRoll.Reels[rowIdx];
                    var items = reel.Items.ToList();
                    var si = items[colIdx];
                    si.Type = 'W';
                    items[colIdx] = si;

                    var updatedReel = new SlotReel(items);
                    var newReels = slotRoll.Reels.ToList();
                    newReels[rowIdx] = updatedReel;

                    var newSlotRoll = new SlotReels();
                    foreach (var r in newReels) newSlotRoll.Add(r);
                    slotRoll = newSlotRoll;
                }
            }

            return slotRoll;
        }

        // Award free spins to the player; this is intentionally simple so the caller controls
        // how and when free spins are consumed in the main game loop.
        public void AwardFreeSpins(int count)
        {
            if (count <= 0) return;
            FreeSpins += count;
            Program.PrintLine($"Awarded {count} Free Spins! Total Free Spins: {FreeSpins}");
            try
            {
                var mp = new MediaPlayer();
                mp.Open(new Uri(Path.Combine(Directory.GetCurrentDirectory(), "sounds", "freespin.wav")));
                mp.Volume = 0.5;
                mp.Play();
                Thread.Sleep(1200);
                mp.Stop();
            }
            catch { }
        }

        // Start and consume free spins. Free spins run automatically but suppress the
        // interactive free-spin prompt to avoid nested prompts during the bonus game.
        // Multiplier stacks during the free-spins session based on WILD occurrences.
        public void PlayFreeSpins(int spins, double betAmount)
        {
            if (spins <= 0 || FreeSpins <= 0) return;

            int toPlay = Math.Min(spins, FreeSpins);
            Program.PrintLine($"Starting {toPlay} Free Spins...");

            // initialize free-spin mode and multiplier
            IsInFreeSpinMode = true;
            FreeSpinMultiplier = 1;
            MultiplierColumnIndex = -1;

            while (toPlay > 0 && FreeSpins > 0)
            {
                var generated = GenerateSpin();
                AnimateRoll(2);
                ClearConsoleAndPrintSpin(generated, betAmount);

                // Determine wild positions before applying payouts so the current multiplier applies
                const int cols = 5;
                var wildColumns = new List<int>();
                for (int c = 0; c < cols; c++)
                {
                    if (generated.Reels.Any(r => c < r.Length && r.Items[c].Type == 'W'))
                        wildColumns.Add(c);
                }
                int wildCount = wildColumns.Count;

                // If there are wilds, pick the first column to display the multiplier indicator
                if (wildColumns.Count > 0)
                    MultiplierColumnIndex = wildColumns[0];

                // Apply wins with the current multiplier
                CalculateWins(generated, betAmount, promptForFreeSpins: false, multiplier: FreeSpinMultiplier);

                // After applying wins, increase multiplier by the number of wilds (stacking), capped
                if (wildCount > 0)
                {
                    FreeSpinMultiplier = Math.Min(MaxFreeSpinMultiplier, FreeSpinMultiplier + wildCount);
                    Program.PrintLine($"Free spin multiplier increased to x{FreeSpinMultiplier}!");
                }

                FreeSpins--;
                toPlay--;
            }

            IsInFreeSpinMode = false;
            MultiplierColumnIndex = -1;
            Program.PrintLine($"Free Spins complete. Remaining Free Spins: {FreeSpins}");
        }

        private void ClearConsoleAndPrintSpin(SlotReels generated, double betAmount)
        {
            Program.Clear();
            PrintSlotSpin(generated);
            if (IsInFreeSpinMode)
                Program.PrintLine($"{new string(' ', 21)}Bet amount: ${betAmount};     Balance Available: ${_player.Balance}      Coin Value: ${betAmount / 5} for each line.    FreeSpin Multiplier: x{FreeSpinMultiplier}");
            else
                Program.PrintLine($"{new string(' ', 21)}Bet amount: ${betAmount};     Balance Available: ${_player.Balance}      Coin Value: ${betAmount / 5} for each line.");
        }

        // Offer the player to buy free spins after a normal spin. The price is derived
        // from the current bet amount to keep the offer proportional to the stake.
        // This method is idempotent (will not award if player refuses or lacks funds).
        public void OfferPurchaseFreeSpins(double betAmount, int count = 10)
        {
            if (count <= 0) return;
            if (FreeSpins > 0) return; // don't offer if player already has free spins

            double price = Math.Round(Math.Max(1.0, betAmount) * 10.0, 2); // 10x the bet as the purchase price

            Program.PrintLine($"Would you like to buy {count} Free Spins for ${price}? (Y/N)");
            var resp = Console.ReadLine();
            if (string.IsNullOrEmpty(resp)) return;

            if (resp.Equals("Y", StringComparison.OrdinalIgnoreCase) || resp.Equals("Yes", StringComparison.OrdinalIgnoreCase))
            {
                if (_player.Balance < price)
                {
                    Program.PrintLine("Insufficient balance to purchase Free Spins.");
                    return;
                }

                _player.Add(-price);
                AwardFreeSpins(count);

                Program.PrintLine($"Free Spins purchased for ${price}. Start now? (Y/N)");
                var startResp = Console.ReadLine();
                if (!string.IsNullOrEmpty(startResp) && (startResp.Equals("Y", StringComparison.OrdinalIgnoreCase) || startResp.Equals("Yes", StringComparison.OrdinalIgnoreCase)))
                {
                    PlayFreeSpins(count, betAmount);
                }
                else
                {
                    Program.PrintLine("Free Spins saved. You can start them later with the appropriate command.");
                }
            }
        }

        // Simple coinflip and bounded random helper using our thread-local RNG
        public bool CoinflipResult() => Rng.Value.Next(0, 2) == 1;
        public int FlipResult(int min, int max) => (min >= max) ? min : Rng.Value.Next(min, max);

        private class WinResult
        {
            public string Category { get; set; }
            public int Line { get; set; }
            public char Symbol { get; set; }
            public int Count { get; set; }
            public double Amount { get; set; }
        }

        // Centralized win calculation. Produces a list of WinResult entries and then
        // applies payouts and UI feedback in a single, easy-to-follow pass.
        // When promptForFreeSpins is true the player will be asked whether they want
        // to start the awarded free spins immediately after payouts are applied.
        public void CalculateWins(SlotReels slot, double betAmount, bool promptForFreeSpins = true, int multiplier = 1)
        {
            if (slot.Length == 0) return;

            // Detect scatter hits but defer awarding until after payouts are applied
            int scatterCount = slot.Reels.SelectMany(r => r.Items).Count(i => i.Type == 'S');
            int pendingFreeSpins = scatterCount >= 3 ? 10 : 0;

            const int rows = 3;
            const int cols = 5;

            var wins = new List<WinResult>();

            // Horizontal wins (per row)
            var horizontalPattern = new Regex("(.)\\1{2,}");
            for (int r = 0; r < slot.Length; r++)
            {
                var reel = slot.Reels[r];
                var line = string.Concat(reel.Items.Select(i => i.Type));
                var match = horizontalPattern.Match(line);
                if (match.Success)
                {
                    char sym = match.Value[0];
                    int timesRepeated = line.Count(x => x == sym);
                    double amount = ComputeHorizontalPayout(sym, timesRepeated, betAmount);
                    wins.Add(new WinResult { Category = "HORIZONTAL", Line = r + 1, Symbol = sym, Count = timesRepeated, Amount = amount });
                }
            }

            // Diagonal wins (top-left -> bottom-right) across columns. We evaluate all diagonals
            // that span exactly `rows` symbols (i.e., start columns 0..cols-rows).
            for (int startCol = 0; startCol <= cols - rows; startCol++)
            {
                var diagChars = new List<char>();
                for (int r = 0; r < rows; r++)
                {
                    int c = startCol + r;
                    if (c < slot.Reels[r].Length)
                        diagChars.Add(slot.Reels[r].Items[c].Type);
                }

                var diag = string.Concat(diagChars);
                var match = horizontalPattern.Match(diag);
                if (match.Success)
                {
                    char sym = match.Value[0];
                    int times = diag.Count(x => x == sym);
                    double amount = (WinningTable.ContainsKey(sym) ? WinningTable[sym] : 0) * (betAmount / 5) * times * 11;
                    wins.Add(new WinResult { Category = "DIAGONAL", Line = startCol + 1, Symbol = sym, Count = times, Amount = amount });
                }
            }

            // Vertical wins (per column) - we expect rows tall sequences
            for (int c = 0; c < cols; c++)
            {
                var colChars = new List<char>();
                for (int r = 0; r < slot.Length; r++)
                {
                    if (c < slot.Reels[r].Length)
                        colChars.Add(slot.Reels[r].Items[c].Type);
                }

                var col = string.Concat(colChars);
                var match = horizontalPattern.Match(col);
                if (match.Success)
                {
                    char sym = match.Value[0];
                    double amount = (WinningTable.ContainsKey(sym) ? WinningTable[sym] : 0) * (betAmount / 5) * rows * 20;
                    wins.Add(new WinResult { Category = "VERTICAL", Line = c + 1, Symbol = sym, Count = rows, Amount = amount });
                }
            }

            // No wins case
            if (wins.Count == 0)
            {
                Program.PrintLine("No winnings, Try Again.");
                _player.Add(-betAmount);
                Program.PrintLine(_player.ToString());

                // If there were pending free spins (from scatters) still award them but do not prompt
                if (pendingFreeSpins > 0)
                    AwardFreeSpins(pendingFreeSpins);

                return;
            }

            // Payouts and presentation
            Program.PrintLine("");
            Program.PrintLine(new string(' ', 49) + new string('*', 20));
            Program.PrintLine(new string(' ', 53) + "W I N N E R   ");
            Program.PrintLine(new string(' ', 49) + new string('*', 20));
            Program.PrintLine("");

            var winsTotal = 0.0;
            foreach (var w in wins)
            {
                // Apply free-spin multiplier only when > 1. Multiplier provided by caller (free spins)
                double appliedAmount = w.Amount * Math.Max(1, multiplier);
                winsTotal += appliedAmount;
                string symbolDescription = w.Symbol == 'W' ? "special triggered bonus from WILD symbol" : w.Symbol.ToString();
                if (w.Category == "HORIZONTAL")
                    Program.PrintLine($"Won ${appliedAmount} at line {w.Line} by symbol {symbolDescription} repeated {w.Count} times");
                else if (w.Category == "DIAGONAL")
                    Program.PrintLine($"Won ${appliedAmount} on diagonal starting at column {w.Line} by symbol {symbolDescription} repeated {w.Count} times");
                else
                    Program.PrintLine($"Won ${appliedAmount} at vertical line {w.Line} by symbol {symbolDescription} repeated {w.Count} times");

                _player.Add(appliedAmount);
            }

            Program.PrintLine($"Total win is ${winsTotal} with a bet amount of ${betAmount}");

            // Play a sound for big wins; keep player experience code isolated and simple
            try
            {
                var mp = new MediaPlayer();
                if (winsTotal > 500)
                {
                    mp.Open(new Uri(Path.Combine(Directory.GetCurrentDirectory(), "sounds", "bigwin.wav")));
                    mp.Volume = 0.6;
                }
                else
                {
                    mp.Open(new Uri(Path.Combine(Directory.GetCurrentDirectory(), "sounds", "win.wav")));
                    mp.Volume = 0.4;
                }

                mp.Play();
                Thread.Sleep(2000);
                mp.Stop();
            }
            catch
            {
                // Non-critical: if audio fails just continue
            }

            // After payouts, if scatter trigger occurred, award free spins and optionally prompt
            if (pendingFreeSpins > 0)
            {
                AwardFreeSpins(pendingFreeSpins);

                if (promptForFreeSpins)
                {
                    Program.PrintLine("You have been awarded free spins. Would you like to start the Free Spins bonus now? (Y/N)");
                    var response = Console.ReadLine();
                    if (!string.IsNullOrEmpty(response) && (response.Equals("Y", StringComparison.OrdinalIgnoreCase) || response.Equals("Yes", StringComparison.OrdinalIgnoreCase)))
                    {
                        PlayFreeSpins(pendingFreeSpins, betAmount);
                    }
                    else
                    {
                        Program.PrintLine("Free Spins saved. You can start them later.");
                    }
                }
            }
        }

        // Convert a reel to a string representation
        public string AsString(SlotReel reel)
        {
            return string.Concat(reel.Items.Select(i => i.Type));
        }

        // Nicely print the current spin in row-major layout
        public void PrintSlotSpin(SlotReels slot)
        {
            // We print rows; columns are aligned starting at column 45 with 3 chars per column
            const int leftPad = 45;
            const int colWidth = 3; // e.g. 'A' + two spaces
            int rows = slot.Length;
            int cols = slot.Reels.Count > 0 ? slot.Reels[0].Length : 0;

            for (int r = 0; r < rows; r++)
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.Write(new string(' ', leftPad));
                for (int c = 0; c < cols; c++)
                {
                    Console.Write(slot.Reels[r].Items[c].Type + "  ");
                }
                Console.WriteLine();
                Console.WriteLine();
            }

            // If in free-spin mode and we have a column with an active multiplier, show it beneath the board
            if (IsInFreeSpinMode && MultiplierColumnIndex >= 0 && cols > MultiplierColumnIndex)
            {
                // Move to next line and print indicator aligned to column
                Console.WriteLine();
                int indicatorLeft = leftPad + MultiplierColumnIndex * colWidth + 1; // center under the column
                try
                {
                    if (indicatorLeft >= 0)
                    {
                        Console.SetCursorPosition(indicatorLeft, Console.CursorTop - 1);
                        Console.Write($"x{FreeSpinMultiplier}");
                    }
                }
                catch
                {
                    // ignore if console size doesn't allow positioning
                }
                Console.WriteLine();
            }
        }

        // Basic animation for the console. Kept lightweight to avoid blocking too long.
        public void AnimateRoll(int seconds)
        {
            var carretPosY = 0;
            Program.Clear();
            Program.PrintLine($"{new string(' ', 45)}{new string('*', 20)}");
            Program.PrintLine($"{new string(' ', 45)}{new string('*', 1)}{new string(' ', 4)} ROLLING {new string(' ', 4)}*");
            Program.PrintLine($"{new string(' ', 45)}{new string('*', 20)}");

            int timesRepeated = Math.Max(1, seconds) * 2;

            for (int i = 0; i < timesRepeated; i++)
            {
                try
                {
                    var mp = new MediaPlayer();
                    mp.Open(new Uri(Path.Combine(Directory.GetCurrentDirectory(), "sounds", "roll.wav")));
                    mp.Play();
                }
                catch { }

                for (int row = 0; row < 3; row++)
                {
                    Console.SetCursorPosition(0, 6 + row * 2);
                    Console.Write(new string(' ', 42));
                    for (int k = 0; k < 5; k++)
                    {
                        Console.Write(GetRandomChar() + "     ");
                        Thread.Sleep(20);
                    }
                    Console.WriteLine();
                    Console.WriteLine();
                }
            }
        }

        private char GetRandomChar()
        {
            const string valid = "AK345W";
            return valid[Rng.Value.Next(0, valid.Length)];
        }

        private string GetRandomLine(int length)
        {
            const string valid = "abcdef0123456789";
            var sb = new System.Text.StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append(valid[Rng.Value.Next(0, valid.Length)]);
            return sb.ToString();
        }

        // Compute horizontal payout based on original payout rules with defensive checks.
        // Rules derived from previous implementation:
        // - 3 in a row: baseMultiplier = WinningTable[sym] * (betAmount / 5)
        // - 4 in a row: base * timesRepeated
        // - 5 in a row: base * timesRepeated * 10
        private double ComputeHorizontalPayout(char sym, int timesRepeated, double betAmount)
        {
            if (timesRepeated < 3) return 0.0;
            if (!WinningTable.ContainsKey(sym)) return 0.0;

            double baseValue = WinningTable[sym] * (betAmount / 5.0);

            return timesRepeated switch
            {
                3 => baseValue,
                4 => baseValue * timesRepeated,
                5 => baseValue * timesRepeated * 10,
                _ => baseValue * timesRepeated
            };
        }
    }
}
