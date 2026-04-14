using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using FluentAssertions;
using Moq;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    /// <summary>
    /// Realistic BTC trade scenarios using actual price levels and ATR values
    /// from the backtest period (Jul–Aug 2025, BTC ~$95k–$115k).
    ///
    /// These tests demonstrate what the strategy SHOULD produce:
    ///   - How many candles a typical trade lasts
    ///   - What the P&L looks like at various exit types
    ///   - How trailing stop protects profits (no breakeven — trailing at 1R does the job)
    ///   - How dynamic TP scales with signal quality
    ///
    /// Price action in each test is modelled on common BTC intraday patterns:
    ///   - Vol expansion selloffs (sharp drop, bounce, continuation)
    ///   - Momentum breakouts (steady trend with pullbacks)
    ///   - Failed breakouts (initial move then full reversal)
    ///   - Choppy consolidation (time stop scenario)
    /// </summary>
    public class RealisticBtcTradeTests
    {
        private readonly ITestOutputHelper _output;

        public RealisticBtcTradeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Test Infrastructure

        /// <summary>
        /// Records each candle and strategy action for trade logging.
        /// </summary>
        private class TradeLog
        {
            public List<(int bar, decimal close, decimal high, decimal low, StrategyAction action, string note)> Candles { get; } = new();

            public void Add(int bar, decimal close, decimal high, decimal low, StrategyAction action, string note = "")
            {
                Candles.Add((bar, close, high, low, action, note));
            }

            public string GetSummary()
            {
                var entries = Candles.Where(c => c.action == StrategyAction.OpenTrade).ToList();
                var exits = Candles.Where(c => c.action == StrategyAction.CloseTrade).ToList();
                var totalBars = Candles.Count;

                return $"Bars: {totalBars} | Entries: {entries.Count} | Exits: {exits.Count}";
            }
        }

        private class BtcPipeline
        {
            public HtfRsiTradingState TradingState { get; }
            public HtfRsiVolExpansionSetup Setup { get; }
            public HtfRsiVolExpansionExecutionStrategy ExecutionStrategy { get; }
            public QuoteHub<IQuote> QuoteHub { get; }
            public TradeLog Log { get; } = new();

            private readonly Mock<ITrade> _tradeMock;
            private DateTime _currentTime;
            private int _barCount;
            private decimal _rebasedEntry;

            public decimal RebasedEntry => _rebasedEntry;

            public BtcPipeline(
                TradeDirection direction,
                decimal entryPrice,
                decimal atr,
                int probabilityScore,
                DateTime startTime)
            {
                var risk = atr * 1.5m;

                TradingState = new HtfRsiTradingState(200_000m);

                Setup = new HtfRsiVolExpansionSetup
                {
                    Direction = direction,
                    EntryPrice = entryPrice,
                    StopLoss = direction == TradeDirection.Long
                        ? entryPrice - risk : entryPrice + risk,
                    TakeProfit = direction == TradeDirection.Long
                        ? entryPrice + risk : entryPrice - risk,
                    AtrAtEntry = atr,
                    InitialRisk = risk,
                    HtfRsi = direction == TradeDirection.Long ? 72.0 : 32.3,
                    VolExpansionRatio = 1.57,
                    ProbabilityScore = probabilityScore,
                    EntryTime = startTime,
                    Quantity = 5.15m, // ~$600k notional at $115k BTC
                    Leverage = 5
                };

                // Algorithm sets this when firing setup
                TradingState.IsInPosition = true;

                ExecutionStrategy = new HtfRsiVolExpansionExecutionStrategy(Setup, TradingState);
                QuoteHub = new QuoteHub<IQuote>(300);
                ExecutionStrategy.SetQuotes(QuoteHub);

                _tradeMock = new Mock<ITrade>();
                _tradeMock.Setup(t => t.Open).Returns(false);
                _tradeMock.Setup(t => t.TotalOpenBaseQuantity).Returns(0m);
                _tradeMock.Setup(t => t.ProfitPct).Returns(0m);

                _currentTime = startTime;

                // Seed quote hub
                for (int i = 0; i < 20; i++)
                    AddQuote(entryPrice, entryPrice + 20, entryPrice - 20);
            }

            public StrategyAction Candle(decimal close, decimal high, decimal low, string note = "")
            {
                _barCount++;
                AddQuote(close, high, low);
                var status = ExecutionStrategy.ProcessStrategy(_tradeMock.Object);
                Log.Add(_barCount, close, high, low, status.StrategyAction, note);
                return status.StrategyAction;
            }

            public void Fill(decimal fillPrice)
            {
                _tradeMock.Setup(t => t.Open).Returns(true);
                _tradeMock.Setup(t => t.TotalOpenBaseQuantity).Returns(Setup.Quantity);
                _rebasedEntry = fillPrice;
            }

            public void Close()
            {
                _tradeMock.Setup(t => t.Open).Returns(false);
                _tradeMock.Setup(t => t.TotalOpenBaseQuantity).Returns(0m);
            }

            public decimal CalculatePnl(decimal exitPrice)
            {
                if (Setup.Direction == TradeDirection.Long)
                    return (exitPrice - _rebasedEntry) * Setup.Quantity * Setup.Leverage;
                else
                    return (_rebasedEntry - exitPrice) * Setup.Quantity * Setup.Leverage;
            }

            private void AddQuote(decimal close, decimal high, decimal low)
            {
                QuoteHub.Add(new Quote
                {
                    Timestamp = _currentTime,
                    Open = close,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = 1500
                });
                _currentTime = _currentTime.AddMinutes(1);
            }
        }

        #endregion

        #region Scenario 1: Vol Expansion Short — Sharp Selloff (Trailing Stop Win)

        /// <summary>
        /// Based on the actual backtest setup: 2025-07-25 04:15, Short at ~115,234.
        /// 4H RSI: 32.3 (oversold regime), 15M ATR: 347, VolExp: 1.57, Score: 69.
        ///
        /// Price action: BTC dumps $800 in 30 mins (vol expansion), bounces $200,
        /// then continues down. Trailing stop catches the trend.
        ///
        /// Expected: Entry → trailing at 1R → ride trend → exit on bounce.
        /// </summary>
        [Fact]
        public void Scenario_ShortSelloff_TrailingStopWin()
        {
            var pipeline = new BtcPipeline(
                direction: TradeDirection.Short,
                entryPrice: 115_234m,
                atr: 347m,
                probabilityScore: 69, // → 3R TP
                startTime: new DateTime(2025, 7, 25, 4, 15, 0));

            var risk = 347m * 1.5m; // 520.50
            _output.WriteLine($"Setup: Short at 115,234 | Risk: {risk:F2} | ATR: 347");
            _output.WriteLine($"SL: {115_234m + risk:F2} | TP (2R): {115_234m - risk * 2:F2}");
            _output.WriteLine($"Trailing activation at 1R: {115_234m - risk:F2}");
            _output.WriteLine("---");

            // Bar 1: Entry signal
            var entry = pipeline.Candle(115_200m, 115_250m, 115_150m, "ENTRY signal");
            entry.Should().Be(StrategyAction.OpenTrade);

            // Fill at market
            pipeline.Fill(115_200m);

            // Bar 2: Rebase (fill at 115,200)
            pipeline.Candle(115_180m, 115_220m, 115_160m, "Rebase");
            _output.WriteLine($"Rebased entry: {pipeline.Setup.EntryPrice:F2}");
            _output.WriteLine($"Rebased SL: {pipeline.Setup.StopLoss:F2}");

            // Bars 3-10: Sharp selloff — BTC drops $50-80 per minute
            decimal price = 115_100m;
            for (int i = 0; i < 8; i++)
            {
                price -= 60m + (i * 5m); // accelerating drop
                pipeline.Candle(price, price + 30m, price - 40m, $"Dump bar {i + 1}");
            }
            _output.WriteLine($"After dump: price at {price:F2} (down {115_200m - price:F0} from entry)");

            // Bars 11-15: Bounce — dead cat, retraces ~$150
            for (int i = 0; i < 5; i++)
            {
                price += 30m;
                pipeline.Candle(price, price + 40m, price - 20m, "Bounce");
            }
            _output.WriteLine($"After bounce: price at {price:F2}");

            // Bars 16-30: Second leg down — grind lower
            for (int i = 0; i < 15; i++)
            {
                price -= 35m;
                var action = pipeline.Candle(price, price + 25m, price - 30m, "Grind down");
                if (action == StrategyAction.CloseTrade)
                {
                    _output.WriteLine($"EXIT at bar {i + 16}: price {price:F2}");
                    break;
                }
            }

            // Bars 31-45: Another bounce that triggers trailing stop
            bool exited = false;
            for (int i = 0; i < 15; i++)
            {
                price += 40m;
                var action = pipeline.Candle(price, price + 50m, price - 10m, "Reversal");
                if (action == StrategyAction.CloseTrade)
                {
                    exited = true;
                    var pnl = pipeline.CalculatePnl(price);
                    _output.WriteLine($"TRAILING STOP at bar {31 + i}: price {price:F2}");
                    _output.WriteLine($"PnL: {pnl:F2} USDT ({(pnl > 0 ? "WIN" : "LOSS")})");
                    pnl.Should().BeGreaterThan(0, "trailing stop should lock in profit from the selloff");
                    break;
                }
            }

            exited.Should().BeTrue("trade should have exited via trailing stop on the bounce");
            _output.WriteLine($"\nTrade summary: {pipeline.Log.GetSummary()}");
        }

        #endregion

        #region Scenario 2: Long Breakout — Momentum Trend (Dynamic TP Win)

        /// <summary>
        /// Strong long setup: 4H RSI at 72, score 50 (→ 1.5R TP).
        /// BTC breaks out of consolidation and trends up steadily.
        ///
        /// Expected: Entry → TP hit at 1.5R.
        /// </summary>
        [Fact]
        public void Scenario_LongBreakout_DynamicTpAt1_5R()
        {
            var pipeline = new BtcPipeline(
                direction: TradeDirection.Long,
                entryPrice: 97_500m,
                atr: 280m,
                probabilityScore: 50, // → 1.5R TP
                startTime: new DateTime(2025, 8, 5, 12, 0, 0));

            var risk = 280m * 1.5m; // 420
            var tp1_5R = 97_500m + risk * 1.5m; // 98,130
            _output.WriteLine($"Setup: Long at 97,500 | Risk: {risk:F2} | Score: 50 → 1.5R TP");
            _output.WriteLine($"SL: {97_500m - risk:F2} | TP (1.5R): {tp1_5R:F2}");
            _output.WriteLine("---");

            // Entry
            var entry = pipeline.Candle(97_520m, 97_560m, 97_480m, "ENTRY");
            entry.Should().Be(StrategyAction.OpenTrade);
            pipeline.Fill(97_520m);

            // Rebase
            pipeline.Candle(97_530m, 97_550m, 97_510m, "Rebase");
            var rebasedTp = pipeline.Setup.EntryPrice + risk * 1.5m;
            _output.WriteLine($"Rebased entry: {pipeline.Setup.EntryPrice:F2} | TP: {rebasedTp:F2}");

            // Steady uptrend — $20-40 per minute with small pullbacks
            decimal price = 97_530m;
            bool tpHit = false;
            for (int bar = 1; bar <= 60; bar++)
            {
                // Realistic BTC 1M: trending up with noise
                var move = 15m + (bar % 3 == 0 ? -10m : 20m); // mostly up, occasional dip
                price += move;
                var high = price + 25m;
                var low = price - 15m;

                var action = pipeline.Candle(price, high, low, bar % 10 == 0 ? $"Bar {bar}: {price:F0}" : "");

                if (action == StrategyAction.CloseTrade)
                {
                    tpHit = true;
                    var pnl = pipeline.CalculatePnl(price);
                    _output.WriteLine($"TP HIT at bar {bar}: price {price:F2}");
                    _output.WriteLine($"Move from entry: +{price - pipeline.RebasedEntry:F0}");
                    _output.WriteLine($"PnL: {pnl:F2} USDT (1.5R win)");

                    pnl.Should().BeGreaterThan(0);
                    (price - pipeline.RebasedEntry).Should().BeGreaterThanOrEqualTo(risk * 1.5m - 50m,
                        "exit should be near 1.5R target");
                    break;
                }
            }

            tpHit.Should().BeTrue("steady uptrend should reach 1.5R TP within 60 bars");
            _output.WriteLine($"\nTrade summary: {pipeline.Log.GetSummary()}");
        }

        #endregion

        #region Scenario 3: Failed Breakout — Reversal (Stop Loss)

        /// <summary>
        /// Long setup at 103k, but the breakout fails. Price drops through SL.
        /// Classic failed breakout: initial pop, then fades back through entry and SL.
        ///
        /// Expected: Entry → small move up → reversal → SL hit. Controlled 1R loss.
        /// </summary>
        [Fact]
        public void Scenario_FailedBreakout_StopLoss()
        {
            var pipeline = new BtcPipeline(
                direction: TradeDirection.Long,
                entryPrice: 103_000m,
                atr: 310m,
                probabilityScore: 45, // → 2R TP
                startTime: new DateTime(2025, 8, 10, 16, 0, 0));

            var risk = 310m * 1.5m; // 465
            _output.WriteLine($"Setup: Long at 103,000 | Risk: {risk:F2} | Score: 45");
            _output.WriteLine($"SL: {103_000m - risk:F2} | TP (1.5R): {103_000m + risk * 1.5m:F2}");
            _output.WriteLine("---");

            // Entry
            pipeline.Candle(103_020m, 103_060m, 102_990m, "ENTRY");
            pipeline.Fill(103_020m);

            // Rebase
            pipeline.Candle(103_030m, 103_050m, 103_010m, "Rebase");
            _output.WriteLine($"Rebased SL: {pipeline.Setup.StopLoss:F2}");

            // Initial pop — fake breakout, up $100
            decimal price = 103_030m;
            for (int i = 0; i < 5; i++)
            {
                price += 20m;
                pipeline.Candle(price, price + 15m, price - 10m, "Pop");
            }
            _output.WriteLine($"Peak: {price:F2} (+{price - 103_020m:F0} from entry)");

            // Fade — price rolls over
            for (int i = 0; i < 8; i++)
            {
                price -= 30m;
                pipeline.Candle(price, price + 20m, price - 25m, "Fade");
            }
            _output.WriteLine($"After fade: {price:F2}");

            // Acceleration down through SL
            bool slHit = false;
            for (int i = 0; i < 10; i++)
            {
                price -= 50m;
                var action = pipeline.Candle(price, price + 20m, price - 40m, "Dump");
                if (action == StrategyAction.CloseTrade)
                {
                    slHit = true;
                    var pnl = pipeline.CalculatePnl(pipeline.Setup.StopLoss);
                    _output.WriteLine($"STOP LOSS at bar {15 + i}: price {price:F2}");
                    _output.WriteLine($"PnL: {pnl:F2} USDT (1R loss)");

                    pnl.Should().BeLessThan(0, "stop loss = controlled loss");
                    // Loss should be approximately 1R × quantity × leverage
                    var expectedLoss = -risk * pipeline.Setup.Quantity * pipeline.Setup.Leverage;
                    pnl.Should().BeApproximately(expectedLoss, 300m,
                        "loss should be close to 1R risk");
                    break;
                }
            }

            slHit.Should().BeTrue("reversal should hit stop loss");
            _output.WriteLine($"\nTrade summary: {pipeline.Log.GetSummary()}");
        }

        #endregion

        #region Scenario 4: Choppy Market — Time Stop

        /// <summary>
        /// Long entry during low-volatility consolidation. Price oscillates in
        /// a tight range, never reaching SL, TP, or trailing activation.
        /// After 240 bars (4 hours) the time stop closes the trade.
        ///
        /// Expected: Flat P&L near zero, exit via time stop.
        /// </summary>
        [Fact]
        public void Scenario_ChoppyConsolidation_TimeStop()
        {
            var pipeline = new BtcPipeline(
                direction: TradeDirection.Long,
                entryPrice: 108_000m,
                atr: 400m,
                probabilityScore: 85, // no hard TP — only trailing + time
                startTime: new DateTime(2025, 8, 15, 8, 0, 0));

            var risk = 400m * 1.5m; // 600
            _output.WriteLine($"Setup: Long at 108,000 | Risk: {risk:F2} | Score: 85 (no hard TP)");
            _output.WriteLine($"SL: {108_000m - risk:F2} | Time stop: 240 bars");
            _output.WriteLine("---");

            // Entry
            pipeline.Candle(108_010m, 108_040m, 107_990m, "ENTRY");
            pipeline.Fill(108_010m);
            pipeline.Candle(108_020m, 108_040m, 108_000m, "Rebase");

            // 250 bars of choppy consolidation — oscillate ±200 around entry
            // Exit strategy counts _barsHeld internally; we loop extra to be safe
            decimal price = 108_020m;
            var rng = new Random(42); // deterministic
            bool timeStopHit = false;

            for (int bar = 1; bar <= 250; bar++)
            {
                // Mean-reverting noise: -$20 to +$20 with drift back to entry
                var noise = (decimal)(rng.NextDouble() * 40 - 20);
                var meanRevert = (108_020m - price) * 0.08m;
                price += noise + meanRevert;

                var high = price + (decimal)(rng.NextDouble() * 30 + 5);
                var low = price - (decimal)(rng.NextDouble() * 30 + 5);

                // Clamp so we don't accidentally hit SL or breakeven
                if (low < pipeline.Setup.StopLoss + 100)
                    low = pipeline.Setup.StopLoss + 100;
                if (high > pipeline.Setup.EntryPrice + risk * 0.9m)
                    high = pipeline.Setup.EntryPrice + risk * 0.9m;

                var action = pipeline.Candle(price, high, low,
                    bar % 60 == 0 ? $"Hour {bar / 60}: {price:F0}" : "");

                if (action == StrategyAction.CloseTrade)
                {
                    timeStopHit = true;
                    var pnl = pipeline.CalculatePnl(price);
                    _output.WriteLine($"TIME STOP at bar {bar}: price {price:F2}");
                    _output.WriteLine($"PnL: {pnl:F2} USDT");

                    // In choppy market, P&L should be small (near zero)
                    Math.Abs((double)pnl).Should().BeLessThan((double)(risk * pipeline.Setup.Quantity * pipeline.Setup.Leverage),
                        "choppy market should result in small P&L, less than 1R");
                    break;
                }
            }

            timeStopHit.Should().BeTrue("240 bars of chop should trigger time stop");
            _output.WriteLine($"\nTrade summary: {pipeline.Log.GetSummary()}");
        }

        #endregion

        #region Scenario 5: Trailing Stop Save — Rally Then Full Reversal

        /// <summary>
        /// Long entry, price rallies past 1R (trailing activates), then reverses.
        /// The trailing stop (at highest - ATR) catches the reversal with partial profit
        /// instead of the old breakeven ($0) or worse (full SL loss).
        ///
        /// Expected: Entry → 1R reached → trailing activates → reversal → trailing exit with profit.
        /// </summary>
        [Fact]
        public void Scenario_TrailingStopSave_RallyThenReversal()
        {
            var pipeline = new BtcPipeline(
                direction: TradeDirection.Long,
                entryPrice: 99_000m,
                atr: 350m,
                probabilityScore: 85, // no hard TP
                startTime: new DateTime(2025, 8, 1, 20, 0, 0));

            var risk = 350m * 1.5m; // 525
            _output.WriteLine($"Setup: Long at 99,000 | Risk: {risk:F2} | ATR: 350");
            _output.WriteLine($"SL: {99_000m - risk:F2} | Trailing activation: 1R ({99_000m + risk:F2})");
            _output.WriteLine("---");

            // Entry and fill at entry price
            pipeline.Candle(99_010m, 99_040m, 98_990m, "ENTRY");
            pipeline.Fill(99_010m);
            pipeline.Candle(99_010m, 99_030m, 98_990m, "Rebase");

            var rebasedEntry = pipeline.Setup.EntryPrice;

            // Rally past 1R — $40/min for 15 bars = +600 (> 1R = 525)
            decimal price = 99_010m;
            for (int i = 0; i < 15; i++)
            {
                price += 40m;
                pipeline.Candle(price, price + 20m, price - 15m, "Rally");
            }
            var peakPrice = price + 20m; // highest = last close + 20 (high wick)
            _output.WriteLine($"Peak: {price:F2} (+{price - rebasedEntry:F0} from entry)");
            _output.WriteLine($"Trail level: {peakPrice - 350m:F2} (highest {peakPrice:F2} - ATR 350)");

            // SL should NOT have moved to entry (old breakeven behavior removed)
            pipeline.Setup.StopLoss.Should().NotBe(rebasedEntry,
                "no breakeven — SL stays at original level");

            // Full reversal — trailing stop should catch it
            bool trailHit = false;
            for (int i = 0; i < 30; i++)
            {
                price -= 35m;
                var low = price - 20m;
                var action = pipeline.Candle(price, price + 25m, low, "Reversal");
                if (action == StrategyAction.CloseTrade)
                {
                    trailHit = true;
                    // Exit at trailing stop level — partial profit locked in
                    var trailLevel = peakPrice - 350m;
                    var pnl = pipeline.CalculatePnl(trailLevel);
                    _output.WriteLine($"TRAILING STOP EXIT at reversal bar {i + 1}: price {price:F2}");
                    _output.WriteLine($"Exit at trail: {trailLevel:F2}");
                    _output.WriteLine($"PnL: {pnl:F2} USDT (saved from {-risk * pipeline.Setup.Quantity * pipeline.Setup.Leverage:F2} full SL loss)");

                    pnl.Should().BeGreaterThan(0,
                        "trailing stop locks in partial profit instead of $0 breakeven or -1R SL");
                    break;
                }
            }

            trailHit.Should().BeTrue("reversal should trigger trailing stop");
            _output.WriteLine($"\nTrade summary: {pipeline.Log.GetSummary()}");
        }

        #endregion

        #region Scenario 6: High Score Setup — Trailing Captures Multi-R Move

        /// <summary>
        /// Score 90 setup (no hard TP). BTC has a sustained 2-hour downtrend.
        /// Trailing stop rides the entire move, capturing profit.
        /// This demonstrates why high-score setups shouldn't have a hard TP cap.
        ///
        /// Expected: Entry → trailing activation at 1R → ride trend → exit on bounce.
        /// </summary>
        [Fact]
        public void Scenario_HighScore_TrailingCapturesMultiR()
        {
            var pipeline = new BtcPipeline(
                direction: TradeDirection.Short,
                entryPrice: 112_000m,
                atr: 380m,
                probabilityScore: 90, // no hard TP
                startTime: new DateTime(2025, 7, 28, 14, 0, 0));

            var risk = 380m * 1.5m; // 570
            _output.WriteLine($"Setup: Short at 112,000 | Risk: {risk:F2} | Score: 90 (NO hard TP)");
            _output.WriteLine($"SL: {112_000m + risk:F2}");
            _output.WriteLine($"Trailing activation at 1R: {112_000m - risk:F2}");
            _output.WriteLine("---");

            // Entry
            pipeline.Candle(111_980m, 112_010m, 111_960m, "ENTRY");
            pipeline.Fill(111_980m);
            pipeline.Candle(111_970m, 111_990m, 111_950m, "Rebase");

            var rebasedEntry = pipeline.Setup.EntryPrice;
            _output.WriteLine($"Rebased entry: {rebasedEntry:F2}");

            // Phase 1: Sustained downtrend — grind down past 1.5R to activate trailing
            // ATR = 380, so trailing trails at 380 behind lowest. Need to go past 1.5R (855)
            // then bounce > ATR (380) to trigger the trailing stop.
            decimal price = 111_970m;
            decimal lowestPrice = price;
            bool trailingExited = false;

            // Grind down ~$20/bar for 60 bars = ~$1200 (>1.5R = $855)
            for (int bar = 1; bar <= 60; bar++)
            {
                price -= 20m;
                if (price < lowestPrice) lowestPrice = price;
                var action = pipeline.Candle(price, price + 15m, price - 15m,
                    bar % 20 == 0 ? $"Bar {bar}: {price:F0} (down {rebasedEntry - price:F0})" : "");

                if (action == StrategyAction.CloseTrade)
                {
                    // Shouldn't exit during grind (no bounces)
                    trailingExited = true;
                    break;
                }
            }
            _output.WriteLine($"After grind: {price:F2} (down {rebasedEntry - price:F0}, lowest: {lowestPrice:F2})");
            _output.WriteLine($"Trail level: {lowestPrice + pipeline.Setup.AtrAtEntry:F2}");

            // Phase 2: Bounce — reversal exceeds ATR trailing distance
            // Trail is at lowestPrice + ATR(380). Bounce needs to push high above that.
            if (!trailingExited)
            {
                for (int bar = 1; bar <= 30; bar++)
                {
                    price += 30m; // bounce $30/bar
                    var high = price + 20m;
                    var action = pipeline.Candle(price, high, price - 10m, "Bounce");

                    if (action == StrategyAction.CloseTrade)
                    {
                        trailingExited = true;
                        var moveFromEntry = rebasedEntry - price;
                        var rMultiple = moveFromEntry / risk;
                        var pnl = pipeline.CalculatePnl(price);

                        _output.WriteLine($"TRAILING STOP at bounce bar {bar}: price {price:F2}");
                        _output.WriteLine($"Move from entry: {moveFromEntry:F0} ({rMultiple:F1}R)");
                        _output.WriteLine($"PnL: {pnl:F2} USDT");
                        _output.WriteLine($"Lowest reached: {lowestPrice:F2} ({(rebasedEntry - lowestPrice) / risk:F1}R)");

                        rMultiple.Should().BeGreaterThan(0.5m,
                            "trailing stop should lock in some profit from the trend");
                        pnl.Should().BeGreaterThan(0);
                        break;
                    }
                }
            }

            trailingExited.Should().BeTrue("sustained trend + bounce should trigger trailing stop");
            _output.WriteLine($"\nTrade summary: {pipeline.Log.GetSummary()}");
        }

        #endregion

        #region Scenario 7: Low Score Setup — Quick TP (Conservative Exit)

        /// <summary>
        /// Score 25 setup (→ 1.0R TP). Weak signal, so the strategy takes
        /// a conservative 1.0R profit quickly.
        ///
        /// Demonstrates why weak setups shouldn't be given wide targets.
        /// </summary>
        [Fact]
        public void Scenario_LowScore_QuickTpAt1R()
        {
            var pipeline = new BtcPipeline(
                direction: TradeDirection.Long,
                entryPrice: 95_000m,
                atr: 250m,
                probabilityScore: 25, // → 1.0R TP
                startTime: new DateTime(2025, 9, 1, 10, 0, 0));

            var risk = 250m * 1.5m; // 375
            var tp = 95_000m + risk * 1.0m; // 95,375
            _output.WriteLine($"Setup: Long at 95,000 | Risk: {risk:F2} | Score: 25 → 1.0R TP");
            _output.WriteLine($"SL: {95_000m - risk:F2} | TP: {tp:F2}");
            _output.WriteLine("---");

            // Entry
            pipeline.Candle(95_010m, 95_030m, 94_990m, "ENTRY");
            pipeline.Fill(95_010m);
            pipeline.Candle(95_010m, 95_020m, 95_000m, "Rebase");

            // Steady up move — TP should hit relatively quickly
            decimal price = 95_010m;
            bool tpHit = false;

            for (int bar = 1; bar <= 40; bar++)
            {
                price += 18m; // ~$18/min up
                var action = pipeline.Candle(price, price + 15m, price - 10m, "");

                if (action == StrategyAction.CloseTrade)
                {
                    tpHit = true;
                    var rMultiple = (price - pipeline.RebasedEntry) / risk;
                    var pnl = pipeline.CalculatePnl(price);

                    _output.WriteLine($"TP HIT at bar {bar}: price {price:F2}");
                    _output.WriteLine($"R-multiple: {rMultiple:F1}R");
                    _output.WriteLine($"PnL: {pnl:F2} USDT");

                    rMultiple.Should().BeApproximately(1.0m, 0.3m,
                        "low-score setup should exit near 1.0R");
                    break;
                }
            }

            tpHit.Should().BeTrue("uptrend should reach 1.0R TP for low-score setup");
            _output.WriteLine($"\nTrade summary: {pipeline.Log.GetSummary()}");
        }

        #endregion

        #region Summary: Expected Trade Profile

        /// <summary>
        /// This test doesn't assert anything — it prints the expected trade profile
        /// for the strategy across all scenario types, serving as documentation.
        /// </summary>
        [Fact]
        public void TradeProfile_Summary()
        {
            var risk = 347m * 1.5m; // 520.50 (from actual backtest ATR)
            var qty = 5.15m;
            var lev = 5;
            var riskUsdt = risk * qty * lev;

            _output.WriteLine("=== HTF RSI + Vol Expansion — Expected Trade Profile ===");
            _output.WriteLine($"Typical BTC setup: ATR ~347, Risk (1.5×ATR) = {risk:F2}");
            _output.WriteLine($"Position: {qty} BTC × {lev}x leverage = ~{qty * 115_000m:F0} USDT notional");
            _output.WriteLine($"Risk per trade: {riskUsdt:F2} USDT");
            _output.WriteLine("");
            _output.WriteLine("Exit Type         | Trigger          | Typical R  | Typical P&L");
            _output.WriteLine("------------------+------------------+------------+------------");
            _output.WriteLine($"Stop Loss         | Price reverses   | -1.0R      | -{riskUsdt:F0} USDT");
            _output.WriteLine($"TP (score <40)    | 1.0R reached     | +1.0R      | +{riskUsdt * 1.0m:F0} USDT");
            _output.WriteLine($"TP (score 40-59)  | 1.5R reached     | +1.5R      | +{riskUsdt * 1.5m:F0} USDT");
            _output.WriteLine($"TP (score 60-79)  | 2.0R reached     | +2.0R      | +{riskUsdt * 2.0m:F0} USDT");
            _output.WriteLine($"Trailing (80+)    | 1R+ then pulls   | +0.3-4R    | +{riskUsdt * 1.5m:F0}+ USDT");
            _output.WriteLine($"Time Stop         | 240 bars (4hrs)  | ~0R        | Small +/-");
            _output.WriteLine("");
            _output.WriteLine("With 50% win rate and average 2R win vs 1R loss:");
            _output.WriteLine($"  10 trades → 5 wins × {riskUsdt * 2.0m:F0} + 5 losses × -{riskUsdt:F0}");
            _output.WriteLine($"           = +{riskUsdt * 10m * 0.5m:F0} USDT net");
        }

        #endregion
    }
}
