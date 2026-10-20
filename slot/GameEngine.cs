using System;
using System.Linq;

namespace slot
{
    internal interface IGameWallet
    {
        bool TrySpend(decimal amount);
        void Credit(decimal amount);
    }

    internal sealed class PlayerWallet : IGameWallet
    {
        private readonly PlayerBase _player;
        public PlayerWallet(PlayerBase player) => _player = player;
        public bool TrySpend(decimal amount) => _player.TrySpend((double)amount);
        public void Credit(decimal amount) { if (amount > 0) _player.Add((double)amount); }
    }

    internal enum GameMode { BaseGame, FreeSpins, CoinRespins }

    internal sealed class CoinFeature
    {
        public decimal Bet;
        public int Respins = 3;
        public decimal[,] Values = new decimal[3, 5];
        public CoinFeature Copy() => new CoinFeature { Bet = Bet, Respins = Respins, Values = (decimal[,])Values.Clone() };
    }

    internal sealed class FeatureState
    {
        public int Remaining;
        public int Awarded;
        public int Multiplier = 1;
        public decimal Bet;
        public bool[,] Sticky = new bool[3, 5];
        public CoinFeature Coins;
        public FeatureState Copy() => new FeatureState {
            Remaining = Remaining, Awarded = Awarded, Multiplier = Multiplier, Bet = Bet,
            Sticky = (bool[,])Sticky.Clone(), Coins = Coins?.Copy()
        };
    }

    internal sealed class RoundStep
    {
        public long Id { get; internal set; }
        public SlotReels Board { get; internal set; }
        public SpinResult Lines { get; internal set; }
        public decimal Bet { get; internal set; }
        public decimal CoinPayout { get; internal set; }
        public decimal CoinTotal { get; internal set; }
        public decimal[,] CoinValues { get; internal set; } = new decimal[3, 5];
        public decimal[,] PreviousCoins { get; internal set; } = new decimal[3, 5];
        public bool[,] Sticky { get; internal set; } = new bool[3, 5];
        public bool[,] PreviousSticky { get; internal set; } = new bool[3, 5];
        public bool FreeSpin { get; internal set; }
        public bool CoinRespin { get; internal set; }
        public bool CoinStarted { get; internal set; }
        public bool CoinFinished { get; internal set; }
        public int Multiplier { get; internal set; } = 1;
        public int NextMultiplier { get; internal set; } = 1;
        public int FreeSpinsAdded { get; internal set; }
        public int Respins { get; internal set; }
        public int NewCoins { get; internal set; }
        internal FeatureState NextState { get; set; }
        public decimal Payout => (Lines?.Win ?? 0) + CoinPayout;
    }

    internal sealed class GameEngine
    {
        private readonly IGameWallet _wallet;
        private readonly IRandomSource _random;
        private FeatureState _state = new FeatureState();
        private RoundStep _pending;
        private long _nextId;
        private long _lastSettledId;
        public GameMode Mode => _state.Coins != null ? GameMode.CoinRespins : _state.Remaining > 0 ? GameMode.FreeSpins : GameMode.BaseGame;
        public int FreeSpinsLeft => _state.Remaining;
        public int FreeSpinsAwarded => _state.Awarded;
        public int Multiplier => _state.Multiplier;
        public decimal FeatureBet => _state.Coins?.Bet ?? _state.Bet;
        public bool HasPending => _pending != null;

        public GameEngine(IGameWallet wallet, IRandomSource random)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public RoundStep BeginPaidSpin(decimal bet)
        {
            RequireIdle();
            if (Mode != GameMode.BaseGame) throw new InvalidOperationException("Finish the current bonus first.");
            ValidateBet(bet);
            if (!_wallet.TrySpend(bet)) return null;
            try
            {
                var step = PrepareSpin(bet, false);
                return SetPending(step);
            }
            catch
            {
                _wallet.Credit(bet);
                throw;
            }
        }

        public bool BuyFreeSpins(decimal bet)
        {
            RequireIdle();
            if (Mode != GameMode.BaseGame) throw new InvalidOperationException("Finish the current bonus first.");
            ValidateBet(bet);
            var next = new FeatureState { Remaining = 15, Awarded = 15, Multiplier = 2, Bet = bet };
            if (!_wallet.TrySpend(checked(bet * SlotRules.BonusBuyMultiplier))) return false;
            _state = next;
            return true;
        }

        public RoundStep BeginFeatureStep()
        {
            RequireIdle();
            if (Mode == GameMode.BaseGame) throw new InvalidOperationException("No bonus is active.");
            var step = Mode == GameMode.CoinRespins ? PrepareCoinRespin() : PrepareSpin(_state.Bet, true);
            return SetPending(step);
        }

