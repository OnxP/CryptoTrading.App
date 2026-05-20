using Binance;
using Binance.Client;
using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Database.Config;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.RequestTracker;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    /// <summary>
    /// Replay test: feeds the full BTCUSDT 15M history through the real
    /// HtfRsiVolExpansionAlgorithm pipeline (via a fake IMarketDataEvents)
    /// and compares the fired setups against the target trade list produced
    /// by the Python backtest.
    ///
    /// Tolerances (some variability allowed):
    ///   Entry time: within ±2 × 15M candles (±30 minutes)
    ///   Entry price: within 0.3% of target
    ///   Direction: must match exactly
    ///
    /// Each captured setup is immediately "closed" (via reflection on the
    /// algorithm's private trading state) so the next setup can fire.
    /// </summary>
    public class AlgorithmReplayTests
    {
        private readonly ITestOutputHelper _output;

        public AlgorithmReplayTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region CSV Loading

        /// <summary>
        /// BTCUSDT 15M.csv layout:
        ///   col0 = CloseTime, col1 = OpenTime, col2..5 = OHLC, col6 = Volume
        /// </summary>
        private record Candle15M(
            DateTime OpenTime, DateTime CloseTime,
            decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

        private static List<Candle15M> LoadCandles(string relativePath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Test data not found: {path}");

            var candles = new List<Candle15M>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Trim().TrimStart('\uFEFF').Split(',');
                if (parts.Length < 7) continue;

                DateTime closeTime, openTime;
                if (!DateTime.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out closeTime))
                    continue;
                if (!DateTime.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out openTime))
                    continue;

                candles.Add(new Candle15M(
                    openTime, closeTime,
                    decimal.Parse(parts[2].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[3].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[4].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[5].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[6].Trim(), CultureInfo.InvariantCulture)));
            }

            // Sort by close time to be safe
            return candles.OrderBy(c => c.CloseTime).ToList();
        }

        private record TargetTrade(
            int Index,
            TradeDirection Direction,
            DateTime EntryTime,
            decimal EntryPrice,
            int EntryRowIndex,
            decimal EntryCandleO,
            decimal EntryCandleH,
            decimal EntryCandleL,
            decimal EntryCandleC,
            decimal StopLoss,
            decimal TakeProfit,
            decimal RiskPts,
            decimal AtrAtEntry,
            decimal Atr20Ago,
            decimal VolExpansion,
            double HtfRsi,
            double LtfRsi,
            int HtfBarIndex,
            DateTime HtfBarDateTime);

        private static List<TargetTrade> LoadTargetTrades(string relativePath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Target trade list not found: {path}");

            var trades = new List<TargetTrade>();
            var lines = File.ReadAllLines(path);
            // Skip header
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim().TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 19) continue;

                var dirStr = parts[1].Trim().ToLowerInvariant();
                var direction = dirStr == "long" ? TradeDirection.Long : TradeDirection.Short;

                trades.Add(new TargetTrade(
                    Index: int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture),
                    Direction: direction,
                    EntryTime: DateTime.Parse(parts[2].Trim(), CultureInfo.InvariantCulture),
                    EntryPrice: decimal.Parse(parts[3].Trim(), CultureInfo.InvariantCulture),
                    EntryRowIndex: int.Parse(parts[4].Trim(), CultureInfo.InvariantCulture),
                    EntryCandleO: decimal.Parse(parts[5].Trim(), CultureInfo.InvariantCulture),
                    EntryCandleH: decimal.Parse(parts[6].Trim(), CultureInfo.InvariantCulture),
                    EntryCandleL: decimal.Parse(parts[7].Trim(), CultureInfo.InvariantCulture),
                    EntryCandleC: decimal.Parse(parts[8].Trim(), CultureInfo.InvariantCulture),
                    StopLoss: decimal.Parse(parts[9].Trim(), CultureInfo.InvariantCulture),
                    TakeProfit: decimal.Parse(parts[10].Trim(), CultureInfo.InvariantCulture),
                    RiskPts: decimal.Parse(parts[11].Trim(), CultureInfo.InvariantCulture),
                    AtrAtEntry: decimal.Parse(parts[12].Trim(), CultureInfo.InvariantCulture),
                    Atr20Ago: decimal.Parse(parts[13].Trim(), CultureInfo.InvariantCulture),
                    VolExpansion: decimal.Parse(parts[14].Trim(), CultureInfo.InvariantCulture),
                    HtfRsi: double.Parse(parts[15].Trim(), CultureInfo.InvariantCulture),
                    LtfRsi: double.Parse(parts[16].Trim(), CultureInfo.InvariantCulture),
                    HtfBarIndex: int.Parse(parts[17].Trim(), CultureInfo.InvariantCulture),
                    HtfBarDateTime: DateTime.Parse(parts[18].Trim(), CultureInfo.InvariantCulture)));
            }
            return trades;
        }

        #endregion

        #region Fake Market Data

        /// <summary>
        /// Captures the historic-load and live-stream callbacks that
        /// HtfRsiVolExpansionAlgorithm registers in its Subscribe method.
        /// The test can then invoke them directly to replay candles.
        /// </summary>
        private class FakeMarketData : IMarketDataEvents
        {
            public Action<IEnumerable<ExchangeCandlestick>> HistoricLoad15M;
            public Action<ExchangeCandlestickEvent> LiveStream15M;
            public Action<IEnumerable<ExchangeCandlestick>> HistoricLoad4H;
            public Action<ExchangeCandlestickEvent> LiveStream4H;

            public void InitialDataLoadSubscribe(string symbol, CandleInterval interval,
                Action<IEnumerable<ExchangeCandlestick>> callback)
            {
                if (interval == CandleInterval.Minute_15)
                    HistoricLoad15M = callback;
                else if (interval == CandleInterval.Hour_4)
                    HistoricLoad4H = callback;
            }

            public void InitialDataLoadUnSubscribe(string symbol, CandleInterval interval) { }

            public void InitialDataStreamSubscribe(string symbol, CandleInterval interval,
                Action<ExchangeCandlestickEvent> callback)
            {
                if (interval == CandleInterval.Minute_15)
                    LiveStream15M = callback;
                else if (interval == CandleInterval.Hour_4)
                    LiveStream4H = callback;
            }

            public void InitialDataStreamUnSubscribe(string symbol, CandleInterval interval) { }
        }

        #endregion

        #region Captured Setup + Matcher

        private record CapturedSetup(
            DateTime EntryTime,
            TradeDirection Direction,
            decimal EntryPrice,
            decimal StopLoss,
            decimal TakeProfit,
            decimal AtrAtEntry,
            double HtfRsi,
            int ProbabilityScore,
            double VolExpansion);

        private static readonly TimeSpan EntryTimeTolerance = TimeSpan.FromMinutes(30);
        private const decimal EntryPriceTolerancePct = 0.003m; // 0.3%

        private static CapturedSetup FindMatch(TargetTrade target, List<CapturedSetup> captured)
        {
            var priceTol = target.EntryPrice * EntryPriceTolerancePct;
            foreach (var c in captured)
            {
                if (c.Direction != target.Direction) continue;
                var dt = (c.EntryTime - target.EntryTime).Duration();
                if (dt > EntryTimeTolerance) continue;
                var dp = Math.Abs(c.EntryPrice - target.EntryPrice);
                if (dp > priceTol) continue;
                return c;
            }
            return null;
        }

        #endregion

        #region Replay Pipeline

        /// <summary>
        /// Drives an HtfRsiVolExpansionAlgorithm instance by feeding live
        /// 15M candles one at a time and captures any setups that fire via
        /// the global RequestTracker singleton. After each captured setup,
        /// the algorithm's private trading state is reset (IsInPosition =
        /// false, gap counter bumped) so the next setup can fire.
        ///
        /// The algorithm also receives native 4H candles: historic bulk load
        /// at startup, then live stream as each 15M candle completes (checking
        /// whether a 4H boundary was crossed).
        /// </summary>
        private List<CapturedSetup> ReplayAlgorithm(List<Candle15M> candles)
        {
            // Clear singleton state (important — RequestTracker is a static singleton)
            RequestTracker.Signals?.Clear();
            _ = RequestTracker.Instance; // forces init
            RequestTracker.Signals.Clear();

            var logger = Mock.Of<ILogger<HtfRsiVolExpansionAlgorithm>>();
            var algo = new HtfRsiVolExpansionAlgorithm(logger);

            var config = new CryptoConfig
            {
                Interval = CandleInterval.Minute_15,
                RunType = RunTypeEnum.BackTesting,
                StartBtcAmount = 2.0 // → ~200k USDT equity at BTC ~100k
            };
            algo.Configure(config);

            var fakeMd = new FakeMarketData();
            algo.Subscribe(Symbol.BTC_USDT, fakeMd);

            // Load native 4H data. Feed bars that close BEFORE the first
            // 15M candle as historic (gives RSI warmup), then live-stream
            // subsequent 4H bars during the 15M replay at the correct time.
            var quotes4H = Load4HCsv(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_4H.csv"));
            var firstCandleTime = candles[0].CloseTime;
            var seed4H = quotes4H.Where(q => q.Timestamp < firstCandleTime).ToList();
            var live4H = quotes4H.Where(q => q.Timestamp >= firstCandleTime)
                .ToDictionary(q => q.Timestamp);

            var seedCandlesticks4H = seed4H.Select(q => new ExchangeCandlestick
            {
                Symbol = "BTCUSDT",
                Interval = CandleInterval.Hour_4,
                OpenTime = q.Timestamp.AddHours(-4),
                CloseTime = q.Timestamp,
                Open = q.Open, High = q.High, Low = q.Low, Close = q.Close,
                Volume = q.Volume,
                QuoteVolume = 0m,
                NumberOfTrades = 0,
                IsClosed = true,
            });

            // Empty 15M historic load — algorithm warms up from the live stream
            fakeMd.HistoricLoad15M?.Invoke(Enumerable.Empty<ExchangeCandlestick>());
            // Seed 4H historic — bars before the 15M data starts
            fakeMd.HistoricLoad4H?.Invoke(seedCandlesticks4H);

            // Reflect to access the private _tradingState so we can reset
            // the "in position" flag after each captured setup.
            var tsField = typeof(HtfRsiVolExpansionAlgorithm)
                .GetField("_tradingState", BindingFlags.NonPublic | BindingFlags.Instance);
            tsField.Should().NotBeNull("need access to _tradingState to reset position between trades");

            var captured = new List<CapturedSetup>();

            foreach (var c in candles)
            {
                // If a native 4H bar closes at this exact time, fire it
                // BEFORE the 15M candle so the algorithm's RSI is up-to-date.
                if (live4H.TryGetValue(c.CloseTime, out var htf))
                {
                    var cs4H = new ExchangeCandlestick
                    {
                        Symbol = "BTCUSDT",
                        Interval = CandleInterval.Hour_4,
                        OpenTime = htf.Timestamp.AddHours(-4),
                        CloseTime = htf.Timestamp,
                        Open = htf.Open, High = htf.High,
                        Low = htf.Low, Close = htf.Close,
                        Volume = htf.Volume,
                        QuoteVolume = 0m,
                        NumberOfTrades = 0,
                        IsClosed = true,
                    };
                    fakeMd.LiveStream4H?.Invoke(new ExchangeCandlestickEvent(cs4H, DateTime.UtcNow));
                }

                // Feed the 15M candle
                var volume = c.Volume < 0 ? 0m : c.Volume;
                var cs = new ExchangeCandlestick
                {
                    Symbol = "BTCUSDT",
                    Interval = CandleInterval.Minute_15,
                    OpenTime = c.OpenTime,
                    CloseTime = c.CloseTime,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = volume,
                    QuoteVolume = 0m,
                    NumberOfTrades = 0,
                    IsClosed = true,
                };

                var args = new ExchangeCandlestickEvent(cs, DateTime.UtcNow);

                fakeMd.LiveStream15M?.Invoke(args);

                // Check for a fired setup on BTCUSDT
                if (RequestTracker.Signals.TryRemove("BTCUSDT", out var tuple))
                {
                    var signal = tuple.Signal;

                    captured.Add(new CapturedSetup(
                        EntryTime: signal.SignalTime,
                        Direction: signal.Direction,
                        EntryPrice: signal.EntryPrice,
                        StopLoss: signal.StopLoss,
                        TakeProfit: signal.TakeProfit,
                        AtrAtEntry: signal.AtrAtSignal,
                        HtfRsi: (double)(signal.HtfRsi ?? 0m),
                        ProbabilityScore: (int)(signal.ProbabilityScore ?? 0m),
                        VolExpansion: (double)(signal.VolExpansionRatio ?? 0m)));

                    // Simulate an instantaneous, successful trade close so
                    // the production gap/cooldown logic applies naturally:
                    // RecordTradeComplete sets IsInPosition=false,
                    // CandlesSinceLastExit=0, counts as a win (no cooldown).
                    var ts = (HtfRsiTradingState)tsField.GetValue(algo);
                    ts.RecordTradeComplete("TakeProfit", 0m);
                }
            }

            return captured;
        }

        #endregion

        [Fact]
        public void Algorithm_ReplayFullHistory_MatchesTargetTradeList()
        {
            var candles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_15M.csv"));
            var targets = LoadTargetTrades(
                Path.Combine("HtfRsiVolExpansion", "TestData", "trade_list_target.csv"));

            _output.WriteLine($"Loaded {candles.Count} 15M candles ({candles.First().CloseTime:yyyy-MM-dd HH:mm} → {candles.Last().CloseTime:yyyy-MM-dd HH:mm})");
            _output.WriteLine($"Loaded {targets.Count} target trades");
            _output.WriteLine("");

            var captured = ReplayAlgorithm(candles);

            _output.WriteLine($"Algorithm fired {captured.Count} setups");
            _output.WriteLine("");

            // Match target ↔ captured
            var matched = new List<(TargetTrade target, CapturedSetup captured)>();
            var unmatched = new List<TargetTrade>();
            var usedCaptures = new HashSet<CapturedSetup>();

            foreach (var target in targets)
            {
                // Find first unused captured setup within tolerance
                var priceTol = target.EntryPrice * EntryPriceTolerancePct;
                CapturedSetup match = null;
                foreach (var c in captured)
                {
                    if (usedCaptures.Contains(c)) continue;
                    if (c.Direction != target.Direction) continue;
                    var dt = (c.EntryTime - target.EntryTime).Duration();
                    if (dt > EntryTimeTolerance) continue;
                    var dp = Math.Abs(c.EntryPrice - target.EntryPrice);
                    if (dp > priceTol) continue;
                    match = c;
                    break;
                }

                if (match != null)
                {
                    matched.Add((target, match));
                    usedCaptures.Add(match);
                }
                else
                {
                    unmatched.Add(target);
                }
            }

            var extraCaptures = captured.Where(c => !usedCaptures.Contains(c)).ToList();

            _output.WriteLine("=== MATCH SUMMARY ===");
            _output.WriteLine($"Matched:   {matched.Count} / {targets.Count} ({100.0 * matched.Count / targets.Count:F1}%)");
            _output.WriteLine($"Missing:   {unmatched.Count} target trades not produced by algorithm");
            _output.WriteLine($"Extra:     {extraCaptures.Count} algorithm setups with no target match");
            _output.WriteLine("");

            if (unmatched.Count > 0)
            {
                _output.WriteLine("=== First 10 MISSING target trades ===");
                foreach (var t in unmatched.Take(10))
                {
                    _output.WriteLine(
                        $"  #{t.Index,3} {t.Direction,-5} {t.EntryTime:yyyy-MM-dd HH:mm} " +
                        $"@ {t.EntryPrice:F2} | ATR:{t.AtrAtEntry:F2} | 4H RSI:{t.HtfRsi:F1}");
                }
                _output.WriteLine("");
            }

            if (extraCaptures.Count > 0)
            {
                _output.WriteLine("=== First 10 EXTRA algorithm setups (no target) ===");
                foreach (var c in extraCaptures.Take(10))
                {
                    _output.WriteLine(
                        $"  {c.Direction,-5} {c.EntryTime:yyyy-MM-dd HH:mm} " +
                        $"@ {c.EntryPrice:F2} | ATR:{c.AtrAtEntry:F2} | 4H RSI:{c.HtfRsi:F1} | Score:{c.ProbabilityScore}");
                }
                _output.WriteLine("");
            }

            if (matched.Count > 0)
            {
                _output.WriteLine("=== First 5 MATCHED trades ===");
                foreach (var (t, c) in matched.Take(5))
                {
                    var dtMin = (c.EntryTime - t.EntryTime).TotalMinutes;
                    var dpPct = 100.0 * (double)((c.EntryPrice - t.EntryPrice) / t.EntryPrice);
                    _output.WriteLine(
                        $"  #{t.Index,3} {t.Direction,-5} target {t.EntryTime:yyyy-MM-dd HH:mm} @ {t.EntryPrice:F2} " +
                        $"| algo {c.EntryTime:HH:mm} @ {c.EntryPrice:F2} " +
                        $"| Δt:{dtMin:+0;-0}min Δp:{dpPct:+0.00;-0.00}%");
                }
                _output.WriteLine("");
            }

            // Diagnostic: compare score and RSI distributions of matched vs extra
            {
                var matchedScores = matched.Select(m => m.captured.ProbabilityScore).ToList();
                var extraScores = extraCaptures.Select(c => c.ProbabilityScore).ToList();
                var matchedRsi = matched.Select(m => m.captured.HtfRsi).ToList();
                var extraRsi = extraCaptures.Select(c => c.HtfRsi).ToList();

                _output.WriteLine("=== MATCHED vs EXTRA distribution ===");
                _output.WriteLine($"Probability score:");
                _output.WriteLine($"  Matched (n={matchedScores.Count}): min={matchedScores.Min()} max={matchedScores.Max()} " +
                    $"mean={matchedScores.Average():F1} <50:{matchedScores.Count(s => s < 50)} " +
                    $"50-69:{matchedScores.Count(s => s >= 50 && s < 70)} 70+:{matchedScores.Count(s => s >= 70)}");
                _output.WriteLine($"  Extras  (n={extraScores.Count}): min={extraScores.Min()} max={extraScores.Max()} " +
                    $"mean={extraScores.Average():F1} <50:{extraScores.Count(s => s < 50)} " +
                    $"50-69:{extraScores.Count(s => s >= 50 && s < 70)} 70+:{extraScores.Count(s => s >= 70)}");

                _output.WriteLine($"HTF RSI (absolute value from threshold):");
                var matchedLongs = matched.Where(m => m.captured.Direction == TradeDirection.Long).Select(m => m.captured.HtfRsi).ToList();
                var extraLongs = extraCaptures.Where(c => c.Direction == TradeDirection.Long).Select(c => c.HtfRsi).ToList();
                var matchedShorts = matched.Where(m => m.captured.Direction == TradeDirection.Short).Select(m => m.captured.HtfRsi).ToList();
                var extraShorts = extraCaptures.Where(c => c.Direction == TradeDirection.Short).Select(c => c.HtfRsi).ToList();

                if (matchedLongs.Count > 0 && extraLongs.Count > 0)
                    _output.WriteLine($"  Longs:  matched mean RSI={matchedLongs.Average():F1} | extras mean RSI={extraLongs.Average():F1}");
                if (matchedShorts.Count > 0 && extraShorts.Count > 0)
                    _output.WriteLine($"  Shorts: matched mean RSI={matchedShorts.Average():F1} | extras mean RSI={extraShorts.Average():F1}");

                var matchedVol = matched.Select(m => m.captured.VolExpansion).ToList();
                var extraVol = extraCaptures.Select(c => c.VolExpansion).ToList();
                _output.WriteLine($"Vol Expansion:");
                _output.WriteLine($"  Matched: min={matchedVol.Min():F3} max={matchedVol.Max():F3} mean={matchedVol.Average():F3}");
                _output.WriteLine($"  Extras:  min={extraVol.Min():F3} max={extraVol.Max():F3} mean={extraVol.Average():F3}");
                // Show distribution at key thresholds
                foreach (var thresh in new[] { 1.2, 1.25, 1.3, 1.4, 1.5 })
                {
                    var mAbove = matchedVol.Count(v => v >= thresh);
                    var eAbove = extraVol.Count(v => v >= thresh);
                    _output.WriteLine($"  >= {thresh:F2}: matched {mAbove}/{matchedVol.Count} ({100.0*mAbove/matchedVol.Count:F0}%) " +
                        $"| extras {eAbove}/{extraVol.Count} ({100.0*eAbove/extraVol.Count:F0}%)");
                }

                // RSI zone analysis: how many extras are in the "gap zone"
                // (60-65 for longs, 35-40 for shorts) vs the core zone?
                var extraLongGap = extraCaptures.Count(c => c.Direction == TradeDirection.Long && c.HtfRsi >= 60.0 && c.HtfRsi < 65.0);
                var extraShortGap = extraCaptures.Count(c => c.Direction == TradeDirection.Short && c.HtfRsi > 35.0 && c.HtfRsi <= 40.0);
                var extraLongCore = extraCaptures.Count(c => c.Direction == TradeDirection.Long && c.HtfRsi >= 65.0);
                var extraShortCore = extraCaptures.Count(c => c.Direction == TradeDirection.Short && c.HtfRsi <= 35.0);
                _output.WriteLine($"RSI zone breakdown (extras):");
                _output.WriteLine($"  Long gap (60-65): {extraLongGap} | Long core (65+): {extraLongCore}");
                _output.WriteLine($"  Short gap (35-40): {extraShortGap} | Short core (<35): {extraShortCore}");
                _output.WriteLine("");
            }

            // Sanity checks — algorithm should at least run
            candles.Count.Should().BeGreaterThan(0);
            targets.Count.Should().BeGreaterThan(0);
            captured.Count.Should().BeGreaterThan(0,
                "algorithm should produce at least some setups when replayed over the full dataset");

            // Regression floor — match rate should not drop below the current
            // baseline. Variability allowed: ±30min on entry time, ±0.3% on price.
            //
            // Progress history on the HTF RSI + Vol Expansion algorithm:
            //   29.6% — baseline (Skender Wilder RSI on 4H, SMA ATR, MinGap=8)
            //   45.2% — lowered HTF RSI thresholds from 65/35 to 60/40
            //   61.9% — switched HTF RSI from Wilder's to Cutler's (SMA of
            //           gains/losses) — matches target's RSI much closer for
            //           long entries in upward trends
            //   62.2% — tightened MinGap from 8 to 9 (target's observed
            //           minimum inter-trade gap is 135min = 9×15M)
            //   68.8% — partial-bar RSI + native 4H subscription + SMA ATR
            //
            // Known gating differences that still cost matches:
            //   (1) ATR method — MATCHED. Target uses SMA-based ATR(14);
            //       the algorithm's ComputeSmaAtr matches target ATR to the
            //       cent across all 436 trades.
            //   (2) HTF RSI — LOOK-AHEAD IN TARGET. The target backtest
            //       computes Cutler's RSI(14) on the COMPLETED 4H bar that
            //       contains the entry. This bar hasn't closed at entry time,
            //       so the target has look-ahead bias. Our partial-bar RSI
            //       (using the current 15M close as the in-progress 4H bar's
            //       close) is the correct live-trading approach. This causes
            //       17/436 target trades (~4%) to fail our RSI gate — trades
            //       the target shouldn't have taken without future data.
            //       Using the next-bar RSI gives 100% gate pass, confirming
            //       the look-ahead as the sole RSI divergence source.
            //   (3) Extras (algorithm fires ~824, target 436) — the algorithm
            //       over-fires during prolonged vol expansion waves. Target
            //       has a secondary filter not yet identified.
            var matchRate = (double)matched.Count / targets.Count;
            matchRate.Should().BeGreaterThanOrEqualTo(0.68,
                $"algorithm match rate regression floor " +
                $"(got {matched.Count}/{targets.Count} = {matchRate:P1}) — " +
                $"raise this threshold as the algorithm is tightened toward the backtest spec");
        }

        /// <summary>
        /// Condition-matching test: replays the exact candlestick history
        /// incrementally (one 15M bar at a time, same as the live algorithm)
        /// and at each target trade's entry candle verifies that:
        ///
        ///   (a) The 15M candle OHLC matches the target's Entry_Candle columns
        ///   (b) SMA-ATR(14) matches ATR_At_Entry, ATR(14) 20 bars ago matches
        ///       ATR_20_Ago, and vol expansion ratio matches Vol_Expansion
        ///   (c) Cutler's RSI on the aggregated 4H series is reported alongside
        ///       the target's HTF_RSI (exact match not expected — see notes)
        ///   (d) The algorithm's entry gates (direction + vol expansion + gap)
        ///       pass at the target's candle, explaining why each trade fires
        ///       or is gated out
        ///
        /// This is the definitive "same data, same pipeline" verification.
        /// Gate failures are categorised so we know exactly what to fix next.
        /// </summary>
        [Fact]
        public void ConditionMatch_IndicatorsAtTargetEntryCandles()
        {
            var candles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_15M.csv"));
            var targets = LoadTargetTrades(
                Path.Combine("HtfRsiVolExpansion", "TestData", "trade_list_target.csv"));

            // Build a lookup: CloseTime → index in candles list
            var closeTimeToIdx = new Dictionary<DateTime, int>();
            for (int i = 0; i < candles.Count; i++)
                closeTimeToIdx[candles[i].CloseTime] = i;

            // Pre-compute the full indicator series on the candle history,
            // using the SAME methods the algorithm uses internally.

            // 1. SMA-ATR(14) on every 15M bar
            var smaAtr14 = SmaAtr(candles, 14);

            // 2. Load native 4H data (same source the algorithm now subscribes to)
            var quotes4H = Load4HCsv(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_4H.csv"));

            // 3. Cutler's RSI(14) on the native 4H close series
            var closes4H = quotes4H.Select(q => q.Close).ToList();
            var cutlerRsi4H = CutlersRsi(closes4H, 14);

            // Map each 15M bar to the index of the most recent CLOSED 4H bar
            // at or before that time (binary-search friendly since both are sorted).
            int[] htfIdxAt15M = new int[candles.Count];
            {
                int j = 0;
                for (int i = 0; i < candles.Count; i++)
                {
                    while (j + 1 < quotes4H.Count && quotes4H[j + 1].Timestamp <= candles[i].CloseTime)
                        j++;
                    htfIdxAt15M[i] = (j < quotes4H.Count && quotes4H[j].Timestamp <= candles[i].CloseTime) ? j : -1;
                }
            }

            // Thresholds — must match what the algorithm uses
            const double rsiLongThreshold = 60.0;
            const double rsiShortThreshold = 40.0;
            const decimal volExpThreshold = 1.2m;
            const int minGap = 9; // candles between trades
            const int rsiPeriod = 14;

            // --- Walk through each target trade ---

            int candleMatch = 0, atrMatch = 0, atr20Match = 0, volExpMatch = 0;
            int rsiGatePass = 0, volGatePass = 0, allGatesPass = 0;
            int warmupFail = 0, gapFail = 0;

            // Track gap as candle-index distance from the previous target
            int lastTradeIdx = -10000; // sentinel so first trade always has plenty of gap

            _output.WriteLine($"Candles: {candles.Count} | 4H bars: {quotes4H.Count} | Targets: {targets.Count}");
            _output.WriteLine("");
            _output.WriteLine(
                "  # | Dir   | Entry Time       | OHLC  | ATR    | ATR20  | VolExp | RSI(my/tgt)  | Gap | Result");
            _output.WriteLine(
                "----+-------+------------------+-------+--------+--------+--------+--------------+-----+--------");

            foreach (var t in targets)
            {
                if (!closeTimeToIdx.TryGetValue(t.EntryTime, out int idx))
                {
                    _output.WriteLine($"  {t.Index,3} | ENTRY TIME {t.EntryTime:yyyy-MM-dd HH:mm} NOT FOUND in 15M data");
                    continue;
                }

                var c = candles[idx];

                // (a) Candle OHLC match
                bool ohlcOk = c.Open == t.EntryCandleO
                           && c.High == t.EntryCandleH
                           && c.Low == t.EntryCandleL
                           && c.Close == t.EntryCandleC;
                if (ohlcOk) candleMatch++;

                // (b) SMA-ATR at entry
                var myAtr = smaAtr14[idx];
                bool atrOk = myAtr.HasValue && Math.Abs(myAtr.Value - t.AtrAtEntry) < 0.02m;
                if (atrOk) atrMatch++;

                // SMA-ATR 20 bars ago
                decimal? myAtr20 = (idx >= 20) ? smaAtr14[idx - 20] : null;
                bool atr20Ok = myAtr20.HasValue && Math.Abs(myAtr20.Value - t.Atr20Ago) < 0.02m;
                if (atr20Ok) atr20Match++;

                // Vol expansion
                decimal? myVolExp = (myAtr.HasValue && myAtr20.HasValue && myAtr20.Value > 0)
                    ? myAtr.Value / myAtr20.Value : null;
                bool volExpOk = myVolExp.HasValue
                    && Math.Abs(myVolExp.Value - t.VolExpansion) < 0.005m;
                if (volExpOk) volExpMatch++;

                // (c) Cutler's 4H RSI with partial bar: use closed 4H bars
                // plus the current 15M close as the partial 4H bar close.
                // This matches what the live algorithm now computes.
                int hi = htfIdxAt15M[idx];
                double? myRsi = null;
                if (hi >= rsiPeriod)
                {
                    double gs = 0, ls = 0;
                    // period-1 closed changes
                    for (int k = hi - rsiPeriod + 2; k <= hi; k++)
                    {
                        var diff = (double)(closes4H[k] - closes4H[k - 1]);
                        if (diff > 0) gs += diff; else ls += -diff;
                    }
                    // partial change: last closed bar → entry candle close
                    var partialDiff = (double)(c.Close - closes4H[hi]);
                    if (partialDiff > 0) gs += partialDiff; else ls += -partialDiff;

                    var ag = gs / rsiPeriod;
                    var al = ls / rsiPeriod;
                    myRsi = al == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + ag / al);
                }

                // (d) Gate checks
                bool warmupOk = hi >= rsiPeriod && myAtr.HasValue && myAtr20.HasValue;
                if (!warmupOk) warmupFail++;

                bool volGate = myVolExp.HasValue && myVolExp.Value >= volExpThreshold;
                if (volGate) volGatePass++;

                bool rsiGate = false;
                if (myRsi.HasValue)
                {
                    if (t.Direction == TradeDirection.Long && myRsi.Value > rsiLongThreshold) rsiGate = true;
                    if (t.Direction == TradeDirection.Short && myRsi.Value < rsiShortThreshold) rsiGate = true;
                }
                if (rsiGate) rsiGatePass++;

                int gap = idx - lastTradeIdx;
                bool gapOk = gap >= minGap;
                if (!gapOk) gapFail++;

                bool allOk = warmupOk && volGate && rsiGate && gapOk;
                if (allOk) allGatesPass++;

                // Record this trade's index for gap calculation
                lastTradeIdx = idx;

                // Formatted output — build failure reason tag
                string failReason = "";
                if (!allOk)
                {
                    var reasons = new List<string>();
                    if (!warmupOk) reasons.Add("warmup");
                    if (!volGate) reasons.Add("vol");
                    if (!rsiGate) reasons.Add("rsi");
                    if (!gapOk) reasons.Add("gap");
                    failReason = " [" + string.Join("+", reasons) + "]";
                }

                string atrStr = myAtr.HasValue
                    ? (atrOk ? $"{myAtr.Value,7:F2}✓" : $"{myAtr.Value,7:F2}✗")
                    : "   null ";
                string atr20Str = myAtr20.HasValue
                    ? (atr20Ok ? $"{myAtr20.Value,7:F2}✓" : $"{myAtr20.Value,7:F2}✗")
                    : "   null ";
                string volStr = myVolExp.HasValue
                    ? (volGate ? $"{myVolExp.Value,5:F3}✓" : $"{myVolExp.Value,5:F3}✗")
                    : "  null ";
                string rsiStr = myRsi.HasValue
                    ? $"{myRsi.Value,5:F1}/{t.HtfRsi,5:F1}" + (rsiGate ? "✓" : "✗")
                    : $" null/{t.HtfRsi,5:F1}✗";
                string gapStr = gapOk ? $"{gap,3}✓" : $"{gap,3}✗";
                string resultStr = allOk ? "PASS" : $"FAIL{failReason}";

                // Print first 30, last 5, and all FAILs in first 100
                if (t.Index <= 30 || t.Index > targets.Count - 5 || (!allOk && t.Index <= 100))
                {
                    _output.WriteLine(
                        $"  {t.Index,3} | {t.Direction,-5} | {t.EntryTime:yyyy-MM-dd HH:mm} | " +
                        $"{(ohlcOk ? "OK   " : "MISS ")} | " +
                        $"{atrStr} | {atr20Str} | {volStr} | {rsiStr} | {gapStr} | {resultStr}");
                }
            }

            _output.WriteLine("");
            _output.WriteLine("=== CONDITION MATCH SUMMARY ===");
            _output.WriteLine($"  Candle OHLC exact match:    {candleMatch,4}/{targets.Count} ({100.0 * candleMatch / targets.Count:F1}%)");
            _output.WriteLine($"  SMA-ATR(14) match (<0.02):  {atrMatch,4}/{targets.Count} ({100.0 * atrMatch / targets.Count:F1}%)");
            _output.WriteLine($"  ATR-20-ago match (<0.02):   {atr20Match,4}/{targets.Count} ({100.0 * atr20Match / targets.Count:F1}%)");
            _output.WriteLine($"  Vol expansion match (<0.5%):{volExpMatch,4}/{targets.Count} ({100.0 * volExpMatch / targets.Count:F1}%)");
            _output.WriteLine("");
            _output.WriteLine("=== GATE ANALYSIS (why trades fire or don't) ===");
            _output.WriteLine($"  Warmup ready:               {targets.Count - warmupFail,4}/{targets.Count}");
            _output.WriteLine($"  Vol expansion >= 1.2:       {volGatePass,4}/{targets.Count} ({100.0 * volGatePass / targets.Count:F1}%)");
            _output.WriteLine($"  RSI direction gate:         {rsiGatePass,4}/{targets.Count} ({100.0 * rsiGatePass / targets.Count:F1}%)");
            _output.WriteLine($"  Gap >= {minGap} from prev target: {targets.Count - gapFail,4}/{targets.Count}");
            _output.WriteLine($"  ALL gates pass:             {allGatesPass,4}/{targets.Count} ({100.0 * allGatesPass / targets.Count:F1}%)");

            // Assertions
            // Candle data must be an exact match — same CSV, same candles
            candleMatch.Should().Be(targets.Count,
                "every target trade's entry candle OHLC should exactly match the 15M CSV row at that time");

            // ATR must match to the cent — we use the same SMA method
            atrMatch.Should().Be(targets.Count,
                "SMA-ATR(14) should match target's ATR_At_Entry for all trades (same computation method, verified)");

            atr20Match.Should().Be(targets.Count,
                "SMA-ATR(14) 20 bars prior should match target's ATR_20_Ago for all trades");

            volExpMatch.Should().Be(targets.Count,
                "vol expansion ratio should match target's Vol_Expansion for all trades");

            // RSI gate: 17/436 target trades (~4%) fail because the target has
            // look-ahead bias — it uses the completed 4H bar's RSI, which isn't
            // available at entry time. Our partial-bar RSI is the correct
            // live-trading approach. 96%+ pass rate is the realistic ceiling.
            ((double)rsiGatePass / targets.Count).Should().BeGreaterThanOrEqualTo(0.95,
                $"RSI direction gate should pass for ≥95% of target trades " +
                $"(got {rsiGatePass}/{targets.Count} — remaining ~4% are target " +
                $"look-ahead trades that wouldn't fire in live trading)");
        }

        /// <summary>
        /// Diagnostic: at each target trade's 15M entry time, recompute the
        /// same indicators the algorithm uses (Skender ATR(14) on 15M,
        /// Skender RSI(14) on 4H aggregated from 15M) and compare to what the
        /// target CSV itself claims. This isolates whether the algorithm's
        /// miss rate is due to indicator-library differences (Skender vs
        /// whatever produced the target) or pure gating-threshold differences.
        /// </summary>
        [Fact]
        public void Diagnostic_CompareSkenderIndicatorsAgainstTargetValues()
        {
            var candles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_15M.csv"));
            var targets = LoadTargetTrades(
                Path.Combine("HtfRsiVolExpansion", "TestData", "trade_list_target.csv"));

            // Build 15M Skender quotes
            var quotes15M = candles.Select(c => (IQuote)new Quote
            {
                Timestamp = c.CloseTime,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            }).ToList();

            // 15M ATR(14) — algorithm uses this for vol expansion
            var atr15M = quotes15M.ToAtr(14).ToList();

            // Aggregate 15M → 4H using the production aggregator
            var aggregator = new FourHourCandleAggregator();
            var quotes4HRaw = candles.Select(c => new Quote
            {
                Timestamp = c.CloseTime,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            });
            var quotes4H = aggregator.AggregateHistoric(quotes4HRaw).ToList();

            // 4H RSI(14) — algorithm uses this for direction
            var rsi4H = quotes4H.Cast<IQuote>().ToList().ToRsi(14).ToList();

            _output.WriteLine($"15M candles:   {quotes15M.Count}");
            _output.WriteLine($"4H candles:    {quotes4H.Count}");
            _output.WriteLine($"ATR results:   {atr15M.Count}");
            _output.WriteLine($"RSI results:   {rsi4H.Count}");
            _output.WriteLine("");

            // Build lookups by timestamp
            var closeToIndex15M = new Dictionary<DateTime, int>();
            for (int i = 0; i < candles.Count; i++)
                closeToIndex15M[candles[i].CloseTime] = i;

            // For each target trade, locate ATR at entry, ATR 20 candles ago,
            // and RSI of the most recent CLOSED 4H bar at/before the entry.
            int sampleCount = Math.Min(20, targets.Count);
            int atrCloseMatches = 0;
            int rsiCloseMatches = 0;
            int gateWouldPassCount = 0;

            _output.WriteLine("=== First 20 target trades: Skender indicators vs target ===");
            _output.WriteLine("");
            _output.WriteLine("Trade | Target: ATR / ATR-20 / VolExp / HTFRsi  | Skender: ATR / ATR-20 / VolExp / HTFRsi  | Gate");
            _output.WriteLine("------+----------------------------------------+-----------------------------------------+------");

            for (int k = 0; k < sampleCount; k++)
            {
                var t = targets[k];
                if (!closeToIndex15M.TryGetValue(t.EntryTime, out int idx))
                {
                    _output.WriteLine($"  #{t.Index,3}  TIMESTAMP NOT FOUND in 15M data: {t.EntryTime}");
                    continue;
                }

                var skAtr = atr15M[idx].Atr;
                decimal? skAtrPast = null;
                if (idx - 20 >= 0)
                    skAtrPast = (decimal?)atr15M[idx - 20].Atr;

                decimal? skVolExp = null;
                if (skAtr.HasValue && skAtrPast.HasValue && skAtrPast.Value > 0)
                    skVolExp = (decimal)skAtr.Value / skAtrPast.Value;

                // Find most recent 4H bar whose Timestamp (close time) is <= entry time
                int htfIdx = -1;
                for (int j = quotes4H.Count - 1; j >= 0; j--)
                {
                    if (quotes4H[j].Timestamp <= t.EntryTime)
                    {
                        htfIdx = j;
                        break;
                    }
                }
                double? skRsi = htfIdx >= 0 && htfIdx < rsi4H.Count ? rsi4H[htfIdx].Rsi : null;

                // "close" tolerance: ATR within 2%, RSI within 1.0 absolute
                bool atrClose = skAtr.HasValue && Math.Abs((decimal)skAtr.Value - t.AtrAtEntry) / t.AtrAtEntry < 0.02m;
                bool rsiClose = skRsi.HasValue && Math.Abs(skRsi.Value - t.HtfRsi) < 1.0;

                if (atrClose) atrCloseMatches++;
                if (rsiClose) rsiCloseMatches++;

                // Would our gate pass? (RSI > 65 long / < 35 short) AND (VolExp >= 1.2)
                bool gateLong = skRsi.HasValue && skRsi.Value > 65.0 && skVolExp.HasValue && skVolExp.Value >= 1.2m;
                bool gateShort = skRsi.HasValue && skRsi.Value < 35.0 && skVolExp.HasValue && skVolExp.Value >= 1.2m;
                bool gatePass = (t.Direction == TradeDirection.Long && gateLong)
                             || (t.Direction == TradeDirection.Short && gateShort);
                if (gatePass) gateWouldPassCount++;

                var gateStr = gatePass ? "PASS" : "FAIL";
                var rsiStr = skRsi.HasValue ? skRsi.Value.ToString("F2") : "null";
                var atrStr = skAtr.HasValue ? skAtr.Value.ToString("F2") : "null";
                var atrPastStr = skAtrPast.HasValue ? skAtrPast.Value.ToString("F2") : "null";
                var volStr = skVolExp.HasValue ? skVolExp.Value.ToString("F4") : "null";

                _output.WriteLine(
                    $"  #{t.Index,3} | tgt ATR:{t.AtrAtEntry,8:F2}        VolExp: ???       HTFRsi:{t.HtfRsi,5:F1} " +
                    $"| sk ATR:{atrStr,8} ATR-20:{atrPastStr,8} VolExp:{volStr,7} HTFRsi:{rsiStr,5} | {gateStr}");
            }

            _output.WriteLine("");
            _output.WriteLine($"Skender ATR close to target: {atrCloseMatches}/{sampleCount}");
            _output.WriteLine($"Skender RSI close to target: {rsiCloseMatches}/{sampleCount}");
            _output.WriteLine($"Skender gate would pass:     {gateWouldPassCount}/{sampleCount}");

            // Full-sample gate-pass rate across ALL targets
            int fullGatePass = 0;
            int fullAtrClose = 0;
            int fullRsiClose = 0;
            foreach (var t in targets)
            {
                if (!closeToIndex15M.TryGetValue(t.EntryTime, out int idx)) continue;

                var skAtr = atr15M[idx].Atr;
                decimal? skAtrPast = idx - 20 >= 0 ? (decimal?)atr15M[idx - 20].Atr : null;
                decimal? skVolExp = (skAtr.HasValue && skAtrPast.HasValue && skAtrPast.Value > 0)
                    ? (decimal)skAtr.Value / skAtrPast.Value : (decimal?)null;

                int htfIdx = -1;
                for (int j = quotes4H.Count - 1; j >= 0; j--)
                {
                    if (quotes4H[j].Timestamp <= t.EntryTime) { htfIdx = j; break; }
                }
                double? skRsi = htfIdx >= 0 && htfIdx < rsi4H.Count ? rsi4H[htfIdx].Rsi : null;

                if (skAtr.HasValue && Math.Abs((decimal)skAtr.Value - t.AtrAtEntry) / t.AtrAtEntry < 0.02m)
                    fullAtrClose++;
                if (skRsi.HasValue && Math.Abs(skRsi.Value - t.HtfRsi) < 1.0)
                    fullRsiClose++;

                bool gateLong = skRsi.HasValue && skRsi.Value > 65.0 && skVolExp.HasValue && skVolExp.Value >= 1.2m;
                bool gateShort = skRsi.HasValue && skRsi.Value < 35.0 && skVolExp.HasValue && skVolExp.Value >= 1.2m;
                if ((t.Direction == TradeDirection.Long && gateLong) ||
                    (t.Direction == TradeDirection.Short && gateShort))
                    fullGatePass++;
            }

            _output.WriteLine("");
            _output.WriteLine($"=== FULL SAMPLE (all {targets.Count} targets) ===");
            _output.WriteLine($"Skender ATR close to target (within 2%): {fullAtrClose}/{targets.Count} ({100.0 * fullAtrClose / targets.Count:F1}%)");
            _output.WriteLine($"Skender RSI close to target (within 1.0): {fullRsiClose}/{targets.Count} ({100.0 * fullRsiClose / targets.Count:F1}%)");
            _output.WriteLine($"Skender gate would pass (RSI+VolExp):     {fullGatePass}/{targets.Count} ({100.0 * fullGatePass / targets.Count:F1}%)");

            targets.Count.Should().BeGreaterThan(0);
        }

        #region Indicator alternatives (Cutler's / SMA-based)

        /// <summary>
        /// Cutler's RSI — uses simple moving average of gains/losses instead of
        /// Wilder's exponential smoothing. Produces noticeably different values
        /// especially right after warmup. Returns one entry per input close,
        /// with null for the first `period` entries.
        /// </summary>
        private static List<double?> CutlersRsi(List<decimal> closes, int period)
        {
            var result = new List<double?>(new double?[closes.Count]);
            if (closes.Count <= period) return result;

            var gains = new double[closes.Count];
            var losses = new double[closes.Count];
            for (int i = 1; i < closes.Count; i++)
            {
                var diff = (double)(closes[i] - closes[i - 1]);
                if (diff > 0) gains[i] = diff;
                else losses[i] = -diff;
            }

            for (int i = period; i < closes.Count; i++)
            {
                double avgGain = 0, avgLoss = 0;
                for (int k = i - period + 1; k <= i; k++)
                {
                    avgGain += gains[k];
                    avgLoss += losses[k];
                }
                avgGain /= period;
                avgLoss /= period;

                if (avgLoss == 0)
                    result[i] = 100.0;
                else
                {
                    var rs = avgGain / avgLoss;
                    result[i] = 100.0 - (100.0 / (1.0 + rs));
                }
            }
            return result;
        }

        /// <summary>
        /// SMA-based ATR — each bar's ATR is the simple mean of the last
        /// `period` True Range values. Different from Wilder's smoothed ATR.
        /// Returns one entry per candle, null for the first `period` entries.
        /// </summary>
        private static List<decimal?> SmaAtr(List<Candle15M> candles, int period)
        {
            var result = new List<decimal?>(new decimal?[candles.Count]);
            if (candles.Count < period + 1) return result;

            var tr = new decimal[candles.Count];
            for (int i = 1; i < candles.Count; i++)
            {
                var h = candles[i].High;
                var l = candles[i].Low;
                var pc = candles[i - 1].Close;
                var t1 = h - l;
                var t2 = Math.Abs(h - pc);
                var t3 = Math.Abs(l - pc);
                tr[i] = Math.Max(t1, Math.Max(t2, t3));
            }

            for (int i = period; i < candles.Count; i++)
            {
                decimal sum = 0;
                for (int k = i - period + 1; k <= i; k++) sum += tr[k];
                result[i] = sum / period;
            }
            return result;
        }

        /// <summary>
        /// Same SMA-based ATR but working on 4H Quote objects — used for
        /// diagnostic symmetry. Not currently needed because ATR is on 15M.
        /// </summary>
        private static List<decimal?> SmaAtr4H(List<Quote> quotes, int period)
        {
            var result = new List<decimal?>(new decimal?[quotes.Count]);
            if (quotes.Count < period + 1) return result;

            var tr = new decimal[quotes.Count];
            for (int i = 1; i < quotes.Count; i++)
            {
                var h = quotes[i].High;
                var l = quotes[i].Low;
                var pc = quotes[i - 1].Close;
                tr[i] = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
            }

            for (int i = period; i < quotes.Count; i++)
            {
                decimal sum = 0;
                for (int k = i - period + 1; k <= i; k++) sum += tr[k];
                result[i] = sum / period;
            }
            return result;
        }

        /// <summary>
        /// EMA-smoothed RSI — uses alpha=2/(period+1) instead of Wilder's
        /// alpha=1/period. Faster-adapting than Wilder's. Some Python
        /// backtests use this via `series.ewm(span=period).mean()`.
        /// </summary>
        private static List<double?> EwmRsi(List<decimal> closes, int period)
        {
            var result = new List<double?>(new double?[closes.Count]);
            if (closes.Count <= period) return result;

            double alpha = 2.0 / (period + 1);
            double emaGain = 0, emaLoss = 0;

            // Seed with SMA of first `period` diffs
            for (int i = 1; i <= period; i++)
            {
                var diff = (double)(closes[i] - closes[i - 1]);
                if (diff > 0) emaGain += diff;
                else emaLoss += -diff;
            }
            emaGain /= period;
            emaLoss /= period;
            result[period] = emaLoss == 0 ? 100.0 : 100.0 - (100.0 / (1.0 + emaGain / emaLoss));

            for (int i = period + 1; i < closes.Count; i++)
            {
                var diff = (double)(closes[i] - closes[i - 1]);
                var g = diff > 0 ? diff : 0.0;
                var l = diff < 0 ? -diff : 0.0;
                emaGain = alpha * g + (1 - alpha) * emaGain;
                emaLoss = alpha * l + (1 - alpha) * emaLoss;
                result[i] = emaLoss == 0 ? 100.0 : 100.0 - (100.0 / (1.0 + emaGain / emaLoss));
            }
            return result;
        }

        #endregion

        /// <summary>
        /// Diagnostic #2: compare Cutler's RSI (SMA-based) and SMA-ATR against
        /// the target values. If these match much better than Skender's Wilder
        /// smoothing, then the algorithm should switch indicator methods.
        /// </summary>
        [Fact]
        public void Diagnostic_CutlersRsiAndSmaAtr_vs_TargetValues()
        {
            var candles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_15M.csv"));
            var targets = LoadTargetTrades(
                Path.Combine("HtfRsiVolExpansion", "TestData", "trade_list_target.csv"));

            // SMA-ATR on 15M
            var smaAtr15M = SmaAtr(candles, 14);

            // Aggregate to 4H using the production aggregator
            var aggregator = new FourHourCandleAggregator();
            var quotes4HRaw = candles.Select(c => new Quote
            {
                Timestamp = c.CloseTime,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            });
            var quotes4H = aggregator.AggregateHistoric(quotes4HRaw).ToList();

            // Cutler's RSI on 4H closes
            var closes4H = quotes4H.Select(q => q.Close).ToList();
            var cutlersRsi4H = CutlersRsi(closes4H, 14);

            var closeToIndex15M = new Dictionary<DateTime, int>();
            for (int i = 0; i < candles.Count; i++)
                closeToIndex15M[candles[i].CloseTime] = i;

            int sampleCount = Math.Min(20, targets.Count);
            _output.WriteLine("=== First 20 target trades: Cutler's RSI + SMA-ATR vs target ===");
            _output.WriteLine("");
            _output.WriteLine("Trade | Target: ATR / HTFRsi    | Cutler/SMA: ATR / HTFRsi   | ATR Δ%   RSI Δ");
            _output.WriteLine("------+-------------------------+-----------------------------+----------------");

            for (int k = 0; k < sampleCount; k++)
            {
                var t = targets[k];
                if (!closeToIndex15M.TryGetValue(t.EntryTime, out int idx)) continue;

                var smaAtr = smaAtr15M[idx];

                // Find most recent 4H bar <= entry time (same convention as algorithm)
                int htfIdx = -1;
                for (int j = quotes4H.Count - 1; j >= 0; j--)
                {
                    if (quotes4H[j].Timestamp <= t.EntryTime) { htfIdx = j; break; }
                }
                double? cutRsi = htfIdx >= 0 ? cutlersRsi4H[htfIdx] : null;

                // Also try "previous" 4H bar — some backtests use the PRIOR
                // closed bar for direction rather than including the in-progress one.
                double? cutRsiPrev = htfIdx - 1 >= 0 ? cutlersRsi4H[htfIdx - 1] : null;

                var atrDeltaPct = smaAtr.HasValue ? ((smaAtr.Value - t.AtrAtEntry) / t.AtrAtEntry * 100m) : 0m;
                var rsiDelta = cutRsi.HasValue ? (cutRsi.Value - t.HtfRsi) : 0;

                _output.WriteLine(
                    $"  #{t.Index,3} | tgt ATR:{t.AtrAtEntry,8:F2}  RSI:{t.HtfRsi,5:F1} " +
                    $"| sma ATR:{(smaAtr?.ToString("F2") ?? "null"),8}  cut RSI:{(cutRsi?.ToString("F2") ?? "null"),6} (prev {cutRsiPrev?.ToString("F2") ?? "null"}) " +
                    $"| {atrDeltaPct:+0.0;-0.0}%  {rsiDelta:+0.0;-0.0}");
            }

            // Full-sample counts for both "current" and "previous" 4H bar
            int fullAtrClose = 0;
            int fullRsiCloseCurrent = 0;
            int fullRsiClosePrev = 0;
            int fullGateCurrent = 0;
            int fullGatePrev = 0;

            foreach (var t in targets)
            {
                if (!closeToIndex15M.TryGetValue(t.EntryTime, out int idx)) continue;

                var smaAtr = smaAtr15M[idx];
                decimal? smaAtrPast = idx - 20 >= 0 ? smaAtr15M[idx - 20] : null;
                decimal? volExp = (smaAtr.HasValue && smaAtrPast.HasValue && smaAtrPast.Value > 0)
                    ? smaAtr.Value / smaAtrPast.Value : (decimal?)null;

                int htfIdx = -1;
                for (int j = quotes4H.Count - 1; j >= 0; j--)
                {
                    if (quotes4H[j].Timestamp <= t.EntryTime) { htfIdx = j; break; }
                }
                double? cutRsi = htfIdx >= 0 ? cutlersRsi4H[htfIdx] : null;
                double? cutRsiPrev = htfIdx - 1 >= 0 ? cutlersRsi4H[htfIdx - 1] : null;

                if (smaAtr.HasValue && Math.Abs(smaAtr.Value - t.AtrAtEntry) / t.AtrAtEntry < 0.02m)
                    fullAtrClose++;
                if (cutRsi.HasValue && Math.Abs(cutRsi.Value - t.HtfRsi) < 1.0)
                    fullRsiCloseCurrent++;
                if (cutRsiPrev.HasValue && Math.Abs(cutRsiPrev.Value - t.HtfRsi) < 1.0)
                    fullRsiClosePrev++;

                // Gate: direction + vol expansion
                bool dirLongCurrent = cutRsi.HasValue && cutRsi.Value > 65.0;
                bool dirShortCurrent = cutRsi.HasValue && cutRsi.Value < 35.0;
                bool dirLongPrev = cutRsiPrev.HasValue && cutRsiPrev.Value > 65.0;
                bool dirShortPrev = cutRsiPrev.HasValue && cutRsiPrev.Value < 35.0;
                bool volOk = volExp.HasValue && volExp.Value >= 1.2m;

                if (volOk && ((t.Direction == TradeDirection.Long && dirLongCurrent) ||
                               (t.Direction == TradeDirection.Short && dirShortCurrent)))
                    fullGateCurrent++;
                if (volOk && ((t.Direction == TradeDirection.Long && dirLongPrev) ||
                               (t.Direction == TradeDirection.Short && dirShortPrev)))
                    fullGatePrev++;
            }

            _output.WriteLine("");
            _output.WriteLine($"=== FULL SAMPLE (all {targets.Count} targets) ===");
            _output.WriteLine($"SMA-ATR close to target (within 2%):             {fullAtrClose}/{targets.Count} ({100.0 * fullAtrClose / targets.Count:F1}%)");
            _output.WriteLine($"Cutler's RSI (current bar) close (within 1.0):   {fullRsiCloseCurrent}/{targets.Count} ({100.0 * fullRsiCloseCurrent / targets.Count:F1}%)");
            _output.WriteLine($"Cutler's RSI (PRIOR bar) close (within 1.0):     {fullRsiClosePrev}/{targets.Count} ({100.0 * fullRsiClosePrev / targets.Count:F1}%)");
            _output.WriteLine($"Gate would pass (current-bar RSI + VolExp≥1.2):  {fullGateCurrent}/{targets.Count} ({100.0 * fullGateCurrent / targets.Count:F1}%)");
            _output.WriteLine($"Gate would pass (prior-bar RSI + VolExp≥1.2):    {fullGatePrev}/{targets.Count} ({100.0 * fullGatePrev / targets.Count:F1}%)");

            targets.Count.Should().BeGreaterThan(0);
        }

        /// <summary>
        /// Diagnostic #3: tries several alternative RSI hypotheses against
        /// the target's HTF_RSI column, to identify how the backtest actually
        /// computes its 4H RSI. Candidates:
        ///   (A) Wilder RSI on clock-aligned 4H aggregates (Skender baseline)
        ///   (B) Cutler's RSI on clock-aligned 4H aggregates
        ///   (C) EWM RSI (α=2/(n+1)) on clock-aligned 4H aggregates
        ///   (D) Cutler's RSI on ROLLING 4H (last 16 15M candles at each point)
        /// Prints side-by-side for the first 10 trades and full-sample match counts.
        /// </summary>
        [Fact]
        public void Diagnostic_HunForBestRsiVariant()
        {
            var candles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_15M.csv"));
            var targets = LoadTargetTrades(
                Path.Combine("HtfRsiVolExpansion", "TestData", "trade_list_target.csv"));

            var aggregator = new FourHourCandleAggregator();
            var quotes4HRaw = candles.Select(c => new Quote
            {
                Timestamp = c.CloseTime,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            });
            var quotes4H = aggregator.AggregateHistoric(quotes4HRaw).ToList();
            var closes4H = quotes4H.Select(q => q.Close).ToList();

            // (A) Wilder on clock-aligned 4H
            var wilder4H = quotes4H.Cast<IQuote>().ToList().ToRsi(14).Select(r => r.Rsi).ToList();
            // (B) Cutler's on clock-aligned 4H
            var cutler4H = CutlersRsi(closes4H, 14);
            // (C) EWM on clock-aligned 4H
            var ewm4H = EwmRsi(closes4H, 14);

            var closeToIndex15M = new Dictionary<DateTime, int>();
            for (int i = 0; i < candles.Count; i++)
                closeToIndex15M[candles[i].CloseTime] = i;

            // (D) Rolling 4H: at each 15M index, synthesize 4H bars by taking
            // every 16 candles backwards, compute RSI(14). We only do this at
            // target trade entry rows to avoid O(n²) over the full dataset.
            decimal? RollingRsiAtIndex(int endIdx, int period)
            {
                const int barsNeeded = 16; // 4H = 16 × 15M
                const int rsiBars = 14;
                int needed = (rsiBars + 1) * barsNeeded;
                if (endIdx + 1 < needed) return null;

                var bars = new List<decimal>();
                for (int k = 0; k <= rsiBars; k++)
                {
                    int end = endIdx - k * barsNeeded;
                    // "Close" of this synthetic 4H bar = 15M close at `end`
                    bars.Add(candles[end].Close);
                }
                bars.Reverse(); // oldest first

                // Cutler's RSI on `bars`
                double gain = 0, loss = 0;
                for (int i = 1; i <= rsiBars; i++)
                {
                    var d = (double)(bars[i] - bars[i - 1]);
                    if (d > 0) gain += d;
                    else loss += -d;
                }
                gain /= rsiBars;
                loss /= rsiBars;
                if (loss == 0) return 100m;
                var rs = gain / loss;
                return (decimal)(100.0 - 100.0 / (1.0 + rs));
            }

            // Comparison
            int sample = Math.Min(15, targets.Count);
            _output.WriteLine("=== First 15 target trades: RSI hypotheses ===");
            _output.WriteLine("");
            _output.WriteLine("Trade |  tgt  |  Wilder  | Cutler   |  EWM     | Rolling");
            _output.WriteLine("------+-------+----------+----------+----------+---------");

            for (int k = 0; k < sample; k++)
            {
                var t = targets[k];
                if (!closeToIndex15M.TryGetValue(t.EntryTime, out int idx)) continue;

                int htfIdx = -1;
                for (int j = quotes4H.Count - 1; j >= 0; j--)
                    if (quotes4H[j].Timestamp <= t.EntryTime) { htfIdx = j; break; }

                var wilder = htfIdx >= 0 ? wilder4H[htfIdx] : null;
                var cutler = htfIdx >= 0 ? cutler4H[htfIdx] : null;
                var ewm    = htfIdx >= 0 ? ewm4H[htfIdx] : null;
                var rolling = RollingRsiAtIndex(idx, 14);

                _output.WriteLine(
                    $"  #{t.Index,3} | {t.HtfRsi,5:F1} | " +
                    $"{(wilder?.ToString("F2") ?? "null"),8} | " +
                    $"{(cutler?.ToString("F2") ?? "null"),8} | " +
                    $"{(ewm?.ToString("F2") ?? "null"),8} | " +
                    $"{(rolling?.ToString("F2") ?? "null"),7}");
            }

            // Full sample
            int wilderClose = 0, cutlerClose = 0, ewmClose = 0, rollingClose = 0;
            int rsiSampleCount = 0;
            foreach (var t in targets)
            {
                if (!closeToIndex15M.TryGetValue(t.EntryTime, out int idx)) continue;
                rsiSampleCount++;

                int htfIdx = -1;
                for (int j = quotes4H.Count - 1; j >= 0; j--)
                    if (quotes4H[j].Timestamp <= t.EntryTime) { htfIdx = j; break; }

                if (htfIdx >= 0)
                {
                    if (wilder4H[htfIdx].HasValue && Math.Abs(wilder4H[htfIdx].Value - t.HtfRsi) < 1.0) wilderClose++;
                    if (cutler4H[htfIdx].HasValue && Math.Abs(cutler4H[htfIdx].Value - t.HtfRsi) < 1.0) cutlerClose++;
                    if (ewm4H[htfIdx].HasValue && Math.Abs(ewm4H[htfIdx].Value - t.HtfRsi) < 1.0) ewmClose++;
                }
                var rolling = RollingRsiAtIndex(idx, 14);
                if (rolling.HasValue && Math.Abs((double)rolling.Value - t.HtfRsi) < 1.0) rollingClose++;
            }

            _output.WriteLine("");
            _output.WriteLine($"=== FULL SAMPLE (n={rsiSampleCount}) ===");
            _output.WriteLine($"Wilder  (4H aligned): {wilderClose} matches  ({100.0 * wilderClose / rsiSampleCount:F1}%)");
            _output.WriteLine($"Cutler  (4H aligned): {cutlerClose} matches  ({100.0 * cutlerClose / rsiSampleCount:F1}%)");
            _output.WriteLine($"EWM     (4H aligned): {ewmClose} matches  ({100.0 * ewmClose / rsiSampleCount:F1}%)");
            _output.WriteLine($"Rolling (last 16×15M): {rollingClose} matches  ({100.0 * rollingClose / rsiSampleCount:F1}%)");

            targets.Count.Should().BeGreaterThan(0);
        }

        /// <summary>
        /// Loads the dedicated BTCUSDT_4H.csv — same column layout as the 15M
        /// file: CloseTime, OpenTime, O, H, L, C, Volume.
        /// </summary>
        private static List<Quote> Load4HCsv(string relativePath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException($"4H data not found: {path}");

            var quotes = new List<Quote>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Trim().TrimStart('\uFEFF').Split(',');
                if (parts.Length < 7) continue;

                if (!DateTime.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var closeTime))
                    continue;

                quotes.Add(new Quote
                {
                    Timestamp = closeTime,
                    Open = decimal.Parse(parts[2].Trim(), CultureInfo.InvariantCulture),
                    High = decimal.Parse(parts[3].Trim(), CultureInfo.InvariantCulture),
                    Low = decimal.Parse(parts[4].Trim(), CultureInfo.InvariantCulture),
                    Close = decimal.Parse(parts[5].Trim(), CultureInfo.InvariantCulture),
                    Volume = decimal.Parse(parts[6].Trim(), CultureInfo.InvariantCulture)
                });
            }
            return quotes.OrderBy(q => q.Timestamp).ToList();
        }

        /// <summary>
        /// Diagnostic #5: load the native 4H CSV (not aggregated from 15M) and
        /// check RSI variants against target HTF_RSI. The 4H CSV starts one
        /// bar earlier than the 15M aggregation (covers 2025-06-30 20:00 →
        /// 2025-07-01 00:00), so indexing is shifted by 1 vs the aggregated view.
        /// </summary>
        [Fact]
        public void Diagnostic_Native4HCsv_vs_TargetRsi()
        {
            var candles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_15M.csv"));
            var targets = LoadTargetTrades(
                Path.Combine("HtfRsiVolExpansion", "TestData", "trade_list_target.csv"));
            var quotes4H = Load4HCsv(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_4H.csv"));

            _output.WriteLine($"Native 4H: {quotes4H.Count} bars ({quotes4H.First().Timestamp} → {quotes4H.Last().Timestamp})");

            var closes4H = quotes4H.Select(q => q.Close).ToList();
            var cutler4H = CutlersRsi(closes4H, 14);
            var wilder4H = quotes4H.Cast<IQuote>().ToList().ToRsi(14).Select(r => r.Rsi).ToList();
            var ewm4H = EwmRsi(closes4H, 14);

            _output.WriteLine("");
            _output.WriteLine("First few 4H closes and RSI values:");
            for (int i = 10; i < 20; i++)
            {
                _output.WriteLine($"  bar {i,2} | {quotes4H[i].Timestamp:yyyy-MM-dd HH:mm} | " +
                    $"close {quotes4H[i].Close,10:F2} | Cutler {cutler4H[i]?.ToString("F2") ?? "null"} | " +
                    $"Wilder {wilder4H[i]?.ToString("F2") ?? "null"} | EWM {ewm4H[i]?.ToString("F2") ?? "null"}");
            }

            // For each target, look up the 4H bar matching HTF_Bar_DateTime
            // (target column stores the OPEN time of the bar containing the trade,
            // so we need the bar with Timestamp == that + 4h = the closing bar).
            // Actually reviewing the target CSV: HTF_Bar_DateTime for trade 1 is
            // 2025-07-03 08:00 which matches the 4H CSV row whose CloseTime is 08:00.
            // So HTF_Bar_DateTime IS the close time of a completed bar.
            var tsToIdx4H = new Dictionary<DateTime, int>();
            for (int i = 0; i < quotes4H.Count; i++)
                tsToIdx4H[quotes4H[i].Timestamp] = i;

            int sample = Math.Min(15, targets.Count);
            _output.WriteLine("");
            _output.WriteLine("Trade |  tgt  |  Cutler  |  Wilder  |   EWM    | 4H-idx | 4H-close");
            _output.WriteLine("------+-------+----------+----------+----------+--------+----------");

            // To determine which index target expects, parse HTF_Bar_DateTime from raw CSV
            var targetMeta = ParseTargetHtfBarDatetime();

            for (int k = 0; k < sample; k++)
            {
                var t = targets[k];
                if (!targetMeta.TryGetValue(t.Index, out var htfBarDt)) continue;
                if (!tsToIdx4H.TryGetValue(htfBarDt, out var idx4H))
                {
                    _output.WriteLine($"  #{t.Index,3} | 4H bar {htfBarDt} not found");
                    continue;
                }

                var cutler = cutler4H[idx4H];
                var wilder = wilder4H[idx4H];
                var ewm = ewm4H[idx4H];

                _output.WriteLine(
                    $"  #{t.Index,3} | {t.HtfRsi,5:F2} | " +
                    $"{(cutler?.ToString("F2") ?? "null"),8} | " +
                    $"{(wilder?.ToString("F2") ?? "null"),8} | " +
                    $"{(ewm?.ToString("F2") ?? "null"),8} | " +
                    $"{idx4H,6} | {quotes4H[idx4H].Close,10:F2}");
            }

            // Full sample
            int cCutler = 0, cWilder = 0, cEwm = 0, n = 0;
            foreach (var t in targets)
            {
                if (!targetMeta.TryGetValue(t.Index, out var htfBarDt)) continue;
                if (!tsToIdx4H.TryGetValue(htfBarDt, out var idx4H)) continue;
                n++;

                if (cutler4H[idx4H].HasValue && Math.Abs(cutler4H[idx4H].Value - t.HtfRsi) < 0.1) cCutler++;
                if (wilder4H[idx4H].HasValue && Math.Abs(wilder4H[idx4H].Value - t.HtfRsi) < 0.1) cWilder++;
                if (ewm4H[idx4H].HasValue && Math.Abs(ewm4H[idx4H].Value - t.HtfRsi) < 0.1) cEwm++;
            }

            _output.WriteLine("");
            _output.WriteLine($"=== FULL SAMPLE (n={n}, tolerance 0.1) ===");
            _output.WriteLine($"Cutler's RSI(14) on native 4H: {cCutler}/{n} ({100.0 * cCutler / n:F1}%)");
            _output.WriteLine($"Wilder RSI(14) on native 4H:   {cWilder}/{n} ({100.0 * cWilder / n:F1}%)");
            _output.WriteLine($"EWM RSI(14) on native 4H:      {cEwm}/{n} ({100.0 * cEwm / n:F1}%)");

            targets.Count.Should().BeGreaterThan(0);
        }

        /// <summary>
        /// Parse HTF_Bar_DateTime column (col 18) from the target CSV and map
        /// by trade index. Used to line up target trades to the native 4H CSV.
        /// </summary>
        private static Dictionary<int, DateTime> ParseTargetHtfBarDatetime()
        {
            var path = Path.Combine(AppContext.BaseDirectory,
                "HtfRsiVolExpansion", "TestData", "trade_list_target.csv");
            var map = new Dictionary<int, DateTime>();
            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim().TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 19) continue;
                if (!int.TryParse(parts[0].Trim(), out var idx)) continue;
                if (DateTime.TryParse(parts[18].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    map[idx] = dt;
            }
            return map;
        }

        /// <summary>
        /// Diagnostic #6: For entries at exact 4H boundaries, compare RSI using
        /// different bar selections to understand the off-by-one pattern.
        /// At boundaries, our partial RSI adds a zero change (diluted).
        /// The target often matches the NEXT bar's RSI.
        /// Tests: (a) closed RSI at htfIdx, (b) partial RSI, (c) closed RSI
        /// at htfIdx+1, (d) partial RSI using NEXT 15M candle as partial close.
        /// </summary>
        [Fact]
        public void Diagnostic_BoundaryRsiOffByOne()
        {
            var candles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_15M.csv"));
            var targets = LoadTargetTrades(
                Path.Combine("HtfRsiVolExpansion", "TestData", "trade_list_target.csv"));
            var quotes4H = Load4HCsv(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_4H.csv"));

            var closes4H = quotes4H.Select(q => q.Close).ToList();
            var cutler4H = CutlersRsi(closes4H, 14);

            var closeToIdx15M = new Dictionary<DateTime, int>();
            for (int i = 0; i < candles.Count; i++)
                closeToIdx15M[candles[i].CloseTime] = i;

            var tsToIdx4H = new Dictionary<DateTime, int>();
            for (int i = 0; i < quotes4H.Count; i++)
                tsToIdx4H[quotes4H[i].Timestamp] = i;

            // Categorize entries as boundary (entry time == 4H close time) or mid-bar
            var boundaryEntries = new List<TargetTrade>();
            var midBarEntries = new List<TargetTrade>();
            foreach (var t in targets)
            {
                if (tsToIdx4H.ContainsKey(t.EntryTime))
                    boundaryEntries.Add(t);
                else
                    midBarEntries.Add(t);
            }

            _output.WriteLine($"Boundary entries (entry == 4H close time): {boundaryEntries.Count}");
            _output.WriteLine($"Mid-bar entries: {midBarEntries.Count}");
            _output.WriteLine("");

            // For boundary entries, compare RSI hypotheses
            _output.WriteLine("=== BOUNDARY ENTRIES: RSI comparison ===");
            _output.WriteLine("Trade | Dir   | Entry Time       | Target | Closed  | Partial | Next    | Closed-1 | Best");
            _output.WriteLine("------+-------+------------------+--------+---------+---------+---------+----------+------");

            int closedWins = 0, partialWins = 0, nextWins = 0, closedM1Wins = 0;
            double totalClosedErr = 0, totalPartialErr = 0, totalNextErr = 0;

            foreach (var t in boundaryEntries)
            {
                int idx4H = tsToIdx4H[t.EntryTime];
                int idx15M = closeToIdx15M[t.EntryTime];

                // (a) Standard closed-bar Cutler RSI at this bar
                double? closedRsi = cutler4H[idx4H];

                // (b) Partial RSI: period-1 closed changes + 0 partial (diluted)
                double? partialRsi = null;
                if (idx4H >= 14)
                {
                    double gs = 0, ls = 0;
                    for (int k = idx4H - 14 + 2; k <= idx4H; k++)
                    {
                        var d = (double)(closes4H[k] - closes4H[k - 1]);
                        if (d > 0) gs += d; else ls += -d;
                    }
                    // partial = entry close - bar close = 0 at boundary
                    var pd = (double)(candles[idx15M].Close - closes4H[idx4H]);
                    if (pd > 0) gs += pd; else ls += -pd;
                    var ag = gs / 14.0; var al = ls / 14.0;
                    partialRsi = al == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + ag / al);
                }

                // (c) Closed RSI at next bar
                double? nextRsi = idx4H + 1 < cutler4H.Count ? cutler4H[idx4H + 1] : null;

                // (d) Closed RSI at previous bar
                double? closedM1Rsi = idx4H > 0 ? cutler4H[idx4H - 1] : null;

                // Determine best match
                var errors = new (string name, double err)[]
                {
                    ("closed", closedRsi.HasValue ? Math.Abs(closedRsi.Value - t.HtfRsi) : 999),
                    ("partial", partialRsi.HasValue ? Math.Abs(partialRsi.Value - t.HtfRsi) : 999),
                    ("next", nextRsi.HasValue ? Math.Abs(nextRsi.Value - t.HtfRsi) : 999),
                    ("prev", closedM1Rsi.HasValue ? Math.Abs(closedM1Rsi.Value - t.HtfRsi) : 999),
                };
                var best = errors.OrderBy(e => e.err).First();

                if (closedRsi.HasValue) totalClosedErr += Math.Abs(closedRsi.Value - t.HtfRsi);
                if (partialRsi.HasValue) totalPartialErr += Math.Abs(partialRsi.Value - t.HtfRsi);
                if (nextRsi.HasValue) totalNextErr += Math.Abs(nextRsi.Value - t.HtfRsi);

                if (best.name == "closed") closedWins++;
                else if (best.name == "partial") partialWins++;
                else if (best.name == "next") nextWins++;
                else closedM1Wins++;

                // Show first 40 boundary entries
                if (boundaryEntries.IndexOf(t) < 40)
                {
                    _output.WriteLine(
                        $"  {t.Index,3} | {t.Direction,-5} | {t.EntryTime:yyyy-MM-dd HH:mm} | " +
                        $"{t.HtfRsi,6:F1} | {(closedRsi?.ToString("F1") ?? "null"),7} | " +
                        $"{(partialRsi?.ToString("F1") ?? "null"),7} | {(nextRsi?.ToString("F1") ?? "null"),7} | " +
                        $"{(closedM1Rsi?.ToString("F1") ?? "null"),8} | {best.name}({best.err:F1})");
                }
            }

            _output.WriteLine("");
            _output.WriteLine($"=== BOUNDARY SUMMARY (n={boundaryEntries.Count}) ===");
            _output.WriteLine($"Best match counts: closed={closedWins} partial={partialWins} next={nextWins} prev={closedM1Wins}");
            _output.WriteLine($"Mean error: closed={totalClosedErr / boundaryEntries.Count:F2} partial={totalPartialErr / boundaryEntries.Count:F2} next={totalNextErr / boundaryEntries.Count:F2}");

            // Now check: what if we used closed RSI (no partial) at boundaries
            // and partial RSI (current behavior) at mid-bar entries?
            // Count how many RSI gate failures each approach causes.
            int gatePassPartial = 0, gatePassClosed = 0, gatePassNext = 0;
            int gatePassHybrid = 0; // closed at boundary, partial at mid-bar

            foreach (var t in targets)
            {
                if (!closeToIdx15M.TryGetValue(t.EntryTime, out int idx15M)) continue;
                bool isBoundary = tsToIdx4H.ContainsKey(t.EntryTime);
                int idx4H;
                if (isBoundary)
                    idx4H = tsToIdx4H[t.EntryTime];
                else
                {
                    // Find most recent 4H bar
                    idx4H = -1;
                    for (int j = quotes4H.Count - 1; j >= 0; j--)
                        if (quotes4H[j].Timestamp <= t.EntryTime) { idx4H = j; break; }
                }
                if (idx4H < 14) continue;

                // Current approach: partial RSI
                double gs = 0, ls = 0;
                for (int k = idx4H - 14 + 2; k <= idx4H; k++)
                {
                    var d = (double)(closes4H[k] - closes4H[k - 1]);
                    if (d > 0) gs += d; else ls += -d;
                }
                var pd = (double)(candles[idx15M].Close - closes4H[idx4H]);
                if (pd > 0) gs += pd; else ls += -pd;
                var ag = gs / 14.0; var al = ls / 14.0;
                double pRsi = al == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + ag / al);

                // Closed RSI at current bar
                double? cRsi = cutler4H[idx4H];

                // Next bar RSI
                double? nRsi = idx4H + 1 < cutler4H.Count ? cutler4H[idx4H + 1] : null;

                // Hybrid: closed at boundary, partial at mid-bar
                double hRsi = isBoundary ? (cRsi ?? pRsi) : pRsi;

                bool partialPass = (t.Direction == TradeDirection.Long && pRsi > 60) ||
                                   (t.Direction == TradeDirection.Short && pRsi < 40);
                bool closedPass = cRsi.HasValue && ((t.Direction == TradeDirection.Long && cRsi.Value > 60) ||
                                   (t.Direction == TradeDirection.Short && cRsi.Value < 40));
                bool nextPass = nRsi.HasValue && ((t.Direction == TradeDirection.Long && nRsi.Value > 60) ||
                                   (t.Direction == TradeDirection.Short && nRsi.Value < 40));
                bool hybridPass = (t.Direction == TradeDirection.Long && hRsi > 60) ||
                                  (t.Direction == TradeDirection.Short && hRsi < 40);

                if (partialPass) gatePassPartial++;
                if (closedPass) gatePassClosed++;
                if (nextPass) gatePassNext++;
                if (hybridPass) gatePassHybrid++;
            }

            _output.WriteLine("");
            _output.WriteLine($"=== RSI GATE PASS RATES (all {targets.Count} targets) ===");
            _output.WriteLine($"Current (partial everywhere):                    {gatePassPartial}/{targets.Count} ({100.0 * gatePassPartial / targets.Count:F1}%)");
            _output.WriteLine($"Closed RSI everywhere:                           {gatePassClosed}/{targets.Count} ({100.0 * gatePassClosed / targets.Count:F1}%)");
            _output.WriteLine($"Next bar RSI everywhere:                         {gatePassNext}/{targets.Count} ({100.0 * gatePassNext / targets.Count:F1}%)");
            _output.WriteLine($"Hybrid (closed at boundary, partial at mid-bar): {gatePassHybrid}/{targets.Count} ({100.0 * gatePassHybrid / targets.Count:F1}%)");

            // Find the 17 gate failures and show their partial RSI values
            _output.WriteLine("");
            _output.WriteLine("=== RSI GATE FAILURES with partial RSI (threshold 60/40) ===");
            _output.WriteLine("Trade | Dir   | Entry Time       | Target | Partial | Next   | Boundary | Gap from threshold");
            int failCount = 0;
            double maxGap = 0;
            foreach (var t in targets)
            {
                if (!closeToIdx15M.TryGetValue(t.EntryTime, out int idx15M2)) continue;
                bool isBoundary2 = tsToIdx4H.ContainsKey(t.EntryTime);
                int idx4H2;
                if (isBoundary2)
                    idx4H2 = tsToIdx4H[t.EntryTime];
                else
                {
                    idx4H2 = -1;
                    for (int j = quotes4H.Count - 1; j >= 0; j--)
                        if (quotes4H[j].Timestamp <= t.EntryTime) { idx4H2 = j; break; }
                }
                if (idx4H2 < 14) continue;

                double gs2 = 0, ls2 = 0;
                for (int k = idx4H2 - 14 + 2; k <= idx4H2; k++)
                {
                    var d = (double)(closes4H[k] - closes4H[k - 1]);
                    if (d > 0) gs2 += d; else ls2 += -d;
                }
                var pd2 = (double)(candles[idx15M2].Close - closes4H[idx4H2]);
                if (pd2 > 0) gs2 += pd2; else ls2 += -pd2;
                var ag2 = gs2 / 14.0; var al2 = ls2 / 14.0;
                double pRsi2 = al2 == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + ag2 / al2);

                double? nRsi2 = idx4H2 + 1 < cutler4H.Count ? cutler4H[idx4H2 + 1] : null;

                bool pass = (t.Direction == TradeDirection.Long && pRsi2 > 60) ||
                            (t.Direction == TradeDirection.Short && pRsi2 < 40);
                if (!pass)
                {
                    double gap = t.Direction == TradeDirection.Long ? 60.0 - pRsi2 : pRsi2 - 40.0;
                    if (gap > maxGap) maxGap = gap;
                    _output.WriteLine(
                        $"  {t.Index,3} | {t.Direction,-5} | {t.EntryTime:yyyy-MM-dd HH:mm} | " +
                        $"{t.HtfRsi,6:F1} | {pRsi2,7:F1} | {(nRsi2?.ToString("F1") ?? "null"),6} | " +
                        $"{(isBoundary2 ? "YES" : "no "),8} | {gap:F1}");
                    failCount++;
                }
            }
            _output.WriteLine($"Total failures: {failCount}, max gap from threshold: {maxGap:F1}");

            // Try threshold sweep
            _output.WriteLine("");
            _output.WriteLine("=== THRESHOLD SWEEP (partial RSI) ===");
            foreach (var offset in new[] { 0, 1, 2, 3, 4, 5, 7, 10 })
            {
                double longTh = 60.0 - offset;
                double shortTh = 40.0 + offset;
                int pass2 = 0;
                foreach (var t in targets)
                {
                    if (!closeToIdx15M.TryGetValue(t.EntryTime, out int idx15M3)) continue;
                    bool isBoundary3 = tsToIdx4H.ContainsKey(t.EntryTime);
                    int idx4H3;
                    if (isBoundary3) idx4H3 = tsToIdx4H[t.EntryTime];
                    else { idx4H3 = -1; for (int j = quotes4H.Count - 1; j >= 0; j--) if (quotes4H[j].Timestamp <= t.EntryTime) { idx4H3 = j; break; } }
                    if (idx4H3 < 14) continue;

                    double gs3 = 0, ls3 = 0;
                    for (int k = idx4H3 - 14 + 2; k <= idx4H3; k++)
                    { var d = (double)(closes4H[k] - closes4H[k - 1]); if (d > 0) gs3 += d; else ls3 += -d; }
                    var pd3 = (double)(candles[idx15M3].Close - closes4H[idx4H3]);
                    if (pd3 > 0) gs3 += pd3; else ls3 += -pd3;
                    var ag3 = gs3 / 14.0; var al3 = ls3 / 14.0;
                    double pRsi3 = al3 == 0 ? 100.0 : 100.0 - 100.0 / (1.0 + ag3 / al3);

                    bool p = (t.Direction == TradeDirection.Long && pRsi3 > longTh) ||
                             (t.Direction == TradeDirection.Short && pRsi3 < shortTh);
                    if (p) pass2++;
                }
                _output.WriteLine($"  {longTh:F0}/{shortTh:F0}: {pass2}/{targets.Count} ({100.0 * pass2 / targets.Count:F1}%)");
            }

            targets.Count.Should().BeGreaterThan(0);
        }

        /// <summary>
        /// Diagnostic #4: try more RSI shapes — different periods, 15M base,
        /// look-ahead rolling, etc. Hunting for whatever the backtest actually
        /// uses for its HTF_RSI column.
        /// </summary>
        [Fact]
        public void Diagnostic_ExtendedRsiHunt()
        {
            var candles = LoadCandles(
                Path.Combine("HtfRsiVolExpansion", "TestData", "BTCUSDT_15M.csv"));
            var targets = LoadTargetTrades(
                Path.Combine("HtfRsiVolExpansion", "TestData", "trade_list_target.csv"));

            var aggregator = new FourHourCandleAggregator();
            var quotes4H = aggregator.AggregateHistoric(candles.Select(c => new Quote
            {
                Timestamp = c.CloseTime,
                Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume
            })).ToList();
            var closes4H = quotes4H.Select(q => q.Close).ToList();

            // RSI on 15M closes (Cutler's)
            var closes15M = candles.Select(c => c.Close).ToList();
            var cutler15M14 = CutlersRsi(closes15M, 14);

            // RSI on 4H with different periods
            var cutler4H7 = CutlersRsi(closes4H, 7);
            var cutler4H14 = CutlersRsi(closes4H, 14);
            var cutler4H21 = CutlersRsi(closes4H, 21);
            var wilder4H = quotes4H.Cast<IQuote>().ToList().ToRsi(14).Select(r => r.Rsi).ToList();

            var closeToIndex15M = new Dictionary<DateTime, int>();
            for (int i = 0; i < candles.Count; i++)
                closeToIndex15M[candles[i].CloseTime] = i;

            int n = 0;
            int cCutler15M = 0, cCutler4H7 = 0, cCutler4H21 = 0;
            int cLookaheadNextBar = 0;

            foreach (var t in targets)
            {
                if (!closeToIndex15M.TryGetValue(t.EntryTime, out int idx)) continue;
                n++;

                int htfIdx = -1;
                for (int j = quotes4H.Count - 1; j >= 0; j--)
                    if (quotes4H[j].Timestamp <= t.EntryTime) { htfIdx = j; break; }

                // Direct RSI on 15M at entry
                if (cutler15M14[idx].HasValue && Math.Abs(cutler15M14[idx].Value - t.HtfRsi) < 1.0)
                    cCutler15M++;

                // 4H Cutler RSI(7) / RSI(21)
                if (htfIdx >= 0 && cutler4H7[htfIdx].HasValue && Math.Abs(cutler4H7[htfIdx].Value - t.HtfRsi) < 1.0)
                    cCutler4H7++;
                if (htfIdx >= 0 && cutler4H21[htfIdx].HasValue && Math.Abs(cutler4H21[htfIdx].Value - t.HtfRsi) < 1.0)
                    cCutler4H21++;

                // Look-ahead: use the NEXT 4H bar (bar that contains the entry, looking at its final close)
                if (htfIdx + 1 < cutler4H14.Count && cutler4H14[htfIdx + 1].HasValue &&
                    Math.Abs(cutler4H14[htfIdx + 1].Value - t.HtfRsi) < 1.0)
                    cLookaheadNextBar++;

                // Look-ahead: shift the 15M by 16 bars (next bar close) and use rolling Cutler at idx+16
                // Approximation: use Cutler's 4H(14) at htfIdx+1 which is the same
            }

            _output.WriteLine($"=== Extended RSI hunt (n={n}) ===");
            _output.WriteLine($"Cutler RSI(14) on 15M:     {cCutler15M}/{n} ({100.0 * cCutler15M / n:F1}%)");
            _output.WriteLine($"Cutler RSI(7)  on 4H:      {cCutler4H7}/{n} ({100.0 * cCutler4H7 / n:F1}%)");
            _output.WriteLine($"Cutler RSI(21) on 4H:      {cCutler4H21}/{n} ({100.0 * cCutler4H21 / n:F1}%)");
            _output.WriteLine($"Cutler RSI(14) NEXT 4H:    {cLookaheadNextBar}/{n} ({100.0 * cLookaheadNextBar / n:F1}%)");

            // Print first 15 side-by-side to see patterns
            _output.WriteLine("");
            _output.WriteLine("Trade | tgt   | 15M   | 4H7   | 4H14  | 4H21  | 4Hnext | Wilder");
            _output.WriteLine("------+-------+-------+-------+-------+-------+--------+-------");
            for (int k = 0; k < Math.Min(15, targets.Count); k++)
            {
                var t = targets[k];
                if (!closeToIndex15M.TryGetValue(t.EntryTime, out int idx)) continue;
                int htfIdx = -1;
                for (int j = quotes4H.Count - 1; j >= 0; j--)
                    if (quotes4H[j].Timestamp <= t.EntryTime) { htfIdx = j; break; }
                var a = cutler15M14[idx]?.ToString("F1") ?? "null";
                var b = htfIdx >= 0 ? (cutler4H7[htfIdx]?.ToString("F1") ?? "null") : "null";
                var c = htfIdx >= 0 ? (cutler4H14[htfIdx]?.ToString("F1") ?? "null") : "null";
                var d = htfIdx >= 0 ? (cutler4H21[htfIdx]?.ToString("F1") ?? "null") : "null";
                var e = (htfIdx + 1 < cutler4H14.Count ? cutler4H14[htfIdx + 1]?.ToString("F1") : null) ?? "null";
                var w = htfIdx >= 0 ? (wilder4H[htfIdx]?.ToString("F1") ?? "null") : "null";
                _output.WriteLine($"  #{t.Index,3} | {t.HtfRsi,5:F1} | {a,5} | {b,5} | {c,5} | {d,5} | {e,6} | {w,5}");
            }

            targets.Count.Should().BeGreaterThan(0);
        }
    }
}
