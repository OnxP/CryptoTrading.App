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
    /// through the HtfRsiVolExpansion strategy. Signals are produced by the
    /// real algorithm; execution is simulated on 1M candles with a best-entry
    /// pullback limit and the strategy's SL/TP/trailing/time-stop rules.
    /// Leverage is fixed at 5x (matches the strategy's internal constant).
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

                if (candles15M.Count == 0 || candles1M.Count == 0 || candles4H.Count == 0)
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

        // ---- Replay ------------------------------------------------------

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
            // Clean RequestTracker singleton state.
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

            // Seed 4H historic — bars before first 15M; live-stream the rest.
            var firstStreamTime = candles15M[0].CloseTime;
            var seed4H = candles4H.Where(c => c.CloseTime < firstStreamTime).ToList();
            var live4H = candles4H
                .Where(c => c.CloseTime >= firstStreamTime)
                .GroupBy(c => c.CloseTime)
                .ToDictionary(g => g.Key, g => g.First());

            md.FireHistoric15M(Array.Empty<Candlestick>());
            md.FireHistoric4H(seed4H);

            // Access the algo's private _tradingState so we can record trade
            // completion when our simulator closes a 1M-level trade, keeping
            // the gap-counter / cooldown consistent with live behavior.
            var tsField = typeof(HtfRsiVolExpansionAlgorithm)
                .GetField("_tradingState", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Cannot access HtfRsiVolExpansionAlgorithm._tradingState");

            // Index 15M by CloseTime for fast join with 1M stream.
            var by15M = candles15M.ToDictionary(c => c.CloseTime, c => c);

            var sim = new TradeSimulator(Leverage);
            decimal startEquityUsdt = 0m;
            bool equitySeeded = false;

            foreach (var m1 in candles1M)
            {
                // 4H candle closes at this instant → fire to algo first.
                if (live4H.TryGetValue(m1.CloseTime, out var c4))
                    md.FireLive4H(c4);

                // 15M candle closes at this instant → fire to algo, may generate signal.
                bool is15MBoundary = by15M.TryGetValue(m1.CloseTime, out var c15);
                if (is15MBoundary)
                    md.FireLive15M(c15);

                if (!equitySeeded)
                {
                    startEquityUsdt = (decimal)opts.StartBtc * m1.Close;
                    equitySeeded = true;
                }

                // Harvest any new signal fired on this bar.
                if (RequestTracker.Requests.TryRemove(opts.Symbol, out var pair) && !sim.HasActive)
                {
                    var request = pair.Item2;
                    var srProp = request.GetType().GetProperty("StrategyResult");
                    var sr = srProp?.GetValue(request) as HtfRsiVolExpansionStrategyResult;
                    if (sr?.Setup != null)
                    {
                        var ts = (HtfRsiTradingState)tsField.GetValue(algo);
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
                            equityUsdt: ts?.CurrentEquity ?? startEquityUsdt);
                    }
                }

                // Drive any pending/open trade with this 1M candle (best-entry
                // means we only act on candles strictly AFTER the signal bar,
                // which TradeSimulator.Step enforces via elapsed-time check).
                if (sim.HasActive)
                {
                    var done = sim.Step(m1);
                    if (done != null)
                    {
                        var ts = (HtfRsiTradingState)tsField.GetValue(algo);
                        ApplyCompletion(ts, done);
                    }
                }
            }

            // End-of-data: force-close anything still open at last 1M close.
            if (sim.HasActive)
            {
                var last = candles1M[^1];
                var done = sim.ForceCloseAtEnd(last.Close, last.CloseTime);
                if (done != null)
                {
                    var ts = (HtfRsiTradingState)tsField.GetValue(algo);
                    ApplyCompletion(ts, done);
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

            // Exit-reason breakdown
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
            w.WriteLine("Index,Direction,SignalTime,SignalPrice,EntryTime,EntryPrice,ExitTime,ExitPrice,ExitReason,Bars,Quantity,StopLoss,TakeProfit,AtrAtSignal,HtfRsi,VolExpansion,ProbabilityScore,PnlUsdt");
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
                    case "--symbol": opts.Symbol = Next().ToUpperInvariant(); break;
                    case "--from": opts.From = DateTime.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--to": opts.To = DateTime.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--startbtc": opts.StartBtc = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--out": opts.OutputCsv = Next(); break;
                    case "-h":
                    case "--help":
                        PrintHelp();
                        Environment.Exit(0);
                        break;
                    default: throw new ArgumentException($"Unknown argument: {a}");
                }
            }
            if (opts.To <= opts.From) throw new ArgumentException("--to must be after --from");
            return opts;
        }

        /// <summary>
        /// Apply a completed simulated trade to the strategy's trading state.
        /// For cancelled pre-fill trades we just clear the position flag —
        /// no P&L, no win/loss bookkeeping, since no actual trade happened.
        /// </summary>
        private static void ApplyCompletion(HtfRsiTradingState ts, SimulatedTrade done)
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

        private static void PrintHelp()
        {
            Console.WriteLine("CryptoTrading.App.BackTesting");
            Console.WriteLine("  --symbol    Symbol to backtest (default BTCUSDT)");
            Console.WriteLine("  --from      Start date (yyyy-MM-dd, inclusive)");
            Console.WriteLine("  --to        End date   (yyyy-MM-dd, exclusive)");
            Console.WriteLine("  --startBtc  Starting BTC balance (default 2.0)");
            Console.WriteLine("  --out       Optional CSV path for trade list");
        }
    }

}
