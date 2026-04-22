using Binance;
using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
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
    /// through the real <see cref="HtfRsiVolExpansionAlgorithm"/> and the real
    /// <see cref="SimpleExecutionStrategy"/> that it hands off to. 1M candles
    /// drive the execution strategy's shared QuoteHub — the same data rail
    /// used in live and paper — so entry timing (BbGuide) and exit decisions
    /// come from production code, not a parallel reimplementation.
    /// </summary>
    internal static class Program
    {
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

        private sealed record ReplayResult(
            List<SimulatedTrade> Trades,
            decimal StartEquity,
            decimal EndEquity,
            int SignalCount,
            DateTime FirstBar,
            DateTime LastBar);

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

            md.FireHistoric15M(Array.Empty<CryptoTrading.App.Core.Exchange.ExchangeCandlestick>());
            md.FireHistoric4H(seed4H.Select(ExchangeCandlestickBridge.ToNeutral));

            // Reach into the algorithm for its trading state — the harness
            // needs to read/mutate equity on the same instance the algorithm
            // reads at signal time, and capture exit metadata the exit
            // strategy writes there.
            var tsField = typeof(HtfRsiVolExpansionAlgorithm)
                .GetField("_tradingState", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Cannot access HtfRsiVolExpansionAlgorithm._tradingState");

            // StartEquity is only known once the first 1M close arrives — the
            // algorithm converts BTC→USDT at the first 15M close and seeds its
            // own state then. We capture a display-only start-equity here.
            decimal startEquityUsdt = 0m;
            bool equitySeeded = false;

            // The simulator needs the algorithm's tradingState so exit-reason
            // and PnL pass through. It is seeded lazily below after the first
            // 15M arrives (which is when the algorithm builds its state).
            TradeSimulator sim = null;

            // Merged-stream replay: events fire in strict chronological order,
            // and at ties the priority 4H → 15M → 1M is enforced. This keeps
            // the algorithm's higher-timeframe indicators current before any
            // 15M signal check, and before the 1M candle drives exit logic.
            foreach (var ev in MergeCandleStreams(live4H, candles15M, candles1M))
            {
                switch (ev.Type)
                {
                    case EvType.Hours4:
                        md.FireLive4H(ExchangeCandlestickBridge.ToNeutral(ev.Candle));
                        break;

                    case EvType.Minutes15:
                        md.FireLive15M(ExchangeCandlestickBridge.ToNeutral(ev.Candle));

                        if (sim == null)
                        {
                            // The algorithm rebuilds its trading state on the
                            // first 15M close (when it can convert BTC→USDT).
                            // Only now can we hand it to the simulator.
                            var ts = (HtfRsiTradingState)tsField.GetValue(algo);
                            if (ts != null)
                                sim = new TradeSimulator(ts);
                        }

                        // Capture any signal the algorithm fired on this bar.
                        if (sim != null
                            && RequestTracker.Requests.TryRemove(opts.Symbol, out var pair)
                            && !sim.HasActive)
                        {
                            var request = pair.Item2;
                            var srProp = request.GetType().GetProperty("StrategyResult");
                            var sr = srProp?.GetValue(request) as HtfRsiVolExpansionStrategyResult;
                            var stratProp = request.GetType().GetProperty("Strategy");
                            var exec = stratProp?.GetValue(request) as CryptoTrading.App.Core.Strategy.IExecutionStrategy;
                            if (sr?.Setup != null && exec != null)
                                sim.OpenFromSignal(exec, sr.Setup);
                        }
                        break;

                    case EvType.Minute1:
                        if (!equitySeeded)
                        {
                            startEquityUsdt = (decimal)opts.StartBtc * ev.Candle.Close;
                            equitySeeded = true;
                        }

                        // Always step once the sim is initialised, even when
                        // no trade is active — the execution strategy's quote
                        // hub needs every 1M bar to stay warm for the next
                        // fill decision and exit trail.
                        sim?.Step(ev.Candle);
                        break;
                }
            }

            if (sim != null && sim.HasActive)
            {
                var last = candles1M[^1];
                sim.ForceCloseAtEnd(last.Close, last.CloseTime);
            }

            var endState = (HtfRsiTradingState)tsField.GetValue(algo);
            var trades = sim?.Completed ?? new List<SimulatedTrade>();
            return new ReplayResult(
                Trades: trades,
                StartEquity: startEquityUsdt,
                EndEquity: endState?.CurrentEquity ?? startEquityUsdt,
                SignalCount: trades.Count,
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
            w.WriteLine("Index,Direction,SignalTime,SignalPrice,EntryTime,EntryPrice,ExitTime,ExitPrice,ExitReason,Bars,Quantity,Leverage,StopLoss,TakeProfit,AtrAtSignal,HtfRsi,VolExpansion,ProbabilityScore,PnlUsdt");
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
                    t.Leverage,
                    t.StopLoss.ToString("F2", CultureInfo.InvariantCulture),
                    t.TakeProfit.ToString("F2", CultureInfo.InvariantCulture),
                    t.AtrAtSignal.ToString("F2", CultureInfo.InvariantCulture),
                    t.HtfRsi.ToString("F1", CultureInfo.InvariantCulture),
                    t.VolExpansion.ToString("F2", CultureInfo.InvariantCulture),
                    t.ProbabilityScore,
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
            Console.WriteLine("  Runs HtfRsiVolExpansionAlgorithm + SimpleExecutionStrategy end-to-end.");
            Console.WriteLine("  --symbol    Symbol to backtest (default BTCUSDT)");
            Console.WriteLine("  --from      Start date (yyyy-MM-dd, inclusive)");
            Console.WriteLine("  --to        End date   (yyyy-MM-dd, exclusive)");
            Console.WriteLine("  --startBtc  Starting BTC balance (default 2.0)");
            Console.WriteLine("  --out       Optional CSV path for trade list");
        }
    }
}
