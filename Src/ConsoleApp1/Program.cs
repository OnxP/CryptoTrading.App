using Binance;
using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.Config;
using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CryptoTrading.App.BackTesting
{
    /// <summary>
    /// Backtest console app: replays 4H / 15M / 1M BTCUSDT history from SQL
    /// through <see cref="HtfRsiVolExpansionAlgorithm"/> (Wilder RSI on native 4H,
    /// Wilder ATR on 15M). Signals come from the real algorithm; execution is
    /// simulated on 1M candles with a best-entry pullback limit and SL/TP/
    /// trailing/time-stop rules resolved at 15M boundaries. Leverage = 5x.
    /// </summary>
    internal static class Program
    {
        private const int Leverage = 5;
        private const string DefaultSymbol = "BTCUSDT";

        private static int Main(string[] args)
        {
            try
            {
                var opts = ParseArgs(args);

                if (!string.IsNullOrEmpty(opts.AnalyzeSlCsv))
                    return SlPostmortem.Run(opts.Symbol, opts.AnalyzeSlCsv, opts.OutputCsv);

                Console.WriteLine("================================================================");
                Console.WriteLine("  HTF RSI Vol Expansion — BACKTEST");
                Console.WriteLine("================================================================");
                Console.WriteLine($"  Symbol:        {opts.Symbol}");
                Console.WriteLine($"  Range:         {opts.From:yyyy-MM-dd} → {opts.To:yyyy-MM-dd}");
                Console.WriteLine($"  Start BTC:     {opts.StartBtc}");
                Console.WriteLine($"  Leverage:      {Leverage}x");
                Console.WriteLine($"  Trade CSV:     {opts.OutputCsv ?? "(none)"}");
                Console.WriteLine("----------------------------------------------------------------");

                Console.Write("  Loading 4H...  ");
                var candles4H = BacktestDataLoader.Load(opts.Symbol, CandlestickInterval.Hours_4, opts.From.AddDays(-30), opts.To);
                Console.WriteLine($"{candles4H.Count} bars");

                Console.Write("  Loading 15M... ");
                var candles15M = BacktestDataLoader.Load(opts.Symbol, CandlestickInterval.Minutes_15, opts.From, opts.To);
                Console.WriteLine($"{candles15M.Count} bars");

                Console.Write("  Loading 1M...  ");
                var candles1M = BacktestDataLoader.Load(opts.Symbol, CandlestickInterval.Minute, opts.From, opts.To);
                Console.WriteLine($"{candles1M.Count} bars");

                if (candles4H.Count == 0 || candles15M.Count == 0 || candles1M.Count == 0)
                {
                    Console.Error.WriteLine("Insufficient data for one or more timeframes.");
                    return 2;
                }

                Console.WriteLine("----------------------------------------------------------------");
                var result = Replay(opts, candles4H, candles15M, candles1M);

                PrintResults(result, opts);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        // ---- Replay result -----------------------------------------------

        private sealed record ReplayResult(
            List<SimulatedTrade> Trades,
            decimal StartEquity,
            decimal EndEquity,
            int SignalCount,
            DateTime FirstBar,
            DateTime LastBar);

        // ---- Replay: HtfRsiVolExpansionAlgorithm (Wilder RSI) ------------

        private static ReplayResult Replay(
            BacktestOptions opts,
            List<Candlestick> candles4H,
            List<Candlestick> candles15M,
            List<Candlestick> candles1M)
        {
            _ = RequestTracker.Instance;
            RequestTracker.Requests.Clear();

            var logger = NullLogger<HtfRsiVolExpansionAlgorithm>.Instance;
            var algo = new HtfRsiVolExpansionAlgorithm(logger);

            // NOTE: the algorithm fires setups immediately on 15M close —
            // no BbGuide defer lives here anymore. The backtest's own
            // BbGuide (inside TradeSimulator) still governs entry timing
            // for this harness. Commit-2 replaces TradeSimulator with the
            // real entry/exit strategies, at which point the entry-strategy
            // BbGuide takes over.

            var config = new CryptoConfig
            {
                Interval = CandlestickInterval.Minutes_15,
                RunType = RunTypeEnum.BackTesting,
                StartBtcAmount = opts.StartBtc
            };
            algo.Configure(config);

            var md = new FakeMarketData();
            algo.Subscribe(Symbol.BTC_USDT, md);

            var firstStreamTime = candles15M[0].CloseTime;
            var seed4H = candles4H.Where(c => c.CloseTime < firstStreamTime).ToList();
            var live4H = candles4H.Where(c => c.CloseTime >= firstStreamTime).ToList();

            md.FireHistoric15M(Array.Empty<Candlestick>());
            md.FireHistoric4H(seed4H);

            var tsField = typeof(HtfRsiVolExpansionAlgorithm)
                .GetField("_tradingState", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Cannot access HtfRsiVolExpansionAlgorithm._tradingState");

            var sim = new TradeSimulator(Leverage);
            decimal startEquityUsdt = 0m;
            bool equitySeeded = false;

            // Pre-aggregate 30M bars from the 15M stream. Used by the
            // TradeSimulator structure-break exit to know the entry-30M-bar's
            // adverse extreme. Aggregation is deterministic and cheap, so we
            // compute it once up front rather than tracking incrementally.
            var bars30M = Build30MBars(candles15M);

            // Rolling 15M EMA20 for the entry classifier. Seeded as a simple
            // mean of the first 20 closes, then smoothed with k = 2/21. The
            // current value is read when a 15M signal fires and passed into
            // TradeSimulator.OpenFromSignal, where it becomes the Extension
            // measure that picks Early / Neutral / Late mode.
            const int Ema20Period = 20;
            decimal ema20_15M = 0m;
            int ema20SeedCount = 0;
            decimal ema20SeedSum = 0m;
            const decimal Ema20K = 2m / (Ema20Period + 1m);

            // Rolling 15M MACD(12,26,9) — classic defaults. Each leg is an EMA
            // over 15M closes, SMA-seeded. The signal line is an EMA of the
            // MACD line itself. Histogram = MACD - signal; its sign (relative
            // to the signal direction) drives the regime classifier.
            const int MacdFast = 12, MacdSlow = 26, MacdSignal = 9;
            decimal emaFast = 0m, emaSlow = 0m, macdSignalEma = 0m;
            int emaFastSeed = 0, emaSlowSeed = 0, macdSignalSeed = 0;
            decimal emaFastSum = 0m, emaSlowSum = 0m, macdSignalSum = 0m;
            const decimal MacdFastK = 2m / (MacdFast + 1m);
            const decimal MacdSlowK = 2m / (MacdSlow + 1m);
            const decimal MacdSignalK = 2m / (MacdSignal + 1m);
            decimal macdHist = 0m;

            // Rolling 15M Bollinger Bands(20, 2). Middle = SMA(20), bands =
            // middle ± 2 × stddev (population-style sigma over the window).
            // A 20-close ring buffer is cheaper than re-summing every bar and
            // keeps the stddev numerically stable across the backtest.
            const int BbPeriod = 20;
            const decimal BbSigma = 2m;
            var bbWindow = new decimal[BbPeriod];
            int bbFill = 0, bbIndex = 0;
            decimal bbMiddle = 0m, bbUpper = 0m, bbLower = 0m;

            // Merged-stream replay: events fire in strict chronological order,
            // and at ties the priority 4H → 15M → 1M is enforced. This keeps
            // the algorithm's higher-timeframe indicators current before any
            // 15M signal check, and before the 1M candle drives exit logic.
            foreach (var ev in MergeCandleStreams(live4H, candles15M, candles1M))
            {
                switch (ev.Type)
                {
                    case EvType.Hours4:
                        md.FireLive4H(ev.Candle);
                        break;

                    case EvType.Minutes15:
                        md.FireLive15M(ev.Candle);

                        // Update rolling EMA20 before signal capture so the
                        // value we pass in reflects this bar's close — the
                        // same close that triggered the signal.
                        if (ema20SeedCount < Ema20Period)
                        {
                            ema20SeedSum += ev.Candle.Close;
                            ema20SeedCount++;
                            if (ema20SeedCount == Ema20Period)
                                ema20_15M = ema20SeedSum / Ema20Period;
                        }
                        else
                        {
                            ema20_15M = ev.Candle.Close * Ema20K + ema20_15M * (1m - Ema20K);
                        }

                        // --- Rolling 15M MACD(12,26,9) ---
                        // Seed each EMA from the mean of its first N closes,
                        // then smooth with k = 2/(N+1). The signal line seeds
                        // from the mean of the first 9 MACD-line values AFTER
                        // both fast/slow EMAs have seeded — so the first
                        // meaningful histogram prints after slow+signal bars.
                        {
                            decimal close = ev.Candle.Close;
                            if (emaFastSeed < MacdFast)
                            {
                                emaFastSum += close; emaFastSeed++;
                                if (emaFastSeed == MacdFast) emaFast = emaFastSum / MacdFast;
                            }
                            else
                            {
                                emaFast = close * MacdFastK + emaFast * (1m - MacdFastK);
                            }
                            if (emaSlowSeed < MacdSlow)
                            {
                                emaSlowSum += close; emaSlowSeed++;
                                if (emaSlowSeed == MacdSlow) emaSlow = emaSlowSum / MacdSlow;
                            }
                            else
                            {
                                emaSlow = close * MacdSlowK + emaSlow * (1m - MacdSlowK);
                            }
                            if (emaSlowSeed >= MacdSlow && emaFastSeed >= MacdFast)
                            {
                                decimal macdLine = emaFast - emaSlow;
                                if (macdSignalSeed < MacdSignal)
                                {
                                    macdSignalSum += macdLine; macdSignalSeed++;
                                    if (macdSignalSeed == MacdSignal)
                                        macdSignalEma = macdSignalSum / MacdSignal;
                                }
                                else
                                {
                                    macdSignalEma = macdLine * MacdSignalK + macdSignalEma * (1m - MacdSignalK);
                                }
                                macdHist = macdLine - macdSignalEma;
                            }
                        }

                        // --- Rolling 15M Bollinger Bands(20, 2) ---
                        // Ring-buffer the last 20 closes, compute SMA and
                        // population stddev directly. At 20 closes per update
                        // this is trivially fast and avoids numerical drift
                        // that would accumulate in incremental stddev.
                        {
                            bbWindow[bbIndex] = ev.Candle.Close;
                            bbIndex = (bbIndex + 1) % BbPeriod;
                            if (bbFill < BbPeriod) bbFill++;
                            if (bbFill == BbPeriod)
                            {
                                decimal sum = 0m;
                                for (int k = 0; k < BbPeriod; k++) sum += bbWindow[k];
                                decimal mean = sum / BbPeriod;
                                decimal varSum = 0m;
                                for (int k = 0; k < BbPeriod; k++)
                                {
                                    decimal d = bbWindow[k] - mean;
                                    varSum += d * d;
                                }
                                decimal variance = varSum / BbPeriod;
                                decimal stdev = (decimal)Math.Sqrt((double)variance);
                                bbMiddle = mean;
                                bbUpper = mean + BbSigma * stdev;
                                bbLower = mean - BbSigma * stdev;
                            }
                        }

                        // Capture any signal the algorithm fired on this bar.
                        if (RequestTracker.Requests.TryRemove(opts.Symbol, out var pair) && !sim.HasActive)
                        {
                            // Loss-cooldown gate: if the sim is serving a
                            // post-losing-streak skip, burn one slot and drop
                            // this signal. Also reset the algorithm's in-
                            // position flag so it's free to evaluate the next
                            // setup (same pattern as EntryCancelled trades in
                            // ApplyCompletionExisting).
                            if (sim.InCooldown)
                            {
                                sim.ConsumeCooldownSkip();
                                var tsGate = (HtfRsiTradingState)tsField.GetValue(algo);
                                if (tsGate != null)
                                {
                                    tsGate.IsInPosition = false;
                                    tsGate.CandlesSinceLastExit = 0;
                                }
                                break;
                            }

                            var request = pair.Item2;
                            var srProp = request.GetType().GetProperty("StrategyResult");
                            var sr = srProp?.GetValue(request) as HtfRsiVolExpansionStrategyResult;
                            if (sr?.Setup != null)
                            {
                                var ts = (HtfRsiTradingState)tsField.GetValue(algo);
                                var (entry30mLow, entry30mHigh) = Last30MBarAtOrBefore(bars30M, sr.Setup.EntryTime);
                                sim.OpenFromSignal(
                                    signalTime: sr.Setup.EntryTime,
                                    direction: sr.Setup.Direction,
                                    signalPrice: sr.Setup.EntryPrice,
                                    stopLoss: sr.Setup.StopLoss,
                                    takeProfit: sr.Setup.TakeProfit,
                                    atrAtSignal: sr.Setup.AtrAtEntry,
                                    initialRisk: sr.Setup.InitialRisk,
                                    htfRsi: sr.Setup.HtfRsi,
                                    volExpansion: sr.Setup.VolExpansionRatio,
                                    probabilityScore: sr.Setup.ProbabilityScore,
                                    equityUsdt: ts?.CurrentEquity ?? startEquityUsdt,
                                    entry30mBarLow: entry30mLow,
                                    entry30mBarHigh: entry30mHigh,
                                    ema20_15M: ema20_15M,
                                    macdHist: macdHist,
                                    bbMiddle: bbMiddle,
                                    bbUpper: bbUpper,
                                    bbLower: bbLower);
                            }
                        }
                        break;

                    case EvType.Minute1:
                        if (!equitySeeded)
                        {
                            startEquityUsdt = (decimal)opts.StartBtc * ev.Candle.Close;
                            equitySeeded = true;
                        }

                        // Always step, even with no active trade, so the
                        // simulator's continuous 1M RSI stays warm for the
                        // next Late-mode fill decision.
                        {
                            var done = sim.Step(ev.Candle);
                            if (done != null)
                            {
                                var ts = (HtfRsiTradingState)tsField.GetValue(algo);
                                ApplyCompletionExisting(ts, done);
                            }
                        }
                        break;
                }
            }

            if (sim.HasActive)
            {
                var last = candles1M[^1];
                var done = sim.ForceCloseAtEnd(last.Close, last.CloseTime);
                if (done != null)
                {
                    var ts = (HtfRsiTradingState)tsField.GetValue(algo);
                    ApplyCompletionExisting(ts, done);
                }
            }

            var endState = (HtfRsiTradingState)tsField.GetValue(algo);
            return new ReplayResult(
                Trades: sim.Completed,
                StartEquity: startEquityUsdt,
                EndEquity: endState?.CurrentEquity ?? startEquityUsdt,
                SignalCount: sim.Completed.Count,
                FirstBar: candles1M[0].CloseTime,
                LastBar: candles1M[^1].CloseTime);
        }

        // ---- Candle stream merge -----------------------------------------

        /// <summary>
        /// Priority assigned to each candle type when multiple close at the
        /// same instant. Lower values fire first: 4H → 15M → 1M.
        /// </summary>
        private enum EvType { Hours4 = 0, Minutes15 = 1, Minute1 = 2 }

        /// <summary>
        /// Three-way merge of sorted candle streams by CloseTime ascending.
        /// Callers rely on the inputs being sorted (BacktestDataLoader orders
        /// by OpenTime, which for a fixed interval matches CloseTime order).
        ///
        /// At a tie on CloseTime the priority is 4H → 15M → 1M so indicators
        /// on the higher timeframe are updated BEFORE the 15M bar that could
        /// fire a signal, and BEFORE the 1M bar that could drive exit logic.
        /// </summary>
        private static IEnumerable<(Candlestick Candle, EvType Type)> MergeCandleStreams(
            IEnumerable<Candlestick> candles4H,
            IEnumerable<Candlestick> candles15M,
            IEnumerable<Candlestick> candles1M)
        {
            using var e4 = candles4H.GetEnumerator();
            using var e15 = candles15M.GetEnumerator();
            using var e1 = candles1M.GetEnumerator();
            bool h4 = e4.MoveNext(), h15 = e15.MoveNext(), h1 = e1.MoveNext();

            while (h4 || h15 || h1)
            {
                var t4 = h4 ? e4.Current.CloseTime : DateTime.MaxValue;
                var t15 = h15 ? e15.Current.CloseTime : DateTime.MaxValue;
                var t1 = h1 ? e1.Current.CloseTime : DateTime.MaxValue;

                // 4H wins on <= because of priority at ties.
                if (t4 <= t15 && t4 <= t1)
                {
                    yield return (e4.Current, EvType.Hours4);
                    h4 = e4.MoveNext();
                }
                else if (t15 <= t1)
                {
                    yield return (e15.Current, EvType.Minutes15);
                    h15 = e15.MoveNext();
                }
                else
                {
                    yield return (e1.Current, EvType.Minute1);
                    h1 = e1.MoveNext();
                }
            }
        }

        // ---- State-sync helpers ------------------------------------------

        // ---- 30M aggregation (for structure-break exit) -----------------
        //
        // Folds consecutive 15M candles into 30M bars on wall-clock
        // boundaries (minute % 30 == 0). In this codebase 15M candles
        // already have CloseTime.Minute divisible by 15, so boundary
        // detection is exact.
        private sealed class Bar30M
        {
            public DateTime CloseTime;
            public decimal High;
            public decimal Low;
        }

        private static List<Bar30M> Build30MBars(List<Candlestick> src15M)
        {
            var result = new List<Bar30M>();
            decimal high = 0, low = 0;
            DateTime closeTime = default;
            bool started = false;
            foreach (var c in src15M)
            {
                if (!started)
                {
                    if (c.OpenTime.Minute % 30 != 0) continue; // wait for 30M alignment
                    high = c.High; low = c.Low; closeTime = c.CloseTime;
                    started = true;
                }
                else
                {
                    if (c.High > high) high = c.High;
                    if (c.Low < low) low = c.Low;
                    closeTime = c.CloseTime;
                }
                if (c.CloseTime.Minute % 30 == 0)
                {
                    result.Add(new Bar30M { CloseTime = closeTime, High = high, Low = low });
                    started = false;
                }
            }
            return result;
        }

        /// <summary>
        /// Binary search for the last 30M bar whose CloseTime is at or before
        /// the given timestamp. Returns (0,0) if no such bar exists.
        /// </summary>
        private static (decimal Low, decimal High) Last30MBarAtOrBefore(List<Bar30M> bars, DateTime t)
        {
            int lo = 0, hi = bars.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (bars[mid].CloseTime <= t) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            if (found < 0) return (0m, 0m);
            return (bars[found].Low, bars[found].High);
        }

        private static void ApplyCompletionExisting(HtfRsiTradingState ts, SimulatedTrade done)
        {
            if (ts == null) return;
            if (done.ExitReason != null && done.ExitReason.StartsWith("EntryCancelled"))
            {
                ts.IsInPosition = false;
                ts.CandlesSinceLastExit = 0;
                return;
            }
            ts.RecordTradeComplete(done.ExitReason, done.PnlUsdt);
        }

        // ---- Output ------------------------------------------------------

        private static void PrintResults(ReplayResult r, BacktestOptions opts)
        {
            var filled = r.Trades.Where(t => t.ExitReason != "EntryCancelled_SLHit" && t.ExitReason != "EntryCancelled_Eof").ToList();
            var pnls = filled.Select(t => t.PnlUsdt).ToList();
            var holdHours = filled.Select(t => (t.ExitTime - t.EntryTime).TotalHours).ToList();

            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  TRADES");
            Console.WriteLine("================================================================");
            Console.WriteLine("  #   Dir    EntryTime             Entry      Exit       Reason         Bars   PnL(USDT)");
            Console.WriteLine("  ---------------------------------------------------------------------------------------");
            int idx = 1;
            foreach (var t in r.Trades)
            {
                Console.WriteLine(
                    $"  {idx,3}  {t.Direction,-5}  {t.EntryTime:yyyy-MM-dd HH:mm}   " +
                    $"{t.EntryPrice,9:F2}  {t.ExitPrice,9:F2}  {t.ExitReason,-14}  {t.BarsHeld,4}   {t.PnlUsdt,10:F2}");
                idx++;
            }

            var metrics = BacktestMetrics.Calculate(pnls, holdHours);
            var netPct = r.StartEquity > 0 ? (r.EndEquity - r.StartEquity) / r.StartEquity : 0m;

            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  SUMMARY");
            Console.WriteLine("================================================================");
            Console.WriteLine($"  Period:                 {r.FirstBar:yyyy-MM-dd HH:mm} → {r.LastBar:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"  Signals fired:          {r.Trades.Count}");
            Console.WriteLine($"  Filled trades:          {filled.Count}");
            Console.WriteLine($"  Cancelled (pre-fill):   {r.Trades.Count - filled.Count}");
            Console.WriteLine($"  Start equity (USDT):    {r.StartEquity:F2}");
            Console.WriteLine($"  End equity   (USDT):    {r.EndEquity:F2}");
            Console.WriteLine($"  Net P&L:                {r.EndEquity - r.StartEquity:F2}  ({netPct:P2})");
            Console.WriteLine();
            Console.WriteLine($"  Win rate:               {metrics.WinRate:P1}   ({metrics.WinningTrades}/{metrics.TotalTrades})");
            Console.WriteLine($"  Avg win:                {metrics.AverageWin:F2}");
            Console.WriteLine($"  Avg loss:               {metrics.AverageLoss:F2}");
            Console.WriteLine($"  Expectancy / trade:     {metrics.Expectancy:F2}");
            Console.WriteLine($"  Profit factor:          {metrics.ProfitFactor:F2}");
            Console.WriteLine($"  Max drawdown:           {metrics.MaxDrawdown:F2}");
            Console.WriteLine($"  Max consec losses:      {metrics.MaxConsecutiveLosses}");
            Console.WriteLine($"  Sharpe (per-trade×√252):{metrics.SharpeRatio:F2}");
            Console.WriteLine($"  Sortino:                {metrics.SortinoRatio:F2}");
            Console.WriteLine($"  Calmar:                 {metrics.CalmarRatio:F2}");
            Console.WriteLine($"  Avg holding (hours):    {metrics.AverageHoldingPeriodHours:F2}");
            Console.WriteLine();

            Console.WriteLine("  Exit reasons:");
            foreach (var grp in r.Trades.GroupBy(t => t.ExitReason).OrderByDescending(g => g.Count()))
                Console.WriteLine($"    {grp.Key,-20} {grp.Count(),4}   P&L: {grp.Sum(x => x.PnlUsdt),10:F2}");
            Console.WriteLine("================================================================");

            if (!string.IsNullOrWhiteSpace(opts.OutputCsv))
                WriteCsv(opts.OutputCsv, r.Trades);
        }

        private static void WriteCsv(string path, IEnumerable<SimulatedTrade> trades)
        {
            using var w = new StreamWriter(path);
            w.WriteLine("Index,Direction,SignalTime,SignalPrice,EntryTime,EntryPrice,ExitTime,ExitPrice,ExitReason,Bars,Quantity,StopLoss,TakeProfit,AtrAtSignal,HtfRsi,VolExpansion,ProbabilityScore,Ema20_15M,Extension,EntryMode,Regime,EffectiveLeverage,MacdHist,BBUpper,BBMiddle,BBLower,BBW,PnlUsdt");
            int i = 1;
            foreach (var t in trades)
            {
                w.WriteLine(string.Join(",",
                    i++,
                    t.Direction,
                    t.SignalTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    t.SignalPrice.ToString("F2", CultureInfo.InvariantCulture),
                    t.EntryTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    t.EntryPrice.ToString("F2", CultureInfo.InvariantCulture),
                    t.ExitTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    t.ExitPrice.ToString("F2", CultureInfo.InvariantCulture),
                    t.ExitReason,
                    t.BarsHeld,
                    t.Quantity.ToString("F6", CultureInfo.InvariantCulture),
                    t.StopLoss.ToString("F2", CultureInfo.InvariantCulture),
                    t.TakeProfit.ToString("F2", CultureInfo.InvariantCulture),
                    t.AtrAtSignal.ToString("F2", CultureInfo.InvariantCulture),
                    t.HtfRsi.ToString("F1", CultureInfo.InvariantCulture),
                    t.VolExpansion.ToString("F2", CultureInfo.InvariantCulture),
                    t.ProbabilityScore,
                    t.Ema20_15M_AtSignal.ToString("F2", CultureInfo.InvariantCulture),
                    t.Extension_AtSignal.ToString("F3", CultureInfo.InvariantCulture),
                    t.EntryMode ?? "",
                    t.Regime ?? "",
                    t.EffectiveLeverage.ToString("F2", CultureInfo.InvariantCulture),
                    t.MacdHist_AtSignal.ToString("F4", CultureInfo.InvariantCulture),
                    t.BBUpper_AtSignal.ToString("F2", CultureInfo.InvariantCulture),
                    t.BBMiddle_AtSignal.ToString("F2", CultureInfo.InvariantCulture),
                    t.BBLower_AtSignal.ToString("F2", CultureInfo.InvariantCulture),
                    t.BBW_AtSignal.ToString("F5", CultureInfo.InvariantCulture),
                    t.PnlUsdt.ToString("F2", CultureInfo.InvariantCulture)));
            }
            Console.WriteLine($"  Trade CSV written: {path}");
        }

        // ---- Args --------------------------------------------------------

        private sealed class BacktestOptions
        {
            public string Symbol = DefaultSymbol;
            public DateTime From = new DateTime(2024, 1, 1);
            public DateTime To = new DateTime(2025, 1, 1);
            public double StartBtc = 2.0;
            public string OutputCsv;
            public string AnalyzeSlCsv;
        }

        private static BacktestOptions ParseArgs(string[] args)
        {
            var opts = new BacktestOptions();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                string Next() => (++i < args.Length) ? args[i] : throw new ArgumentException($"Missing value for {a}");
                switch (a.ToLowerInvariant())
                {
                    case "--strategy":
                        // Accepted for backward-compatibility with old command lines,
                        // but the only supported strategy is HtfRsiVolExpansionAlgorithm
                        // (Wilder RSI on native 4H). Value is ignored.
                        Next();
                        break;
                    case "--symbol": opts.Symbol = Next().ToUpperInvariant(); break;
                    case "--from": opts.From = DateTime.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--to": opts.To = DateTime.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--startbtc": opts.StartBtc = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--out": opts.OutputCsv = Next(); break;
                    case "--analyze-sl": opts.AnalyzeSlCsv = Next(); break;
                    case "-h":
                    case "--help":
                        PrintHelp();
                        Environment.Exit(0);
                        break;
                    default: throw new ArgumentException($"Unknown argument: {a}");
                }
            }
            if (string.IsNullOrEmpty(opts.AnalyzeSlCsv) && opts.To <= opts.From)
                throw new ArgumentException("--to must be after --from");
            return opts;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("CryptoTrading.App.BackTesting");
            Console.WriteLine("  Runs HtfRsiVolExpansionAlgorithm (Wilder RSI on native 4H).");
            Console.WriteLine("  --symbol    Symbol to backtest (default BTCUSDT)");
            Console.WriteLine("  --from      Start date (yyyy-MM-dd, inclusive)");
            Console.WriteLine("  --to        End date   (yyyy-MM-dd, exclusive)");
            Console.WriteLine("  --startBtc  Starting BTC balance (default 2.0)");
            Console.WriteLine("  --out       Optional CSV path for trade list");
        }
    }

}
