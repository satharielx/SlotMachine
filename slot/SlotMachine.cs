using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
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

    public class SlotMachine : IDisposable
    {
        private readonly PlayerBase _player;
        private readonly Random _visualRandom = new Random();
        private readonly IRandomSource _outcomeRandom;
        private readonly GameEngine _engine;
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
        private bool _highlight;
        private bool _quitRequested;
        private decimal _lastWin;
        private RoundStep _step;
        private bool _featurePaused;
        private int _rulesPage;
        private string _message = "Press SPACE to spin.";

        public int FreeSpinsLeft => _engine.FreeSpinsLeft;
        public double CurrentBet => (double)(_engine.Mode == GameMode.BaseGame ? SlotRules.BetOptions[_betIndex] : _engine.FeatureBet);

        public SlotMachine(PlayerBase player) : this(player, new CryptoRandomSource()) { }

        internal SlotMachine(PlayerBase player, IRandomSource random)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _outcomeRandom = random ?? throw new ArgumentNullException(nameof(random));
            _engine = new GameEngine(new PlayerWallet(player), random);
            _preGeneratedResult = GenerateVisualBoard();
        }

        public void Dispose() => (_outcomeRandom as IDisposable)?.Dispose();

        private SlotReels GenerateVisualBoard(bool coins = false)
        {
            var symbols = new char[3, 5];
            string choices = coins ? "O...." : "345AKCWSO";
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 5; column++) symbols[row, column] = choices[_visualRandom.Next(choices.Length)];
            return SlotRules.Board(symbols);
        }

        private void AnimateRoll(RoundStep step)
        {
            _preGeneratedResult = step.Board;
            _winningPaths.Clear();
            _message = step.CoinRespin ? "Looking for more coins..." : step.FreeSpin ? $"Free spin at {step.Multiplier}x." : "Spinning...";
            try
            {
                for (int frame = 0; frame < 35; frame++)
                {
                    _animationFrame = frame;
                    _animationSymbols = GenerateVisualBoard(step.CoinRespin);
                    DrawUIFrame();
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Q || key == ConsoleKey.Escape) { _quitRequested = true; break; }
                        HandleNavigation(key);
                    }
                    Thread.Sleep(30);
                }
            }
            finally { _animationFrame = -1; }
        }

        private void PlayStep(bool feature)
        {
            RoundStep step;
            try
            {
                step = feature ? _engine.BeginFeatureStep() : _engine.BeginPaidSpin(SlotRules.BetOptions[_betIndex]);
            }
            catch (CryptographicException)
            {
                _featurePaused = feature;
                _message = feature ? "Random source unavailable. SPACE retries the bonus." : "Random source unavailable. Your bet was refunded.";
                return;
            }
            if (step == null) { _message = "Not enough credits. Press C to add $500."; return; }
            _step = step;
            _featurePaused = false;
            _highlight = false;
            if (!step.CoinRespin) { _spins++; _lastWin = 0; }
            try { AnimateRoll(step); }
            finally { _engine.CompleteStep(step.Id); }
            _lastWin += step.Payout;
            _winningPaths = step.Lines?.WinningPaths ?? new List<int[]>();
            if (step.CoinFinished) _message = $"Coin bonus paid ${step.CoinPayout:N2}!";
            else if (step.CoinStarted || step.CoinRespin) _message = $"${step.CoinTotal:N2} locked. {step.Respins} respins left.";
            else if (step.FreeSpinsAdded > 0) _message = $"{step.FreeSpinsAdded} free spins added! Line win: ${step.Payout:N2}.";
            else _message = step.Payout > 0 ? $"You won ${step.Payout:N2}." : "No win this time.";
            if (step.Payout > 0)
            {
                for (int flash = 0; flash < 4; flash++) { _highlight = flash % 2 == 0; Wait(140); }
                _highlight = true;
            }
            DrawUIFrame();
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
                    if (_engine.Mode != GameMode.BaseGame && !_featurePaused)
                    {
                        Wait(650);
                        if (_quitRequested) break;
                        PlayStep(true);
                        continue;
                    }
                    var key = ReadGameKey();
                    if (key == ConsoleKey.Spacebar) PlayStep(_engine.Mode != GameMode.BaseGame);
                    else if (_engine.Mode == GameMode.BaseGame && key == ConsoleKey.UpArrow) _betIndex = Math.Min(_betIndex + 1, SlotRules.BetOptions.Length - 1);
                    else if (_engine.Mode == GameMode.BaseGame && key == ConsoleKey.DownArrow) _betIndex = Math.Max(_betIndex - 1, 0);
                    else if (_engine.Mode == GameMode.BaseGame && key == ConsoleKey.F)
                    {
                        if (_engine.BuyFreeSpins(SlotRules.BetOptions[_betIndex])) _message = "15 free spins ready. Wilds stick and grow the multiplier.";
                        else _message = $"You need ${CurrentBet * SlotRules.BonusBuyMultiplier:N2} for the bonus. C adds credits.";
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

        private bool HandleNavigation(ConsoleKey key)
        {
            if (key == ConsoleKey.Tab) _view = _view == 1 ? 0 : 1;
            else if (key == ConsoleKey.H) _view = _view == 2 ? 0 : 2;
            else if (key == ConsoleKey.PageDown) { if (_view == 2) _rulesPage = Math.Min(1, _rulesPage + 1); else _anticheatPage++; }
            else if (key == ConsoleKey.PageUp) { if (_view == 2) _rulesPage = Math.Max(0, _rulesPage - 1); else _anticheatPage = Math.Max(0, _anticheatPage - 1); }
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
                canvas.Write(2, height - 1, ConsoleCanvas.Fit(_view == 1 ? "PgUp / PgDn to see more. TAB returns to the game." : _view == 2 ? "PgUp / PgDn switches between payouts and bonuses. H goes back." : Anticheat.StatusText, width - 4), ConsoleColor.DarkGray);
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
            bool coinBoard = _step != null && (_step.CoinRespin || _step.CoinStarted);
            string mode = coinBoard ? $"Coin bonus - {_step.Respins} respins" : _animationFrame >= 0 ? "Spinning..." : _engine.Mode == GameMode.FreeSpins ? $"Free spins - {_engine.Multiplier}x" : "15 paylines";
            canvas.Box(x, reelY - 1, width, compact ? 11 : 14, mode, ConsoleColor.DarkGray);
            int reelX = x + (width - 49) / 2;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    bool pinnedCoin = _step != null && _step.CoinRespin && _step.PreviousCoins[row, column] > 0;
                    bool pinnedWild = _step != null && _step.FreeSpin && _step.PreviousSticky[row, column];
                    bool stopped = pinnedCoin || pinnedWild || (_animationFrame < 0 || _animationFrame > _reelStops[column]);
                    char symbol = stopped ? _preGeneratedResult.Reels[row].Items[column].Type : _animationSymbols.Reels[row].Items[column].Type;
                    bool winner = stopped && _highlight && (_winningPaths.Any(path => path.Length > column && path[column] == row) || (_step != null && _step.CoinFinished && _step.CoinValues[row, column] > 0));
                    ConsoleColor color = !stopped ? ConsoleColor.DarkGray : symbol == 'W' ? ConsoleColor.Yellow : symbol == 'S' ? ConsoleColor.Magenta : symbol == 'C' || symbol == 'O' ? ConsoleColor.Yellow : symbol == 'A' ? ConsoleColor.Cyan : symbol == 'K' ? ConsoleColor.White : ConsoleColor.Gray;
                    ConsoleColor background = winner ? ConsoleColor.DarkGreen : ConsoleColor.Black;
                    int tileX = reelX + column * 10;
                    int tileY = reelY + row * (compact ? 3 : 4);
                    bool sticky = stopped && _step != null && _step.FreeSpin && _step.Sticky[row, column];
                    ConsoleColor border = winner ? ConsoleColor.Green : sticky ? ConsoleColor.Cyan : ConsoleColor.DarkGray;
                    canvas.Write(tileX, tileY, "╭───────╮", border, background);
                    canvas.Write(tileX, tileY + 1, "│       │", color, background);
                    string label = symbol == 'O' && stopped && _step != null && _step.CoinValues[row, column] > 0 ? (_step.CoinValues[row, column] / _step.Bet).ToString("0") + "x" : symbol == '.' ? "·" : symbol.ToString();
                    canvas.Center(tileX + 1, tileY + 1, 7, label, color, background);
                    canvas.Write(tileX, tileY + 2, "╰───────╯", border, background);
                }
            }
            canvas.Center(x + 1, y + (compact ? 15 : 18), width - 2, _message, _lastWin > 0 ? ConsoleColor.Green : ConsoleColor.Gray);
            canvas.Write(x + 1, y + (compact ? 16 : 19), ConsoleCanvas.Fit($"Spins: {_spins}  Free: {FreeSpinsLeft}  Next: {_engine.Multiplier}x  Bonus: ${CurrentBet * SlotRules.BonusBuyMultiplier:N2}", width - 2), ConsoleColor.DarkGray);
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
            string[] rules = _rulesPage == 0 ? new[] {
                "Paytable                     3       4       5 symbols",
                $"{"3, 4, 5",-24}{SlotRules.Payout('3', 3),3}x   {SlotRules.Payout('3', 4),4}x   {SlotRules.Payout('3', 5),4}x",
                $"{"A, K",-24}{SlotRules.Payout('A', 3),3}x   {SlotRules.Payout('A', 4),4}x   {SlotRules.Payout('A', 5),4}x",
                $"{"C (crown)",-24}{SlotRules.Payout('C', 3),3}x   {SlotRules.Payout('C', 4),4}x   {SlotRules.Payout('C', 5),4}x",
                $"{"W (wilds only)",-24}{SlotRules.Payout('W', 3),3}x   {SlotRules.Payout('W', 4),4}x   {SlotRules.Payout('W', 5),4}x",
                "",
                "Payouts multiply your line bet: total bet / 15.",
                "W substitutes. Only the best combination on each line pays.",
                "S is a scatter. O is a coin. Neither pays on a line.",
                "Free spins multiply line payouts. Coin prizes are separate.",
                "PgDn: bonus rules. C adds $500. H returns to the game."
            } : new[] {
                "Scatters on 3 / 4 / 5 reels award 8 / 12 / 20 free spins.",
                "Wilds stick for the feature. Cyan borders mark sticky wilds.",
                "Start at 2x. New wilds raise the NEXT spin's multiplier.",
                "Multiplier limit: 5x. Three scatter reels add five spins.",
                "At most 50 free spins can be awarded per feature.",
                "Free-spin reels have fewer new wilds; landed wilds stay.",
                "Six coins trigger three respins. Existing coins stay put.",
                "New coins reset the respin counter to three.",
                "Three misses or a full board pays the collected coins once.",
                "Coin labels are multiples of your locked bet (1x to 25x).",
                "Coin bonuses run first; queued free spins resume afterward.",
                $"F buys 15 free spins for {SlotRules.BonusBuyMultiplier}x your bet. PgUp: paytable."
            };
            for (int i = 0; i < rules.Length; i++)
                canvas.Write(5, 5 + i, ConsoleCanvas.Fit(rules[i], width - 6), i % 2 == 0 ? ConsoleColor.White : ConsoleColor.Gray);
            canvas.Write(5, 17, ConsoleCanvas.Fit($"Page {_rulesPage + 1}/2. RTP target: {SlotRules.TargetRtp:P1}. Play credits.", width - 6), ConsoleColor.DarkGray);
        }
    }
}
