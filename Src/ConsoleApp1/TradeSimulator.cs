using Binance;
using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;

namespace CryptoTrading.App.BackTesting
{
    /// <summary>
    /// One row per signal in the final trade log. Populated in two passes:
    ///   1. <see cref="TradeSimulator.OpenFromSignal"/> captures the signal-time
    ///      fields (direction, signal price, SL/TP/ATR, score, HTF RSI, etc.).
    ///   2. The backtest loop populates entry/exit fields as the real
    ///      <see cref="SimpleExecutionStrategy"/> opens and closes the trade.
    /// </summary>
    internal class SimulatedTrade
    {
        public DateTime SignalTime;
        public TradeDirection Direction;
        public decimal SignalPrice;
        public decimal InitialRisk;
        public decimal AtrAtSignal;
        public double HtfRsi;
        public double VolExpansion;
        public int ProbabilityScore;

        public decimal Quantity;
        public int Leverage;

        public DateTime EntryTime;
        public decimal EntryPrice;
        public decimal StopLoss;
        public decimal TakeProfit;

        public DateTime ExitTime;
        public decimal ExitPrice;
        public string ExitReason;
        public int BarsHeld;
        public decimal PnlUsdt;
    }

    /// <summary>
    /// Backtest driver: owns the shared 1M <see cref="QuoteHub{IQuote}"/> and
    /// drives the algorithm's own <see cref="IExecutionStrategy"/> (the real
    /// <see cref="SimpleExecutionStrategy"/>) one 1M candle at a time.
    ///
    /// For each signal fired by the algorithm, the harness wires the returned
    /// execution strategy's quote hub to this driver's hub, so the BbGuide
    /// entry and the SimpleExitStrategy both see 1M data on the same rail
    /// they consume in live / paper. That means the backtest exercises the
    /// same code paths as production — no parallel reimplementation.
    ///
    /// Exit reason is read from <see cref="HtfRsiTradingState.LastExitReason"/>,
    /// which the exit strategy writes via <c>RecordTradeComplete</c> immediately
    /// before its <c>StrategyAction.CloseTrade</c> result is returned.
    /// </summary>
    internal class TradeSimulator
    {
        private readonly HtfRsiTradingState _tradingState;
        private readonly QuoteHub<IQuote> _hub = new QuoteHub<IQuote>(10_000);
        private readonly FakeTrade _fakeTrade = new FakeTrade();

        private SimulatedTrade _active;
        private IExecutionStrategy _executionStrategy;
        private HtfRsiVolExpansionSetup _activeSetup;

        public List<SimulatedTrade> Completed { get; } = new List<SimulatedTrade>();
        public bool HasActive => _active != null;
        public QuoteHub<IQuote> Hub => _hub;

        public TradeSimulator(HtfRsiTradingState tradingState)
        {
            _tradingState = tradingState;
        }

        /// <summary>
        /// Latch a freshly-fired signal. The algorithm has already constructed
        /// the execution strategy and sized the setup; we just wire its quote
        /// hub to ours and capture the metadata for the CSV row.
        /// </summary>
        public void OpenFromSignal(IExecutionStrategy executionStrategy, HtfRsiVolExpansionSetup setup)
        {
            executionStrategy.SetQuotes(_hub);
            _executionStrategy = executionStrategy;
            _activeSetup = setup;
            _fakeTrade.Reset();

            _active = new SimulatedTrade
            {
                SignalTime = setup.EntryTime,
                Direction = setup.Direction,
                SignalPrice = setup.EntryPrice,
                InitialRisk = setup.InitialRisk,
                AtrAtSignal = setup.AtrAtEntry,
                HtfRsi = setup.HtfRsi,
                VolExpansion = setup.VolExpansionRatio,
                ProbabilityScore = setup.ProbabilityScore,
                Quantity = setup.Quantity,
                Leverage = setup.Leverage,
                // SL/TP are placeholders until the entry strategy pins the
                // fill price and rebases them — captured in Step() below.
                StopLoss = setup.StopLoss,
                TakeProfit = setup.TakeProfit
            };
        }

