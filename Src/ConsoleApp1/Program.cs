using Binance;
using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.Config;
using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Strategies;
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
    /// through one of the HTF RSI Vol Expansion strategies. Signals come from
    /// the real strategy; execution is simulated on 1M candles with a best-entry
    /// pullback limit and SL/TP/trailing/time-stop rules. Leverage = 5x.
    ///
    /// Two strategy modes:
    ///   --strategy existing  HtfRsiVolExpansionAlgorithm (native 4H, Cutler's RSI)
    ///   --strategy sma       HtfRsiVolExpansionStrategy (15M-aggregated 4H, SMA RSI)
    /// </summary>
    internal static class Program
    {
        private const int Leverage = 5;
        private const string DefaultSymbol = "BTCUSDT";

        private enum StrategyMode { Existing, Sma }

        private static int Main(string[] args)
        {
            try
            {
                var opts = ParseArgs(args);

                Console.WriteLine("================================================================");
                Console.WriteLine("  HTF RSI Vol Expansion — BACKTEST");
                Console.WriteLine("================================================================");
                Console.WriteLine($"  Strategy:      {opts.Strategy}");
                Console.WriteLine($"  Symbol:        {opts.Symbol}");
                Console.WriteLine($"  Range:         {opts.From:yyyy-MM-dd} → {opts.To:yyyy-MM-dd}");
                Console.WriteLine($"  Start BTC:     {opts.StartBtc}");
                Console.WriteLine($"  Leverage:      {Leverage}x");
                Console.WriteLine($"  Trade CSV:     {opts.OutputCsv ?? "(none)"}");
                Console.WriteLine("----------------------------------------------------------------");

                // 4H only needed for the Existing strategy (native 4H feed).
                List<Candlestick> candles4H = new();
                if (opts.Strategy == StrategyMode.Existing)
                {
                    Console.Write("  Loading 4H...  ");
                    candles4H = BacktestDataLoader.Load(opts.Symbol, CandlestickInterval.Hours_4, opts.From.AddDays(-30), opts.To);
                    Console.WriteLine($"{candles4H.Count} bars");
                }

                Console.Write("  Loading 15M... ");
                var candles15M = BacktestDataLoader.Load(opts.Symbol, CandlestickInterval.Minutes_15, opts.From, opts.To);
                Console.WriteLine($"{candles15M.Count} bars");

                Console.Write("  Loading 1M...  ");
                var candles1M = BacktestDataLoader.Load(opts.Symbol, CandlestickInterval.Minute, opts.From, opts.To);
                Console.WriteLine($"{candles1M.Count} bars");

                if (candles15M.Count == 0 || candles1M.Count == 0
                    || (opts.Strategy == StrategyMode.Existing && candles4H.Count == 0))
                {
                    Console.Error.WriteLine("Insufficient data for one or more timeframes.");
                    return 2;
                }

                Console.WriteLine("----------------------------------------------------------------");
                var result = opts.Strategy == StrategyMode.Existing
                    ? ReplayExisting(opts, candles4H, candles15M, candles1M)
                    : ReplaySma(opts, candles15M, candles1M);

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

        // ---- Replay: existing HtfRsiVolExpansionAlgorithm ----------------

        private static ReplayResult ReplayExisting(
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
            var live4H = candles4H
                .Where(c => c.CloseTime >= firstStreamTime)
                .GroupBy(c => c.CloseTime)
                .ToDictionary(g => g.Key, g => g.First());

            md.FireHistoric15M(Array.Empty<Candlestick>());
            md.FireHistoric4H(seed4H);

            var tsField = typeof(HtfRsiVolExpansionAlgorithm)
                .GetField("_tradingState", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Cannot access HtfRsiVolExpansionAlgorithm._tradingState");

            var by15M = candles15M.ToDictionary(c => c.CloseTime, c => c);

            var sim = new TradeSimulator(Leverage);
            decimal startEquityUsdt = 0m;
            bool equitySeeded = false;

            foreach (var m1 in candles1M)
            {
                if (live4H.TryGetValue(m1.CloseTime, out var c4))
                    md.FireLive4H(c4);

                if (by15M.TryGetValue(m1.CloseTime, out var c15))
                    md.FireLive15M(c15);

                if (!equitySeeded)
                {
                    startEquityUsdt = (decimal)opts.StartBtc * m1.Close;
                    equitySeeded = true;
                }

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

                if (sim.HasActive)
                {
                    var done = sim.Step(m1);
                    if (done != null)
                    {
                        var ts = (HtfRsiTradingState)tsField.GetValue(algo);
                        ApplyCompletionExisting(ts, done);
                    }
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

        // ---- Replay: HtfRsiVolExpansionStrategy (SMA RSI) ----------------

        /// <summary>
        /// Drives HtfRsiVolExpansionStrategy on 15M candles but executes
        /// on 1M candles via the TradeSimulator for best-entry + precise
        /// SL/TP detection. The strategy's internal trade state is the
        /// signal-side bookkeeper; whenever our 1M sim closes a trade we
        /// reflectively sync the strategy's _currentTrade / _lastExitIndex /
        /// _tradeResults so its gap + cooldown logic remains coherent.
        /// </summary>
        private static ReplayResult ReplaySma(
            BacktestOptions opts,
            List<Candlestick> candles15M,
            List<Candlestick> candles1M)
        {
            var strategy = new HtfRsiVolExpansionStrategy { Leverage = Leverage };

            var stratType = typeof(HtfRsiVolExpansionStrategy);
            var currentTradeField = stratType.GetField("_currentTrade", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Cannot access HtfRsiVolExpansionStrategy._currentTrade");
            var lastExitIdxField = stratType.GetField("_lastExitIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Cannot access HtfRsiVolExpansionStrategy._lastExitIndex");
            var tradeResultsField = stratType.GetField("_tradeResults", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Cannot access HtfRsiVolExpansionStrategy._tradeResults");

            var by15M = candles15M.ToDictionary(c => c.CloseTime, c => c);
            var indexBy15M = new Dictionary<DateTime, int>(candles15M.Count);
            for (int i = 0; i < candles15M.Count; i++)
                indexBy15M[candles15M[i].CloseTime] = i;

            var sim = new TradeSimulator(Leverage);
            decimal startEquityUsdt = 0m;
            decimal currentEquityUsdt = 0m;
            bool equitySeeded = false;
            int latest15MIdx = -1;

            foreach (var m1 in candles1M)
            {
                if (!equitySeeded)
                {
                    startEquityUsdt = (decimal)opts.StartBtc * m1.Close;
                    currentEquityUsdt = startEquityUsdt;
                    equitySeeded = true;
                }

                // Fire the strategy on 15M boundary candles only.
                if (by15M.TryGetValue(m1.CloseTime, out var c15))
                {
                    latest15MIdx = indexBy15M[m1.CloseTime];

                    var signal = strategy.OnCandle(
                        (double)c15.Open, (double)c15.High,
                        (double)c15.Low, (double)c15.Close,
                        (double)c15.Volume, c15.CloseTime, latest15MIdx);

                    // Entry signals (BUY/SELL, not a close) open a 1M sim trade.
                    // The strategy's own close signals are ignored — our 1M sim
                    // owns the exit. The strategy's internal _currentTrade may
                    // or may not have closed on a 15M bar; we force-sync below
                    // when our sim closes.
                    if (signal.HasAction && !signal.IsClose && !sim.HasActive)
                    {
                        var dir = signal.Direction == "long" ? TradeDirection.Long : TradeDirection.Short;
                        var signalPrice = (decimal)signal.Price;
                        var stopLoss = (decimal)signal.StopLoss;
                        var takeProfit = (decimal)signal.TakeProfit;
                        var initialRisk = Math.Abs(signalPrice - stopLoss);
                        var atr = initialRisk / (decimal)strategy.SlAtrMult;

                        sim.OpenFromSignal(
                            signalTime: c15.CloseTime,
                            direction: dir,
                            signalPrice: signalPrice,
                            stopLoss: stopLoss,
                            takeProfit: takeProfit,
                            atrAtSignal: atr,
                            initialRisk: initialRisk,
                            htfRsi: 0,
                            volExpansion: 0,
                            probabilityScore: 0,
                            equityUsdt: currentEquityUsdt);
                    }
                }

                if (sim.HasActive)
                {
                    var done = sim.Step(m1);
                    if (done != null)
                    {
                        currentEquityUsdt += done.PnlUsdt;
                        SyncStrategyOnClose(strategy, currentTradeField, lastExitIdxField, tradeResultsField,
                            done, latest15MIdx);
                    }
                }
            }

            if (sim.HasActive)
            {
                var last = candles1M[^1];
                var done = sim.ForceCloseAtEnd(last.Close, last.CloseTime);
                if (done != null)
                {
                    currentEquityUsdt += done.PnlUsdt;
                    SyncStrategyOnClose(strategy, currentTradeField, lastExitIdxField, tradeResultsField,
                        done, latest15MIdx);
                }
            }

            return new ReplayResult(
                Trades: sim.Completed,
                StartEquity: startEquityUsdt,
                EndEquity: currentEquityUsdt,
                SignalCount: sim.Completed.Count,
                FirstBar: candles1M[0].CloseTime,
                LastBar: candles1M[^1].CloseTime);
        }

        // ---- State-sync helpers ------------------------------------------

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

        /// <summary>
        /// After our 1M simulator closes a trade, clear the SMA strategy's
        /// internal position (it may still be open from its POV) and update
        /// its exit-gap / cooldown state to match our actual close.
        /// </summary>
        private static void SyncStrategyOnClose(
            HtfRsiVolExpansionStrategy strategy,
            FieldInfo currentTradeField,
            FieldInfo lastExitIdxField,
            FieldInfo tradeResultsField,
            SimulatedTrade done,
            int current15MIdx)
        {
            currentTradeField.SetValue(strategy, null);
            if (current15MIdx >= 0)
                lastExitIdxField.SetValue(strategy, current15MIdx);

            if (done.ExitReason != null && !done.ExitReason.StartsWith("EntryCancelled"))
            {
                var results = (List<bool>)tradeResultsField.GetValue(strategy);
                results?.Add(done.PnlUsdt > 0);
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
            Console.WriteLine($"  Strategy:               {opts.Strategy}");
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
            public StrategyMode Strategy = StrategyMode.Existing;
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
                    case "--strategy":
                        opts.Strategy = Next().ToLowerInvariant() switch
                        {
                            "existing" or "htf" or "algo" => StrategyMode.Existing,
                            "sma" or "sma-rsi" or "new" => StrategyMode.Sma,
                            var x => throw new ArgumentException($"Unknown strategy '{x}'. Use 'existing' or 'sma'.")
                        };
                        break;
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

        private static void PrintHelp()
        {
            Console.WriteLine("CryptoTrading.App.BackTesting");
            Console.WriteLine("  --strategy  existing | sma   (default: existing)");
            Console.WriteLine("                existing = HtfRsiVolExpansionAlgorithm (native 4H, Cutler's RSI)");
            Console.WriteLine("                sma      = HtfRsiVolExpansionStrategy (15M-aggregated 4H, SMA RSI)");
            Console.WriteLine("  --symbol    Symbol to backtest (default BTCUSDT)");
            Console.WriteLine("  --from      Start date (yyyy-MM-dd, inclusive)");
            Console.WriteLine("  --to        End date   (yyyy-MM-dd, exclusive)");
            Console.WriteLine("  --startBtc  Starting BTC balance (default 2.0)");
            Console.WriteLine("  --out       Optional CSV path for trade list");
        }
    }
}