        public void CompleteStep(long id)
        {
            if (id == _lastSettledId && id > 0) return;
            if (_pending == null || _pending.Id != id) throw new InvalidOperationException("This round is not awaiting settlement.");
            _wallet.Credit(_pending.Payout);
            _state = _pending.NextState;
            _lastSettledId = id;
            _pending = null;
        }

        private RoundStep SetPending(RoundStep step)
        {
            step.Id = ++_nextId;
            _pending = step;
            return step;
        }

        private RoundStep PrepareSpin(decimal bet, bool free)
        {
            FeatureState next = _state.Copy();
            char[,] symbols = SlotRules.Symbols(SlotRules.Generate(_random, free));
            int multiplier = free ? next.Multiplier : 1;
            bool newWild = false;
            if (free)
            {
                for (int row = 0; row < 3; row++)
                    for (int column = 0; column < 5; column++)
                    {
                        if (next.Sticky[row, column]) symbols[row, column] = 'W';
                        else if (symbols[row, column] == 'W') { next.Sticky[row, column] = true; newWild = true; }
                    }
                next.Remaining--;
            }
            SlotReels board = SlotRules.Board(symbols);
            SpinResult lines = SlotRules.Evaluate(board, bet, multiplier);
            int award = free ? lines.ScatterReels >= 3 ? Math.Min(5, SlotRules.MaximumFreeSpins - next.Awarded) : 0 : SlotRules.FreeSpinAward(lines.ScatterReels);
            if (!free && award > 0)
                next = new FeatureState { Bet = bet, Multiplier = 2 };
            next.Remaining += award;
            next.Awarded += award;
            if (free && newWild) next.Multiplier = Math.Min(SlotRules.MaximumMultiplier, multiplier + 1);
            var step = new RoundStep {
                Board = board, Lines = lines, Bet = bet, FreeSpin = free, Multiplier = multiplier,
                NextMultiplier = next.Multiplier, FreeSpinsAdded = award, Sticky = (bool[,])next.Sticky.Clone(),
                PreviousSticky = (bool[,])_state.Sticky.Clone(), NextState = next
            };
            if (lines.Coins >= SlotRules.CoinTrigger)
            {
                var coins = new CoinFeature { Bet = bet };
                for (int row = 0; row < 3; row++)
                    for (int column = 0; column < 5; column++)
                        if (symbols[row, column] == 'O') coins.Values[row, column] = SlotRules.DrawCoin(_random, bet);
                next.Coins = coins;
                step.CoinStarted = true;
                step.CoinValues = (decimal[,])coins.Values.Clone();
                step.CoinTotal = coins.Values.Cast<decimal>().Sum();
                step.Respins = coins.Respins;
                if (coins.Values.Cast<decimal>().All(value => value > 0))
                {
                    step.CoinFinished = true;
                    step.CoinPayout = SlotRules.Money(step.CoinTotal);
                    step.Respins = 0;
                    next.Coins = null;
                }
            }
            FinishFreeSession(next);
            return step;
        }

        private RoundStep PrepareCoinRespin()
        {
            FeatureState next = _state.Copy();
            CoinFeature coins = next.Coins;
            var step = new RoundStep {
                CoinRespin = true, Bet = coins.Bet, PreviousCoins = (decimal[,])coins.Values.Clone(), NextState = next
            };
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 5; column++)
                    if (coins.Values[row, column] == 0 && _random.NextInt(100) < SlotRules.CoinChancePercent)
                    {
                        coins.Values[row, column] = SlotRules.DrawCoin(_random, coins.Bet);
                        step.NewCoins++;
                    }
            coins.Respins = step.NewCoins > 0 ? 3 : coins.Respins - 1;
            var symbols = new char[3, 5];
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 5; column++) symbols[row, column] = coins.Values[row, column] > 0 ? 'O' : '.';
            step.Board = SlotRules.Board(symbols);
            step.CoinValues = (decimal[,])coins.Values.Clone();
            step.CoinTotal = coins.Values.Cast<decimal>().Sum();
            step.Respins = coins.Respins;
            if (coins.Respins == 0 || coins.Values.Cast<decimal>().All(value => value > 0))
            {
                step.CoinFinished = true;
                step.CoinPayout = SlotRules.Money(step.CoinTotal);
                step.Respins = 0;
                next.Coins = null;
                FinishFreeSession(next);
            }
            return step;
        }

        private static void FinishFreeSession(FeatureState state)
        {
            if (state.Remaining > 0 || state.Coins != null) return;
            state.Sticky = new bool[3, 5];
            state.Multiplier = 1;
            state.Awarded = 0;
            state.Bet = 0;
        }

        private void RequireIdle()
        {
            if (_pending != null) throw new InvalidOperationException("Settle the current round before starting another.");
        }

        private static void ValidateBet(decimal bet)
        {
            if (bet <= 0 || bet != SlotRules.Money(bet) || bet > 150m) throw new ArgumentOutOfRangeException(nameof(bet));
        }
    }
}
