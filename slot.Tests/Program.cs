using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using slot;

internal static class Program
{
    private static int _checks;
    private static void Check(bool valid, string name)
    {
        if (!valid) throw new Exception(name);
        _checks++;
    }
    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { _checks++; return; }
        throw new Exception(name);
    }
    private static SlotReels Board(params string[] rows)
    {
        var symbols = new char[3, 5];
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 5; column++) symbols[row, column] = rows[row][column];
        return SlotRules.Board(symbols);
    }
    private static SlotReels Line(string middle) => Board("SSSSS", middle, "SSSSS");
    private static int[] Stops(string pattern, bool free = false) => (free ? SlotRules.FreeReelStrips : SlotRules.ReelStrips).Select(strip => strip.IndexOf(pattern, StringComparison.Ordinal)).ToArray();
    private static int[] ScatterStops(bool free = false) => (free ? SlotRules.FreeReelStrips : SlotRules.ReelStrips).Select(strip => strip.LastIndexOf('S')).ToArray();
    private static void Complete(GameEngine engine, RoundStep step) => engine.CompleteStep(step.Id);

    private static void PayoutChecks()
    {
        Check(SlotRules.Evaluate(Line("333AK"), 15).Win == 6, "Low three-symbol win");
        Check(SlotRules.Evaluate(Line("3333K"), 15).Win == 15, "Low four-symbol win");
        Check(SlotRules.Evaluate(Line("33333"), 15).Win == 45, "Low five-symbol win");
        Check(SlotRules.Evaluate(Line("AAAK3"), 15).Win == 15, "Premium three-symbol win");
        Check(SlotRules.Evaluate(Line("AAAA3"), 15).Win == 36, "Premium four-symbol win");
        Check(SlotRules.Evaluate(Line("AAAAA"), 15).Win == 120, "Premium five-symbol win");
        Check(SlotRules.Evaluate(Line("CCCAK"), 15).Win == 30, "Crown three-symbol win");
        Check(SlotRules.Evaluate(Line("CCCCA"), 15).Win == 90, "Crown four-symbol win");
        Check(SlotRules.Evaluate(Line("CCCCC"), 15).Win == 300, "Crown five-symbol win");
        Check(SlotRules.Evaluate(Line("WWWSS"), 15).Win == 45, "Three wilds pay their own value");
        Check(SlotRules.Evaluate(Line("WWWAK"), 15).Win == 45, "Best wild prefix beats longer premium run");
        Check(SlotRules.Evaluate(Line("WWWCA"), 15).Win == 90, "Longer crown run beats wild prefix");
        Check(SlotRules.Evaluate(Line("WWWWW"), 15).Win == 450, "All wild line pays once");
        Check(SlotRules.Evaluate(Line("WAAK3"), 15).Win == 15, "Leading wild substitutes without legacy doubling");
        Check(SlotRules.Evaluate(Line("33S33"), 15).Win == 0, "Scatter breaks run");
        Check(SlotRules.Evaluate(Line("33O33"), 15).Win == 0, "Coin breaks run");
        Check(SlotRules.Evaluate(Board("WWWWW", "WWWWW", "WWWWW"), 15).Win == 6750, "All fifteen lines");
        Check(SlotRules.Evaluate(Line("333AK"), 15, 5).Win == 30, "Free multiplier applies to line");
        Check(SlotRules.Evaluate(Line("333AK"), 0.01m).Win == 0, "Round combined payout to cents");
        var scatters = SlotRules.Evaluate(Board("SSS34", "SSS45", "SSS5A"), 3);
        Check(scatters.ScatterReels == 3, "Scatter reels counted once despite nine symbols");
        Check(SlotRules.FreeSpinAward(3) == 8 && SlotRules.FreeSpinAward(4) == 12 && SlotRules.FreeSpinAward(5) == 20, "Scatter award schedule");
        Throws<ArgumentOutOfRangeException>(() => SlotRules.Evaluate(Line("AAAAA"), -1), "Reject negative bet");
        Throws<ArgumentException>(() => SlotRules.Evaluate(Line("XXXXX"), 3), "Reject unknown symbol");
    }

    private static void RandomChecks()
    {
        var bytes = new ByteRandom(uint.MaxValue, 7);
        using (var source = new CryptoRandomSource(bytes))
        {
            Check(source.NextInt(10) == 7 && bytes.Draws == 2, "Reject biased modulo remainder");
            Throws<ArgumentOutOfRangeException>(() => source.NextInt(0), "Reject empty range");
        }
        using (var source = new CryptoRandomSource())
        {
            foreach (int maximum in new[] { 1, 2, 3, 65, 100, int.MaxValue })
                for (int i = 0; i < 100; i++)
                {
                    int value = source.NextInt(maximum);
                    Check(value >= 0 && value < maximum, "Cryptographic draw bounds");
                }
        }
        foreach (decimal bet in SlotRules.BetOptions)
        {
            Check(SlotRules.Evaluate(Line("AAAK3"), bet).Win == bet, "Three premium symbols cover the total bet");
            Check(SlotRules.Evaluate(Line("WWWWW"), bet).Win == bet * 30, "Five wilds pay thirty total bets per line");
        }
        var freeRandom = new SequenceRandom();
        freeRandom.Add(SlotRules.FreeReelStrips.Select(strip => strip.Length - 1));
        var freeGrid = SlotRules.Generate(freeRandom, true);
        for (int column = 0; column < 5; column++)
        {
            string strip = SlotRules.FreeReelStrips[column];
            for (int row = 0; row < 3; row++)
                Check(freeGrid.Reels[row].Items[column].Type == strip[(strip.Length - 1 + row) % strip.Length], "Free spins use their own circular strips");
        }
        var random = new SequenceRandom();
        random.Add(Enumerable.Repeat(0, 5));
        var grid = SlotRules.Generate(random);
        for (int column = 0; column < 5; column++)
            for (int row = 0; row < 3; row++) Check(grid.Reels[row].Items[column].Type == SlotRules.ReelStrips[column][row], "Consecutive virtual reel positions");
        var wrap = new SequenceRandom();
        wrap.Add(SlotRules.ReelStrips.Select(strip => strip.Length - 1));
        var wrapped = SlotRules.Generate(wrap);
        Check(wrapped.Reels[1].Items[0].Type == SlotRules.ReelStrips[0][0], "Reel wraps around");
        int[] thresholds = { 0, 699, 700, 899, 900, 969, 970, 989, 990, 997, 998 };
        decimal[] values = { 1, 1, 2, 2, 3, 3, 5, 5, 10, 10, 25 };
        for (int i = 0; i < thresholds.Length; i++)
        {
            var draw = new SequenceRandom(); draw.Add(thresholds[i]);
            Check(SlotRules.DrawCoin(draw, 3) == values[i] * 3, "Weighted coin threshold");
        }
    }

    private static void TransactionChecks()
    {
        var wallet = new Wallet(1000);
        var random = new SequenceRandom();
        var engine = new GameEngine(wallet, random);
        random.Add(Stops("345"));
        var step = engine.BeginPaidSpin(3);
        Check(wallet.Balance == 997 && engine.HasPending, "Reserve bet before display");
        Check(step.Payout > 0, "Prepared payout exists");
        Throws<InvalidOperationException>(() => engine.BeginPaidSpin(3), "Pending round blocks another wager");
        Throws<InvalidOperationException>(() => engine.CompleteStep(step.Id + 1), "Wrong settlement ID rejected");
        Complete(engine, step);
        decimal expected = 997 + step.Payout;
        Check(wallet.Balance == expected, "Credit payout after settlement");
        Complete(engine, step);
        Check(wallet.Balance == expected, "Settlement idempotent");
        wallet.FailCredit = true;
        random.Add(Stops("345"));
        var retry = engine.BeginPaidSpin(3);
        Throws<InvalidOperationException>(() => Complete(engine, retry), "Failed credit remains retryable");
        Check(engine.HasPending, "Failed settlement stays pending");
        Complete(engine, retry);
        decimal afterRetry = wallet.Balance;
        Complete(engine, retry);
        Check(wallet.Balance == afterRetry, "Retried settlement credits once");
        var poor = new GameEngine(new Wallet(1), new SequenceRandom());
        Check(poor.BeginPaidSpin(3) == null && !poor.HasPending, "Insufficient credits do not draw");
        Throws<ArgumentOutOfRangeException>(() => poor.BeginPaidSpin(0.001m), "Fractional cent bet rejected");
        var failedWallet = new Wallet(100);
        var failedRandom = new SequenceRandom();
        var failed = new GameEngine(failedWallet, failedRandom);
        Throws<CryptographicException>(() => failed.BeginPaidSpin(3), "RNG failure aborts paid spin");
        Check(failedWallet.Balance == 100 && !failed.HasPending && failed.Mode == GameMode.BaseGame, "Refund and no state transition");
        failedRandom.Add(Stops("SOO")); failedRandom.Add(0);
        Throws<CryptographicException>(() => failed.BeginPaidSpin(3), "Failure during coin valuation aborts entire spin");
        Check(failedWallet.Balance == 100 && failed.FreeSpinsLeft == 0 && !failed.HasPending, "Partial trigger cannot leak bonus state");
    }

    private static void FreeSpinChecks()
    {
        Check(SlotRules.BonusBuyMultiplier == 50, "Bonus buy costs fifty times the bet");
        var wallet = new Wallet(10000);
        var random = new SequenceRandom();
        var engine = new GameEngine(wallet, random);
        Check(engine.BuyFreeSpins(3) && wallet.Balance == 10000 - 3 * SlotRules.BonusBuyMultiplier, "Buy fifteen spins at the configured price");
        Check(engine.FreeSpinsLeft == 15 && engine.Multiplier == 2, "Free feature starts at 2x");
        var exactWallet = new Wallet(150);
        var exactBuy = new GameEngine(exactWallet, new SequenceRandom());
        Check(exactBuy.BuyFreeSpins(3) && exactWallet.Balance == 0, "Three-credit bonus costs exactly 150");
        var shortWallet = new Wallet(149.99m);
        var shortBuy = new GameEngine(shortWallet, new SequenceRandom());
        Check(!shortBuy.BuyFreeSpins(3) && shortWallet.Balance == 149.99m && shortBuy.Mode == GameMode.BaseGame, "Insufficient bonus credits leave wallet and feature unchanged");
        Throws<InvalidOperationException>(() => engine.BeginPaidSpin(15), "Cannot change stake through paid spin during bonus");
        Throws<CryptographicException>(() => engine.BeginFeatureStep(), "Free RNG failure pauses without consuming spin");
        Check(engine.FreeSpinsLeft == 15 && engine.Multiplier == 2, "Free state intact after failure");
        for (int row = 0; row < 3; row++)
        {
            random.Add(SlotRules.FreeReelStrips.Select(strip => (strip.IndexOf('W') - row + strip.Length) % strip.Length));
            var step = engine.BeginFeatureStep();
            Check(step.Bet == 3 && step.Multiplier == row + 2, "Locked bet and current multiplier");
            for (int column = 0; column < 5; column++) Check(step.Sticky[row, column], "New wild becomes sticky");
            Complete(engine, step);
            Check(engine.Multiplier == Math.Min(5, row + 3), "Multiplier increases once per spin, not per wild");
        }
        random.Add(Stops("345", true));
        var allSticky = engine.BeginFeatureStep();
        Check(allSticky.Board.Reels.All(reel => reel.Items.All(item => item.Type == 'W')), "Sticky positions override next reel stop");
        Check(allSticky.Multiplier == 5 && allSticky.NextMultiplier == 5, "Multiplier capped at five");
        Complete(engine, allSticky);
        while (engine.Mode != GameMode.BaseGame)
        {
            random.Add(Stops("345", true));
            var step = engine.BeginFeatureStep();
            if (engine.FreeSpinsLeft == 1) Check(step.Multiplier == 5 && step.Payout == 6750, "Last free spin keeps multiplier");
            Complete(engine, step);
        }
        Check(engine.Multiplier == 1 && engine.FreeSpinsLeft == 0, "Feature state clears on completion");
        Check(engine.BuyFreeSpins(3) && engine.Multiplier == 2, "New feature resets multiplier");
        random.Add(Stops("345", true));
        var fresh = engine.BeginFeatureStep();
        Check(!fresh.Sticky.Cast<bool>().Any(value => value), "New feature has no old sticky wilds");
        Complete(engine, fresh);

        var capRandom = new SequenceRandom();
        var capped = new GameEngine(new Wallet(100000), capRandom);
        capRandom.Add(ScatterStops());
        var trigger = capped.BeginPaidSpin(3);
        Check(trigger.FreeSpinsAdded == 20, "Five scatter reels start twenty spins");
        Complete(capped, trigger);
        int added = 20;
        for (int i = 0; i < 12; i++)
        {
            capRandom.Add(ScatterStops(true));
            var step = capped.BeginFeatureStep();
            added += step.FreeSpinsAdded;
            Complete(capped, step);
        }
        Check(added == 50 && capped.FreeSpinsAwarded == 50, "Cumulative free-spin award capped at fifty");
    }

    private static void CoinChecks()
    {
        var wallet = new Wallet(1000);
        var random = new SequenceRandom();
        var engine = new GameEngine(wallet, random);
        random.Add(Stops("SOO")); random.Add(Enumerable.Repeat(0, 10));
        var trigger = engine.BeginPaidSpin(3);
        Check(trigger.CoinStarted && trigger.CoinTotal == 30 && trigger.FreeSpinsAdded == 20, "Coin and scatter trigger together");
        Complete(engine, trigger);
        Check(engine.Mode == GameMode.CoinRespins && engine.FreeSpinsLeft == 20, "Coins run before queued free spins");
        decimal balance = wallet.Balance;
        random.Add(0, 0);
        Throws<CryptographicException>(() => engine.BeginFeatureStep(), "Failed coin draw does not partially lock coins");
        random.Add(Enumerable.Repeat(99, 5));
        var miss = engine.BeginFeatureStep();
        Check(miss.CoinTotal == 30 && miss.Respins == 2, "Coin state preserved after RNG failure");
        Complete(engine, miss);
        Check(wallet.Balance == balance, "Locked coins not paid early");
        random.Add(0, 700); random.Add(Enumerable.Repeat(99, 4));
        var hit = engine.BeginFeatureStep();
        Check(hit.NewCoins == 1 && hit.Respins == 3 && hit.CoinTotal == 36, "New coin resets counter and holds its value");
        Complete(engine, hit);
        for (int remaining = 2; remaining >= 0; remaining--)
        {
            random.Add(Enumerable.Repeat(99, 4));
            var step = engine.BeginFeatureStep();
            Check(step.Respins == remaining && step.CoinTotal == 36, "Consecutive misses count down");
            if (remaining > 0) Check(step.Payout == 0, "No intermediate coin settlement");
            Complete(engine, step);
            if (remaining == 0)
            {
                Check(step.CoinFinished && step.CoinPayout == 36 && wallet.Balance == balance + 36, "Coin collection paid once without free multiplier");
                Complete(engine, step);
                Check(wallet.Balance == balance + 36, "Coin payout cannot be duplicated");
            }
        }
        Check(engine.Mode == GameMode.FreeSpins && engine.FreeSpinsLeft == 20 && engine.Multiplier == 2, "Free spins resume after coins without consuming a spin");

        var fullRandom = new SequenceRandom();
        var full = new GameEngine(new Wallet(1000), fullRandom);
        fullRandom.Add(Stops("SOO")); fullRandom.Add(Enumerable.Repeat(0, 10));
        Complete(full, full.BeginPaidSpin(3));
        for (int i = 0; i < 5; i++) fullRandom.Add(0, 0);
        var filled = full.BeginFeatureStep();
        Check(filled.CoinFinished && filled.CoinPayout == 45 && filled.NewCoins == 5, "Full board ends bonus immediately");
        Complete(full, filled);
        Check(full.Mode == GameMode.FreeSpins, "Full board resumes queued feature");
    }

    private static void LastFreeCoinChecks()
    {
        var random = new SequenceRandom();
        var wallet = new Wallet(10000);
        var engine = new GameEngine(wallet, random);
        Check(engine.BuyFreeSpins(3), "Purchase for last-free-spin test");
        for (int i = 0; i < 14; i++)
        {
            random.Add(Stops("345", true));
            Complete(engine, engine.BeginFeatureStep());
        }
        Check(engine.FreeSpinsLeft == 1, "One free spin remains");
        random.Add(Stops("OO", true)); random.Add(Enumerable.Repeat(0, 10));
        var last = engine.BeginFeatureStep();
        Check(last.CoinStarted && last.FreeSpin && last.Multiplier == 2, "Last free spin can trigger coins");
        Complete(engine, last);
        Check(engine.Mode == GameMode.CoinRespins && engine.FreeSpinsLeft == 0, "Last-spin coin feature remains payable");
        for (int i = 0; i < 3; i++)
        {
            random.Add(Enumerable.Repeat(99, 5));
            Complete(engine, engine.BeginFeatureStep());
        }
        Check(engine.Mode == GameMode.BaseGame && engine.Multiplier == 1, "Last-spin coins finish and clear free state");
    }

    private static void UiChecks()
    {
        var player = new PlayerBase();
        Anticheat.Stop();
        player.Set(1000);
        var random = new SequenceRandom();
        using (var game = new SlotMachine(player, random))
        {
            var compose = typeof(SlotMachine).GetMethod("ComposeFrame", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var size in new[] { new[] { 79, 23 }, new[] { 119, 35 }, new[] { 60, 20 } })
            {
                var frame = compose.Invoke(game, new object[] { size[0], size[1] });
                var chars = (char[])frame.GetType().GetField("_characters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(frame);
                Check(chars.Length == size[0] * size[1], "Frame bounds");
                Check(new string(chars).Contains("A U R U M"), "Brand survives layout changes");
            }
            typeof(SlotMachine).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, 2);
            for (int page = 0; page < 2; page++)
            {
                typeof(SlotMachine).GetField("_rulesPage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, page);
                var frame = compose.Invoke(game, new object[] { 79, 23 });
                var chars = (char[])frame.GetType().GetField("_characters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(frame);
                Check(new string(chars).Contains(page == 0 ? "Paytable" : "Six coins"), "Both help pages available");
            }
            typeof(SlotMachine).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, 0);
            var step = new RoundStep { Bet = 3, CoinRespin = true, Respins = 3, Board = Line("OOOOO") };
            step.CoinValues[1, 0] = 75;
            step.PreviousCoins[1, 0] = 75;
            typeof(SlotMachine).GetField("_step", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, step);
            typeof(SlotMachine).GetField("_preGeneratedResult", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, step.Board);
            typeof(SlotMachine).GetField("_animationFrame", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, 0);
            var visual = typeof(SlotMachine).GetMethod("GenerateVisualBoard", BindingFlags.Instance | BindingFlags.NonPublic);
            typeof(SlotMachine).GetField("_animationSymbols", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, visual.Invoke(game, new object[] { true }));
            var coinFrame = compose.Invoke(game, new object[] { 79, 23 });
            var coinChars = (char[])coinFrame.GetType().GetField("_characters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(coinFrame);
            Check(new string(coinChars).Contains("25x"), "Locked coin value stays visible during animation");
            for (int i = 0; i < 30; i++) visual.Invoke(game, new object[] { false });
            Check(random.Draws == 0, "Drawing and animation never consume gameplay entropy");
            player.Set(123);
            typeof(SlotMachine).GetMethod("HandleNavigation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { ConsoleKey.C });
            Check(player.Balance == 623, "Credit button still works at nonzero balance");
        }
    }

    private static void Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--simulate" || args[0] == "--simulate-buys"))
        {
            Simulate(args.Length > 1 ? int.Parse(args[1]) : 100000, args.Length > 2 ? int.Parse(args[2]) : 1937, args[0] == "--simulate-buys");
            return;
        }
        PayoutChecks(); RandomChecks(); TransactionChecks(); FreeSpinChecks(); CoinChecks(); LastFreeCoinChecks(); UiChecks();
        Console.WriteLine(_checks + " checks passed.");
    }

    private static void Simulate(int count, int seed, bool purchases)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        var wallet = new Wallet(1000000000000m);
        var engine = new GameEngine(wallet, new SimulationRandom(seed));
        decimal lines = 0, coins = 0, baseLines = 0;
        double mean = 0, squaredDifferences = 0;
        int free = 0, respins = 0, triggers = 0, hits = 0;
        for (int i = 0; i < count; i++)
        {
            decimal before = wallet.Balance;
            RoundStep step;
            if (purchases)
            {
                if (!engine.BuyFreeSpins(3)) throw new Exception("Simulation credits exhausted.");
                step = engine.BeginFeatureStep();
            }
            else step = engine.BeginPaidSpin(3);
            if (step.Payout > 0) hits++;
            while (true)
            {
                lines += step.Lines?.Win ?? 0;
                if (!step.FreeSpin) baseLines += step.Lines?.Win ?? 0;
                coins += step.CoinPayout;
                if (step.FreeSpin) free++;
                if (step.CoinRespin) respins++;
                if (step.CoinStarted) triggers++;
                Complete(engine, step);
                if (engine.Mode == GameMode.BaseGame) break;
                step = engine.BeginFeatureStep();
            }
            double returned = (double)((wallet.Balance - before) / (3m * (purchases ? SlotRules.BonusBuyMultiplier : 1)) + 1);
            double delta = returned - mean;
            mean += delta / (i + 1);
            squaredDifferences += delta * (returned - mean);
        }
        double margin = count > 1 ? 1.96 * Math.Sqrt(squaredDifferences / (count - 1) / count) : double.NaN;
        Console.WriteLine($"Approximate 95% RTP interval: {mean - margin:P3} to {mean + margin:P3}; seed: {seed}");
        decimal wagered = count * 3m * (purchases ? SlotRules.BonusBuyMultiplier : 1);
        Console.WriteLine($"{(purchases ? "Bonus purchases" : "Paid spins")}: {count:N0}; wagered: {wagered:N2}; lines: {lines:N2}; coins: {coins:N2}");
        Console.WriteLine($"Observed RTP: {(lines + coins) / wagered:P3}; first-spin hit rate: {(decimal)hits / count:P3}");
        Console.WriteLine($"Base line wins: {baseLines:N2}; free-spin line wins: {lines - baseLines:N2}");
        Console.WriteLine($"Free spins: {free:N0}; coin respins: {respins:N0}; coin features: {triggers:N0}");
        if (wallet.Balance != 1000000000000m - wagered + lines + coins) throw new Exception("Simulation wallet reconciliation failed.");
        Console.WriteLine("Wallet reconciliation passed. This sample is not a certified or theoretical RTP.");
    }

    private sealed class Wallet : IGameWallet
    {
        public decimal Balance;
        public bool FailCredit;
        public Wallet(decimal balance) => Balance = balance;
        public bool TrySpend(decimal amount) { if (Balance < amount) return false; Balance -= amount; return true; }
        public void Credit(decimal amount) { if (FailCredit) { FailCredit = false; throw new InvalidOperationException(); } Balance += amount; }
    }
    private sealed class SequenceRandom : IRandomSource
    {
        private readonly Queue<int> _values = new Queue<int>();
        public int Draws;
        public void Add(params int[] values) { foreach (int value in values) _values.Enqueue(value); }
        public void Add(IEnumerable<int> values) => Add(values.ToArray());
        public int NextInt(int maximum)
        {
            Draws++;
            if (_values.Count == 0) throw new CryptographicException("Injected entropy failure.");
            int value = _values.Dequeue();
            if (value < 0 || value >= maximum) throw new Exception("Invalid scripted draw.");
            return value;
        }
    }
    private sealed class SimulationRandom : IRandomSource
    {
        private readonly Random _random;
        public SimulationRandom(int seed) => _random = new Random(seed);
        public int NextInt(int maximum) => _random.Next(maximum);
    }
    private sealed class ByteRandom : RandomNumberGenerator
    {
        private readonly Queue<uint> _values;
        public int Draws;
        public ByteRandom(params uint[] values) => _values = new Queue<uint>(values);
        public override void GetBytes(byte[] data) { Draws++; Array.Copy(BitConverter.GetBytes(_values.Dequeue()), data, 4); }
        public override void GetNonZeroBytes(byte[] data) => throw new NotSupportedException();
    }
}
