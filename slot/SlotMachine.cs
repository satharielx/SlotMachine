using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace slot
{
    public struct SlotItem
    {
        public int Index { get; set; }
        public char Type { get; set; }
    }

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
        public int Length => Reels.Count;
    }

    public class SlotMachine
    {
        private readonly PlayerBase _player;
        private readonly Random _random = new Random();
        private readonly double[] _betOptions = { 1.50, 3.00, 7.50, 15.00, 30.00, 75.00, 150.00 };
        private readonly int[] _reelStops = { 8, 14, 20, 26, 32 };
        private SlotReels _preGeneratedResult;
        private SlotReels _animationSymbols;
        private ConsoleCanvas _previousFrame;
        private List<int[]> _winningPaths = new List<int[]>();
        private int _betIndex = 1;
        private int _animationFrame = -1;
        private int _anticheatPage;
        private int _spins;
        private int _view;
        private bool _isFreeSpin;
        private bool _highlight;
        private bool _choosingBonus;
        private bool _quitRequested;
        private double _lastWin;
        private string _message = "Press SPACE to spin.";

        public int FreeSpinsLeft { get; private set; }
        public double CurrentBet => _betOptions[_betIndex];

        public SlotMachine(PlayerBase player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _preGeneratedResult = GenerateSpin();
        }

        public SlotReels GenerateSpin()
        {
            var result = new SlotReels();
            for (int row = 0; row < 3; row++)
            {
                var reel = new SlotReel();
                for (int column = 0; column < 5; column++)
                {
                    double chance = _random.NextDouble();
                    char symbol = chance < 0.04 ? 'W' : chance < 0.07 ? 'S' : "AK345"[_random.Next(5)];
                    reel.Add(new SlotItem { Index = column, Type = symbol });
                }
                result.Add(reel);
            }
            return result;
        }

        public void AnimateRoll()
        {
            _preGeneratedResult = GenerateSpin();
            _winningPaths.Clear();
            _message = _isFreeSpin ? "Free spin. Wins pay triple." : "Spinning...";
            for (int frame = 0; frame < 35; frame++)
            {
                _animationFrame = frame;
                _animationSymbols = GenerateSpin();
                DrawUIFrame();
                Thread.Sleep(30);
            }
            _animationFrame = -1;
            DrawUIFrame();
        }

        public void CalculateWins()
        {
            var result = SlotRules.Evaluate(_preGeneratedResult, CurrentBet, _isFreeSpin);
            _lastWin = result.Win;
            _winningPaths = result.WinningPaths;
            if (result.Win > 0)
            {
                _player.Add(result.Win);
                _message = $"You won ${result.Win:N2} on {result.WinningPaths.Count} {(result.WinningPaths.Count == 1 ? "line" : "lines")}.";
                for (int flash = 0; flash < 4; flash++)
                {
                    _highlight = flash % 2 == 0;
                    Wait(160);
                }
                _highlight = true;
            }
            else _message = "No win this time.";
            if (result.Scatters >= 3) RunScatterBonus();
            DrawUIFrame();
        }

        private void RunScatterBonus()
        {
            _choosingBonus = true;
            _view = 0;
            _message = "Pick a treasure: 1, 2 or 3.";
            int[] treasures = { _random.Next(5, 15), _random.Next(5, 15), _random.Next(5, 15) };
            int choice = 0;
            while (!_quitRequested)
            {
                var key = ReadGameKey();
                if (key == ConsoleKey.D1 || key == ConsoleKey.NumPad1) break;
                if (key == ConsoleKey.D2 || key == ConsoleKey.NumPad2) { choice = 1; break; }
                if (key == ConsoleKey.D3 || key == ConsoleKey.NumPad3) { choice = 2; break; }
                if (key == ConsoleKey.Q || key == ConsoleKey.Escape) _quitRequested = true;
            }
            double bonus = CurrentBet * treasures[choice];
            _player.Add(bonus);
            _lastWin += bonus;
            FreeSpinsLeft += 15;
            _choosingBonus = false;
            _message = $"You found ${bonus:N2} and 15 free spins!";
            Wait(1200);
        }

        public void RunGameLoop()
        {
            bool cursorVisible = Console.CursorVisible;
            ConsoleColor foreground = Console.ForegroundColor;
            ConsoleColor background = Console.BackgroundColor;
            _quitRequested = false;
            _view = 0;
            _previousFrame = null;
            try
            {
                Console.CursorVisible = false;
                Console.Clear();
                while (!_quitRequested)
                {
                    DrawUIFrame();
                    if (FreeSpinsLeft > 0)
                    {
                        Wait(650);
                        if (_quitRequested) break;
                        FreeSpinsLeft--;
                        _isFreeSpin = true;
                        try { Spin(); }
                        finally { _isFreeSpin = false; }
                        continue;
                    }
                    var key = ReadGameKey();
                    if (key == ConsoleKey.Spacebar)
                    {
                        if (_player.TrySpend(CurrentBet)) Spin();
                        else _message = "Not enough credits. Press C to add $500.";
                    }
                    else if (key == ConsoleKey.UpArrow) _betIndex = Math.Min(_betIndex + 1, _betOptions.Length - 1);
                    else if (key == ConsoleKey.DownArrow) _betIndex = Math.Max(_betIndex - 1, 0);
                    else if (key == ConsoleKey.F)
                    {
                        if (_player.TrySpend(CurrentBet * 50)) { FreeSpinsLeft += 15; _message = "15 free spins ready. Wins pay triple."; }
                        else _message = $"You need ${CurrentBet * 50:N2} for the bonus. C adds credits.";
                    }
                    else if (key == ConsoleKey.Q || key == ConsoleKey.Escape) _quitRequested = true;
                }
            }
            finally
            {
                Console.ForegroundColor = foreground;
                Console.BackgroundColor = background;
                Console.CursorVisible = cursorVisible;
                Console.Clear();
            }
        }

        private void Spin()
        {
            _spins++;
            _lastWin = 0;
            _highlight = false;
            AnimateRoll();
            CalculateWins();
        }

        private bool HandleNavigation(ConsoleKey key)
        {
            if (key == ConsoleKey.Tab) _view = _view == 1 ? 0 : 1;
            else if (key == ConsoleKey.H) _view = _view == 2 ? 0 : 2;
            else if (key == ConsoleKey.PageDown) _anticheatPage++;
            else if (key == ConsoleKey.PageUp) _anticheatPage = Math.Max(0, _anticheatPage - 1);
            else if (key == ConsoleKey.C || key == ConsoleKey.Z)
            {
                _player.Add(500);
                _message = "$500 added to your balance.";
            }
            else return false;
            return true;
        }

        private ConsoleKey ReadGameKey()
        {
            while (true)
            {
                DrawUIFrame();
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (!HandleNavigation(key)) return key;
                }
                Thread.Sleep(80);
            }
        }

        private void Wait(int milliseconds)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                DrawUIFrame();
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q || key == ConsoleKey.Escape) _quitRequested = true;
                    else HandleNavigation(key);
                }
                Thread.Sleep(40);
            } while (timer.ElapsedMilliseconds < milliseconds && !_quitRequested);
        }

        private void DrawUIFrame()
        {
            if (Console.IsOutputRedirected) return;
            int width = Math.Max(1, Math.Min(Console.WindowWidth, Console.BufferWidth) - 1);
            int height = Math.Max(1, Math.Min(Console.WindowHeight, Console.BufferHeight) - 1);
            var canvas = ComposeFrame(width, height);
            _previousFrame = canvas.Present(_previousFrame) ? canvas : null;
        }

        private ConsoleCanvas ComposeFrame(int width, int height)
        {
            var canvas = new ConsoleCanvas(width, height);
            if (width < 75 || height < 23)
            {
                canvas.Center(0, Math.Max(0, height / 2 - 1), width, "A U R U M", ConsoleColor.Yellow);
                canvas.Center(0, height / 2 + 1, width, "Resize the terminal to at least 76 x 24 to play.");
                canvas.Center(0, height / 2 + 2, width, "Q returns to the command menu.", ConsoleColor.DarkGray);
            }
            else
            {
                canvas.Write(2, 0, "A U R U M", ConsoleColor.Yellow);
                canvas.Write(16, 0, "15 lines. 5 reels.", ConsoleColor.DarkGray);
                canvas.Write(width - 13, 0, "Free play", ConsoleColor.DarkGray);
                canvas.Write(2, 1, new string('─', width - 4), ConsoleColor.DarkGray);
                if (_view == 1) DrawSecurity(canvas, 2, 3, width - 4, height - 7);
                else if (_view == 2) DrawRules(canvas);
                else
                {
                    bool sidebar = width >= 112;
                    int gameWidth = sidebar ? width - 47 : width - 4;
                    DrawGame(canvas, 2, 3, gameWidth, height - 7);
                    if (sidebar) DrawSecurity(canvas, gameWidth + 4, 3, width - gameWidth - 6, height - 7);
                }
                canvas.Write(2, height - 3, new string('─', width - 4), ConsoleColor.DarkGray);
                canvas.Write(2, height - 2, ConsoleCanvas.Fit("SPACE Spin  ↑↓ Bet  C +$500  F Bonus  TAB Anti-cheat  H Help  Q Back", width - 4), ConsoleColor.Gray);
                canvas.Write(2, height - 1, ConsoleCanvas.Fit(_view == 1 ? "PgUp / PgDn to see more. TAB returns to the game." : Anticheat.StatusText, width - 4), ConsoleColor.DarkGray);
            }
            return canvas;
        }

        private void DrawGame(ConsoleCanvas canvas, int x, int y, int width, int height)
        {
            int metricWidth = (width - 2) / 3;
            DrawMetric(canvas, x, y, metricWidth, "Balance", $"${_player.Balance:N2}", ConsoleColor.Cyan, true);
            DrawMetric(canvas, x + metricWidth + 1, y, metricWidth, "Bet", $"${CurrentBet:N2}", ConsoleColor.White);
            DrawMetric(canvas, x + (metricWidth + 1) * 2, y, width - (metricWidth + 1) * 2, "Last win", $"${_lastWin:N2}", ConsoleColor.Yellow);
            int reelY = y + 5;
            bool compact = canvas.Height < 27;
            string mode = _animationFrame >= 0 ? "Spinning..." : _isFreeSpin || FreeSpinsLeft > 0 ? "Free spins - triple wins" : "15 paylines";
            canvas.Box(x, reelY - 1, width, compact ? 11 : 14, mode, ConsoleColor.DarkGray);
            int reelX = x + (width - 49) / 2;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    bool stopped = _animationFrame < 0 || _animationFrame > _reelStops[column];
                    char symbol = stopped ? _preGeneratedResult.Reels[row].Items[column].Type : _animationSymbols.Reels[row].Items[column].Type;
                    bool winner = stopped && _highlight && _winningPaths.Any(path => path.Length > column && path[column] == row);
                    ConsoleColor color = !stopped ? ConsoleColor.DarkGray : symbol == 'W' ? ConsoleColor.Yellow : symbol == 'S' ? ConsoleColor.Magenta : symbol == 'A' ? ConsoleColor.Cyan : symbol == 'K' ? ConsoleColor.White : ConsoleColor.Gray;
                    ConsoleColor background = winner ? ConsoleColor.DarkGreen : ConsoleColor.Black;
                    int tileX = reelX + column * 10;
                    int tileY = reelY + row * (compact ? 3 : 4);
                    canvas.Write(tileX, tileY, "╭───────╮", winner ? ConsoleColor.Green : ConsoleColor.DarkGray, background);
                    canvas.Write(tileX, tileY + 1, "│       │", color, background);
                    canvas.Write(tileX + 4, tileY + 1, symbol.ToString(), color, background);
                    canvas.Write(tileX, tileY + 2, "╰───────╯", winner ? ConsoleColor.Green : ConsoleColor.DarkGray, background);
                }
            }
            string message = _choosingBonus ? "Pick a treasure:  [1] Left   [2] Middle   [3] Right" : _message;
            canvas.Center(x + 1, y + (compact ? 15 : 18), width - 2, message, _choosingBonus ? ConsoleColor.Magenta : _lastWin > 0 ? ConsoleColor.Green : ConsoleColor.Gray);
            canvas.Write(x + 1, y + (compact ? 16 : 19), ConsoleCanvas.Fit($"Spins: {_spins}    Free spins: {FreeSpinsLeft}    Bonus: ${CurrentBet * 50:N2}", width - 2), ConsoleColor.DarkGray);
            if (height > 21) canvas.Write(x + 1, y + height - 1, ConsoleCanvas.Fit(Anticheat.StatusText, width - 2), ConsoleColor.DarkCyan);
        }

        private static void DrawMetric(ConsoleCanvas canvas, int x, int y, int width, string label, string value, ConsoleColor color, bool addCredits = false)
        {
            canvas.Box(x, y, width, 4, label);
            canvas.Center(x + 1, y + (addCredits ? 1 : 2), width - 2, value, color);
            if (addCredits) canvas.Center(x + 1, y + 2, width - 2, "[C] Add $500", ConsoleColor.Green);
        }

        private void DrawSecurity(ConsoleCanvas canvas, int x, int y, int width, int height)
        {
            canvas.Box(x, y, width, height, "Anti-cheat", ConsoleColor.DarkCyan);
            int inside = width - 4;
            canvas.Write(x + 2, y + 2, ConsoleCanvas.Fit(Anticheat.StatusText, inside), ConsoleColor.Yellow);
            canvas.Write(x + 2, y + 3, ConsoleCanvas.Fit(Anticheat.HasDebugPrivilege ? "Running as administrator" : "Running in usermode without elevation", inside), ConsoleColor.DarkGray);
            canvas.Write(x + 2, y + 4, ConsoleCanvas.Fit("Balance tampering detection is ON.", inside), ConsoleColor.DarkGray);
            canvas.Write(x + 2, y + 5, new string('─', Math.Max(0, inside)), ConsoleColor.DarkGray);
            var lines = Anticheat.ProcessLines;
            int capacity = Math.Max(1, (height - 10) / 3);
            int pages = Math.Max(1, (lines.Count / 2 + capacity - 1) / capacity);
            _anticheatPage = Math.Min(_anticheatPage, pages - 1);
            for (int index = 0; index < capacity; index++)
            {
                int processIndex = (_anticheatPage * capacity + index) * 2;
                if (processIndex >= lines.Count) break;
                int rowY = y + 6 + index * 3;
                canvas.Write(x + 2, rowY, ConsoleCanvas.Fit(lines[processIndex], inside), ConsoleColor.White);
                string access = lines[processIndex + 1].Trim();
                canvas.Write(x + 2, rowY + 1, ConsoleCanvas.Fit(access, inside), ConsoleColor.DarkCyan);
            }
            if (lines.Count == 0) canvas.Write(x + 2, y + 7, "No other apps connected.", ConsoleColor.DarkGray);
            canvas.Write(x + 2, y + height - 3, ConsoleCanvas.Fit($"{lines.Count / 2} connected apps. Page {_anticheatPage + 1}/{pages}.", inside), ConsoleColor.Gray);
            canvas.Write(x + 2, y + height - 2, ConsoleCanvas.Fit("RPM/WPM access flags detection is ON.", inside), ConsoleColor.DarkGray);
        }

        private void DrawRules(ConsoleCanvas canvas)
        {
            int width = canvas.Width - 4;
            canvas.Box(2, 3, width, canvas.Height - 7, "How to play", ConsoleColor.DarkCyan);
            string[] rules = {
                "Press SPACE to spin. Use the arrow keys to change your bet.",
                "Match at least three symbols from left to right on a payline.",
                "All 15 paylines are included in your bet.",
                "W is wild: it fills in for any regular symbol.",
                "A winning line with a wild and regular symbols pays double.",
                "Land three or more S symbols to pick a treasure.",
                "You'll win 5-14 times your bet, plus 15 free spins.",
                "During free spins, line wins pay triple.",
                "Press F to buy 15 free spins for 50 times your bet.",
                "Running low? Press C to add $500 whenever you like."
            };
            for (int i = 0; i < rules.Length; i++)
                canvas.Write(5, 5 + i, ConsoleCanvas.Fit(rules[i], width - 6), i % 2 == 0 ? ConsoleColor.White : ConsoleColor.Gray);
            canvas.Write(5, 16, ConsoleCanvas.Fit("Line payout: matches x 1.5 x your bet / 15.", width - 6), ConsoleColor.DarkGray);
            canvas.Write(5, 17, ConsoleCanvas.Fit("It's all play money. H takes you back.", width - 6), ConsoleColor.DarkCyan);
        }
    }
}