        /// <summary>
        /// Push one 1M candle into the quote hub and tick the execution
        /// strategy. Returns the completed trade when this candle closes the
        /// position, otherwise null.
        /// </summary>
        public SimulatedTrade Step(Candlestick m1)
        {
            _hub.Add(new Quote
            {
                Timestamp = m1.CloseTime,
                Open = m1.Open,
                High = m1.High,
                Low = m1.Low,
                Close = m1.Close,
                Volume = m1.Volume
            });

            if (_active == null) return null;

            var status = _executionStrategy.ProcessStrategy(_fakeTrade);

            if (status.StrategyAction == StrategyAction.OpenTrade)
            {
                // Entry strategy already pinned EntryPrice/SL/TP on the setup
                // (BbGuide mode). Copy them into the SimulatedTrade now — the
                // exit strategy will also see them through the shared setup.
                _fakeTrade.Open = true;
                _fakeTrade.TotalOpenBaseQuantity = _activeSetup.Quantity;
                _active.EntryTime = _activeSetup.EntryTime;
                _active.EntryPrice = _activeSetup.EntryPrice;
                _active.StopLoss = _activeSetup.StopLoss;
                _active.TakeProfit = _activeSetup.TakeProfit;
                return null;
            }

            if (status.StrategyAction == StrategyAction.CloseTrade)
            {
                // SimpleExitStrategy has already called tradingState.RecordTradeComplete
                // with the reason and computed PnL, which also mutates equity.
                // Pull those through rather than recomputing.
                _active.ExitTime = m1.CloseTime;
                _active.ExitPrice = status.ExitDetails.Price;
                _active.ExitReason = _tradingState.LastExitReason;
                _active.PnlUsdt = _tradingState.LastExitPnl;
                _active.BarsHeld = _active.EntryTime == default
                    ? 0
                    : (int)((_active.ExitTime - _active.EntryTime).TotalMinutes / 15);

                var done = _active;
                _active = null;
                _executionStrategy = null;
                _activeSetup = null;
                _fakeTrade.Reset();
                Completed.Add(done);
                return done;
            }

            return null;
        }

        /// <summary>
        /// Force-close any still-active trade at the given price/time. Used at
        /// end-of-data so open trades don't vanish from the log.
        /// </summary>
        public SimulatedTrade ForceCloseAtEnd(decimal price, DateTime time)
        {
            if (_active == null) return null;

            if (!_fakeTrade.Open)
            {
                // Signal fired but the entry strategy never pinned a fill
                // (budget hasn't expired yet when data ended). Record as a
                // cancelled setup so the CSV still carries a row.
                _active.EntryTime = time;
                _active.EntryPrice = price;
                _active.ExitTime = time;
                _active.ExitPrice = price;
                _active.ExitReason = "EntryCancelled_Eof";
                _active.PnlUsdt = 0m;
                _active.BarsHeld = 0;
            }
            else
            {
                decimal priceMove = _active.Direction == TradeDirection.Long
                    ? price - _active.EntryPrice
                    : _active.EntryPrice - price;
                _active.ExitTime = time;
                _active.ExitPrice = price;
                _active.ExitReason = "EndOfData";
                _active.PnlUsdt = priceMove * _active.Quantity * _active.Leverage;
                _active.BarsHeld = (int)((time - _active.EntryTime).TotalMinutes / 15);

                // Mirror the live-exit path so equity and trade counters stay
                // consistent with what the algorithm would have seen.
                _tradingState.RecordTradeComplete(_active.ExitReason, _active.PnlUsdt);
            }

            var done = _active;
            _active = null;
            _executionStrategy = null;
            _activeSetup = null;
            _fakeTrade.Reset();
            Completed.Add(done);
            return done;
        }
    }

    /// <summary>
    /// Minimal <see cref="ITrade"/> stub. The execution and exit strategies
    /// only ever read <see cref="ITrade.Open"/>, <see cref="ITrade.TotalOpenBaseQuantity"/>
    /// and <see cref="ITrade.ProfitPct"/>; everything else throws to make any
    /// accidental use loud in a backtest.
    /// </summary>
    internal class FakeTrade : ITrade
    {
        public bool Open { get; set; }
        public decimal TotalOpenBaseQuantity { get; set; }
        public decimal ProfitPct => 0m;

        public void Reset()
        {
            Open = false;
            TotalOpenBaseQuantity = 0m;
        }

        // --- Unused surface ---
        public string Pair => "BTCUSDT";
        public decimal TotalCloseBaseQuantity => 0m;
        public decimal RemainingQuantity => 0m;
        public decimal CurrentPrice { get; set; }
        public decimal Profit => 0m;
        public decimal OpenPrice => 0m;
        public decimal ClosePrice => 0m;
        public DateTime StartDate => default;
        public DateTime CloseDate => default;
        public string Comment => "";
        public decimal FeeBnb => 0m;
        public List<ITransaction> PendingEntryTransactions => new List<ITransaction>();
        public List<ITransaction> PendingExitTransactions => new List<ITransaction>();

        public ITransaction GetCurrentTransaction() => null;
        public void CancelCurrentTransaction() { }
        public void UpdateCurrentTransaction(ExchangeOrder order) { }
        public ITransaction CompleteTrade() => null;
        public ITransaction CreateOpenTransaction(decimal price, DateTime closeTime, decimal amount) => null;
        public ITransaction CreateCloseTransaction(decimal price, DateTime closeTime, decimal amount) => null;
    }
}
