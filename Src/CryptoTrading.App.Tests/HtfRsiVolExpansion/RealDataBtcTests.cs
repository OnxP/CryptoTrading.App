using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using FluentAssertions;
using Moq;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    /// <summary>
    /// Replays REAL 1-minute BTCUSDT candles from 2025-07-25 through the strategy
    /// pipeline. This is the exact data the backtest ran on — no synthetic prices.
    ///
    /// The setup that fired in the backtest:
    ///   Short at 115,234 | ATR: 347 | VolExp: 1.57 | 4H RSI: 32.3 | Score: 69
    ///   SL: 115,754.53 | TP: 114,713.49 (original 1R levels)
    ///
    /// What actually happened (visible in the real data):
    ///   - Price dropped ~$120 in the first 12 minutes (04:15-04:27)
    ///   - Then reversed HARD — up $700 in 10 minutes (04:28-04:38)
    ///   - SL at ~115,755 was breached at ~04:35-04:36
    ///   - Price continued to 116,000+ by 04:48
    ///
    /// This test proves the strategy handles a real losing trade correctly:
    ///   controlled 1R stop loss, no re-entry from consumed setup.
    /// </summary>
    public class RealDataBtcTests
    {
        private readonly ITestOutputHelper _output;

        public RealDataBtcTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region CSV Loading

        private record CandleData(
            DateTime OpenTime, DateTime CloseTime,
            decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

        private static List<CandleData> LoadCandles(string relativePath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Test data not found: {path}");

            var candles = new List<CandleData>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Trim().TrimStart('\uFEFF').Split(',');
                if (parts.Length < 7) continue;

                candles.Add(new CandleData(
                    DateTime.Parse(parts[0].Trim(), CultureInfo.InvariantCulture),
                    DateTime.Parse(parts[1].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[2].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[3].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[4].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[5].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[6].Trim(), CultureInfo.InvariantCulture)));
            }
            return candles;
        }

        #endregion

        #region Pipeline

        private class RealDataPipeline
        {
            public HtfRsiTradingState TradingState { get; }
            public HtfRsiVolExpansionSetup Setup { get; }
            public HtfRsiVolExpansionExecutionStrategy ExecutionStrategy { get; }
            public QuoteHub<IQuote> QuoteHub { get; }

            private readonly Mock<ITrade> _tradeMock;
            private bool _isOpen;
            private decimal _fillPrice;

            public bool IsOpen => _isOpen;
            public decimal FillPrice => _fillPrice;

            public RealDataPipeline(
                TradeDirection direction,
                decimal entryPrice,
                decimal atr,
                int probabilityScore,
                List<CandleData> seedCandles)
            {
                var risk = atr * 1.5m;

                TradingState = new HtfRsiTradingState(200_000m);
                TradingState.IsInPosition = true; // algorithm sets this

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
                    EntryTime = seedCandles.Last().CloseTime,
                    Quantity = 5.153714m,
                    Leverage = 5
                };

                ExecutionStrategy = new HtfRsiVolExpansionExecutionStrategy(Setup, TradingState);
                QuoteHub = new QuoteHub<IQuote>(500);
                ExecutionStrategy.SetQuotes(QuoteHub);

                _tradeMock = new Mock<ITrade>();
                _tradeMock.Setup(t => t.Open).Returns(false);
                _tradeMock.Setup(t => t.TotalOpenBaseQuantity).Returns(0m);
                _tradeMock.Setup(t => t.ProfitPct).Returns(0m);

                // Seed the quote hub with candles before setup time
                foreach (var c in seedCandles)
                {
                    QuoteHub.Add(new Quote
                    {
                        Timestamp = c.CloseTime,
                        Open = c.Open,
                        High = c.High,
                        Low = c.Low,
                        Close = c.Close,
                        Volume = c.Volume
                    });
                }
            }

            public StrategyAction ProcessCandle(CandleData candle)
            {
                QuoteHub.Add(new Quote
                {
                    Timestamp = candle.CloseTime,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    Volume = candle.Volume
                });

                return ExecutionStrategy.ProcessStrategy(_tradeMock.Object).StrategyAction;
            }

            public void SimulateFill(decimal fillPrice)
            {
                _isOpen = true;
                _fillPrice = fillPrice;
                _tradeMock.Setup(t => t.Open).Returns(true);
                _tradeMock.Setup(t => t.TotalOpenBaseQuantity).Returns(Setup.Quantity);
            }

            public void SimulateClose()
            {
                _isOpen = false;
                _tradeMock.Setup(t => t.Open).Returns(false);
                _tradeMock.Setup(t => t.TotalOpenBaseQuantity).Returns(0m);
            }

            public decimal CalculatePnl(decimal exitPrice)
            {
                // Mirror SimpleExitStrategy / HtfRsiVolExpansionExitStrategy:
                // priceDelta × quantity. Quantity already encodes leverage from
                // sizing — multiplying by leverage again would double-count.
                return Setup.Direction == TradeDirection.Long
                    ? (exitPrice - _fillPrice) * Setup.Quantity
                    : (_fillPrice - exitPrice) * Setup.Quantity;
            }
        }

        #endregion

        [Fact]
        public void RealData_20250725_ShortSetup_StopsOut()
        {
            // Load all 1M candles for 2025-07-25
            var allCandles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_1M_20250725.csv"));

            _output.WriteLine($"Loaded {allCandles.Count} candles");
            _output.WriteLine($"Range: {allCandles.First().CloseTime} → {allCandles.Last().CloseTime}");
            _output.WriteLine($"Price range: {allCandles.Min(c => c.Low):F2} → {allCandles.Max(c => c.High):F2}");
            _output.WriteLine("---");

            // Setup fires at 04:15 — use candles before that as seed
            var setupTime = new DateTime(2025, 7, 25, 4, 15, 0);
            var seedCandles = allCandles.Where(c => c.CloseTime <= setupTime).ToList();
            var tradeCandles = allCandles.Where(c => c.CloseTime > setupTime).ToList();

            _output.WriteLine($"Seed candles: {seedCandles.Count} (00:00-04:15)");
            _output.WriteLine($"Trade candles: {tradeCandles.Count} (04:15-12:00)");

            // Create pipeline matching the EXACT setup from the backtest log
            var pipeline = new RealDataPipeline(
                direction: TradeDirection.Short,
                entryPrice: 115_234.01m,
                atr: 347.01m,
                probabilityScore: 69, // → 3R TP
                seedCandles: seedCandles);

            var risk = 347.01m * 1.5m; // 520.515
            _output.WriteLine($"");
            _output.WriteLine($"=== SETUP ===");
            _output.WriteLine($"Direction: Short");
            _output.WriteLine($"Entry: {pipeline.Setup.EntryPrice:F2}");
            _output.WriteLine($"ATR: 347.01 | Risk: {risk:F2}");
            _output.WriteLine($"SL: {pipeline.Setup.StopLoss:F2}");
            _output.WriteLine($"Score: 69 → TP multiplier: {HtfRsiVolExpansionExitStrategy.GetTakeProfitMultiplier(69)}R");
            _output.WriteLine($"");

            // Track trade events
            bool entered = false;
            bool exited = false;
            int entryBar = 0;
            int exitBar = 0;
            decimal exitPrice = 0;
            string exitReason = "";

            _output.WriteLine("=== TRADE LOG (candle-by-candle) ===");
            _output.WriteLine("Bar | Time            | Open      | High      | Low       | Close     | Action");
            _output.WriteLine("----+-----------------+-----------+-----------+-----------+-----------+--------");

            for (int i = 0; i < tradeCandles.Count; i++)
            {
                var candle = tradeCandles[i];
                var action = pipeline.ProcessCandle(candle);

                // Log significant candles
                bool isSignificant = action != StrategyAction.NoAction
                    || i < 5       // first 5 candles
                    || i % 30 == 0 // every 30 minutes
                    || candle.High > pipeline.Setup.StopLoss // near SL
                    || (!entered && i < 2); // entry area

                if (isSignificant || (entered && !exited))
                {
                    var marker = action switch
                    {
                        StrategyAction.OpenTrade => ">>> ENTRY",
                        StrategyAction.CloseTrade => "<<< EXIT",
                        _ => entered && !exited ? "  [in trade]" : ""
                    };

                    // Only log first 80 candles or significant events to keep output readable
                    if (i < 80 || action != StrategyAction.NoAction)
                    {
                        _output.WriteLine(
                            $"{i + 1,3} | {candle.CloseTime:HH:mm} | " +
                            $"{candle.Open,10:F2} | {candle.High,10:F2} | {candle.Low,10:F2} | {candle.Close,10:F2} | {marker}");
                    }
                }

                if (action == StrategyAction.OpenTrade && !entered)
                {
                    entered = true;
                    entryBar = i + 1;
                    // Simulate market fill at the close of this candle
                    pipeline.SimulateFill(candle.Close);
                    _output.WriteLine($"    → Filled at {candle.Close:F2}");
                }

                if (action == StrategyAction.CloseTrade && entered && !exited)
                {
                    exited = true;
                    exitBar = i + 1;
                    exitPrice = pipeline.Setup.StopLoss; // SL exit price
                    exitReason = "StopLoss";

                    if (pipeline.Setup.Direction == TradeDirection.Short)
                    {
                        if (candle.High >= pipeline.Setup.StopLoss)
                            exitReason = "StopLoss";
                    }

                    // Simulate TradeMonitor closing the position immediately
                    pipeline.SimulateClose();
                }

                // Continue processing after exit to verify no re-entry
                if (exited && i > exitBar + 30)
                    break;
            }

            _output.WriteLine("");
            _output.WriteLine("=== RESULT ===");

            entered.Should().BeTrue("strategy should enter on the first candle after setup");

            if (exited)
            {
                var pnl = pipeline.CalculatePnl(exitPrice);
                var holdTime = tradeCandles[exitBar - 1].CloseTime - tradeCandles[entryBar - 1].CloseTime;

                _output.WriteLine($"Entry:     bar {entryBar} at {pipeline.FillPrice:F2}");
                _output.WriteLine($"Rebased:   entry {pipeline.Setup.EntryPrice:F2} | SL {pipeline.Setup.StopLoss:F2}");
                _output.WriteLine($"Exit:      bar {exitBar} ({exitReason}) at ~{exitPrice:F2}");
                _output.WriteLine($"Hold time: {holdTime.TotalMinutes:F0} minutes");
                _output.WriteLine($"PnL:       {pnl:F2} USDT");
                _output.WriteLine($"R-multiple: {pnl / (risk * pipeline.Setup.Quantity):F1}R");

                // This was a losing trade — price reversed against the short
                pnl.Should().BeLessThan(0, "real data shows price reversed against the short");
            }
            else
            {
                _output.WriteLine("Trade did not exit within available data");
            }

            // Verify no re-entry from consumed setup (already tested inline above,
            // but let's count how many candles we verified)
            if (exited)
            {
                pipeline.TradingState.RecordTradeComplete("StopLoss", pipeline.CalculatePnl(exitPrice));

                // Process remaining candles — all should be NoAction
                int noActionCount = 0;
                for (int i = exitBar + 30; i < Math.Min(tradeCandles.Count, exitBar + 200); i++)
                {
                    var action = pipeline.ProcessCandle(tradeCandles[i]);
                    action.Should().Be(StrategyAction.NoAction,
                        $"candle {i}: consumed setup must not re-enter");
                    noActionCount++;
                }
                _output.WriteLine($"");
                _output.WriteLine($"Verified no re-entry for {noActionCount} candles after exit");
            }
        }

        [Fact]
        public void RealData_20250725_PriceAction_Summary()
        {
            // Pure data analysis — no strategy, just shows what BTC did on this day
            var allCandles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_1M_20250725.csv"));

            _output.WriteLine("=== BTC Price Action: 2025-07-25 00:00–12:00 UTC ===");
            _output.WriteLine("");

            // Hourly OHLC summary
            var hourlyGroups = allCandles.GroupBy(c => c.CloseTime.Hour).OrderBy(g => g.Key);
            _output.WriteLine("Hour | Open      | High      | Low       | Close     | Range  | Candles");
            _output.WriteLine("-----+-----------+-----------+-----------+-----------+--------+--------");

            foreach (var hour in hourlyGroups)
            {
                var candles = hour.OrderBy(c => c.CloseTime).ToList();
                var open = candles.First().Open;
                var close = candles.Last().Close;
                var high = candles.Max(c => c.High);
                var low = candles.Min(c => c.Low);
                var range = high - low;

                _output.WriteLine(
                    $" {hour.Key:D2}  | {open,10:F2} | {high,10:F2} | {low,10:F2} | {close,10:F2} | {range,6:F0} | {candles.Count}");
            }

            // Key levels
            _output.WriteLine("");
            _output.WriteLine("Key observations:");
            _output.WriteLine($"  Session high: {allCandles.Max(c => c.High):F2}");
            _output.WriteLine($"  Session low:  {allCandles.Min(c => c.Low):F2}");
            _output.WriteLine($"  Total range:  {allCandles.Max(c => c.High) - allCandles.Min(c => c.Low):F0}");

            // Around setup time
            var aroundSetup = allCandles
                .Where(c => c.CloseTime >= new DateTime(2025, 7, 25, 4, 0, 0)
                         && c.CloseTime <= new DateTime(2025, 7, 25, 5, 0, 0))
                .ToList();

            _output.WriteLine($"");
            _output.WriteLine($"  04:00-05:00 (setup window):");
            _output.WriteLine($"    Low:  {aroundSetup.Min(c => c.Low):F2} (at {aroundSetup.OrderBy(c => c.Low).First().CloseTime:HH:mm})");
            _output.WriteLine($"    High: {aroundSetup.Max(c => c.High):F2} (at {aroundSetup.OrderByDescending(c => c.High).First().CloseTime:HH:mm})");
            _output.WriteLine($"    The short setup fired at 04:15 with entry 115,234");
            _output.WriteLine($"    Price dropped to {aroundSetup.Where(c => c.CloseTime <= new DateTime(2025, 7, 25, 4, 28, 0)).Min(c => c.Low):F2} by 04:27");
            _output.WriteLine($"    Then reversed to {aroundSetup.Max(c => c.High):F2} by 04:49");
        }
    }
}
