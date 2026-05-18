using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Algorithm.RegimeBased;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CryptoTrading.App.BackTesting
{
    /// <summary>
    /// Post-mortem analysis of StopLoss exits from a trade CSV.
    ///
    /// For each SL trade, loads the next N 15M candles after ExitTime and asks:
    ///   1. How far past SL did price close before reversing? (SL overshoot in ATRs)
    ///   2. After the SL, did price recover toward the original trade direction?
    ///      - MFE (Max Favorable Excursion) measured in R (units of InitialRisk)
    ///      - Did the original TakeProfit price get touched?
    ///      - Time (bars) to peak recovery
    ///   3. How tight was the original SL relative to post-SL volatility?
    ///
    /// This answers "were stops too tight?" and "would widening SL have rescued
    /// these trades?" on the actual data, trade by trade.
    /// </summary>
    internal static class SlPostmortem
    {
        private const int LookaheadBars15M = 32; // 8 hours of 15M candles after SL

        public static int Run(string symbol, string tradesCsv, string outputCsv)
        {
            if (!File.Exists(tradesCsv))
            {
                Console.Error.WriteLine($"Trade CSV not found: {tradesCsv}");
                return 2;
            }

            Console.WriteLine("================================================================");
            Console.WriteLine("  SL POSTMORTEM");
            Console.WriteLine("================================================================");
            Console.WriteLine($"  Trade CSV:  {tradesCsv}");
            Console.WriteLine($"  Lookahead:  {LookaheadBars15M} Ã— 15M bars ({LookaheadBars15M * 15.0 / 60:F1} hours)");
            Console.WriteLine($"  Output:     {outputCsv ?? "(stdout only)"}");

            var trades = ReadTrades(tradesCsv)
                .Where(t => !t.ExitReason.StartsWith("EntryCancelled"))
                .ToList();
            var losers = trades.Where(t => t.PnlUsdt < 0).ToList();
            var winners = trades.Where(t => t.PnlUsdt >= 0).ToList();
            Console.WriteLine($"  Total filled trades:  {trades.Count}");
            Console.WriteLine($"  Winners:              {winners.Count}  (P&L: {winners.Sum(w => w.PnlUsdt):F2})");
            Console.WriteLine($"  Losers:               {losers.Count}   (P&L: {losers.Sum(l => l.PnlUsdt):F2})");
            if (losers.Count == 0)
            {
                Console.WriteLine("  Nothing to analyze.");
                return 0;
            }

            // Range needs to cover both the intra-trade period (from EntryTime)
            // and the post-exit lookahead. Use ALL trades, not just losers â€” we
            // compute winner features too for the fakeout-detection comparison.
            var minTime = trades.Min(t => t.EntryTime);
            var maxTime = trades.Max(t => t.ExitTime).AddHours(LookaheadBars15M * 0.25 + 4);
            Console.Write($"  Loading 15M candles {minTime:yyyy-MM-dd} â†’ {maxTime:yyyy-MM-dd}...  ");
            var candles15M = BacktestDataLoader.Load(symbol, CandleInterval.Minute_15,
                                                     minTime.Date, maxTime.Date.AddDays(1));
            Console.WriteLine($"{candles15M.Count} bars");

            // Also load 4H candles for HTF regime tracking during the trade.
            // Include 30 days of warmup before the earliest entry so RSI(14) is valid.
            Console.Write($"  Loading 4H candles (with 30d warmup)...  ");
            var candles4H = BacktestDataLoader.Load(symbol, CandleInterval.Hour_4,
                                                    minTime.Date.AddDays(-30), maxTime.Date.AddDays(1));
            Console.WriteLine($"{candles4H.Count} bars");

            // Compute 4H Wilder RSI series once.
            var quotes4H = candles4H.Select(c => (IQuote)new Quote
            {
                Timestamp = c.CloseTime,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            }).ToList();
            var rsi4H = quotes4H.ToRsi(14).ToList();

            // Aggregate 15M â†’ 30M and 1H series, compute RSI(14) and EMA(8/21) once.
            // Intermediate TFs sit between the 15M execution clock and the slow 4H
            // bias. They should see ~4 bar closes (30M) / ~2 closes (1H) per trade,
            // which is a sweet spot: fast enough to react within the 4-hour time
            // stop, slow enough to filter 15M noise.
            Console.Write("  Building 30M series from 15M aggregation...  ");
            var tf30M = BuildTfSeries(candles15M, 30);
            Console.WriteLine($"{tf30M.Quotes.Count} bars");
            Console.Write("  Building 1H  series from 15M aggregation...  ");
            var tf1H  = BuildTfSeries(candles15M, 60);
            Console.WriteLine($"{tf1H.Quotes.Count} bars");

            // Index candles by CloseTime for fast lookup
            var byClose = new Dictionary<DateTime, int>(candles15M.Count);
            for (int i = 0; i < candles15M.Count; i++) byClose[candles15M[i].CloseTime] = i;

            var rows = new List<PostmortemRow>(losers.Count);

            foreach (var t in losers)
            {
                var row = new PostmortemRow
                {
                    Index = t.Index,
                    Direction = t.Direction,
                    EntryTime = t.EntryTime,
                    EntryPrice = t.EntryPrice,
                    ExitTime = t.ExitTime,
                    ExitPrice = t.ExitPrice,
                    ExitReason = t.ExitReason,
                    StopLoss = t.StopLoss,
                    TakeProfit = t.TakeProfit,
                    Atr = t.AtrAtSignal,
                    Pnl = t.PnlUsdt,
                    ProbabilityScore = t.ProbabilityScore
                };

                // Look at bars strictly AFTER ExitTime
                if (!byClose.TryGetValue(t.ExitTime, out int exitIdx))
                {
                    // Fall back: find first bar with CloseTime >= ExitTime
                    exitIdx = candles15M.FindIndex(c => c.CloseTime >= t.ExitTime);
                    if (exitIdx < 0) { rows.Add(row); continue; }
                }

                int start = exitIdx + 1;
                int end = Math.Min(start + LookaheadBars15M, candles15M.Count);
                if (start >= end) { rows.Add(row); continue; }

                // Compute forward-looking stats in direction of ORIGINAL trade
                decimal initialRisk = Math.Abs(t.EntryPrice - t.StopLoss);
                if (initialRisk <= 0) { rows.Add(row); continue; }

                decimal mfeAbsolute = 0m;   // best move back toward trade direction vs SL-exit price
                decimal maePastSl = 0m;     // worst continued move past SL (adverse vs entry)
                int barsToMfe = 0;
                bool tpTouched = false;
                int barsToTpTouch = -1;

                for (int i = start; i < end; i++)
                {
                    var c = candles15M[i];
                    int ahead = i - exitIdx;

                    if (t.Direction == TradeDirection.Long)
                    {
                        // Favorable = price going UP after stop-out
                        var favorable = c.High - t.ExitPrice;
                        if (favorable > mfeAbsolute) { mfeAbsolute = favorable; barsToMfe = ahead; }

                        // Continued adverse = price dropping further below SL
                        var adverse = t.StopLoss - c.Low;
                        if (adverse > maePastSl) maePastSl = adverse;

                        if (!tpTouched && c.High >= t.TakeProfit)
                        {
                            tpTouched = true;
                            barsToTpTouch = ahead;
                        }
                    }
                    else // Short
                    {
                        var favorable = t.ExitPrice - c.Low;
                        if (favorable > mfeAbsolute) { mfeAbsolute = favorable; barsToMfe = ahead; }

                        var adverse = c.High - t.StopLoss;
                        if (adverse > maePastSl) maePastSl = adverse;

                        if (!tpTouched && c.Low <= t.TakeProfit)
                        {
                            tpTouched = true;
                            barsToTpTouch = ahead;
                        }
                    }
                }

                row.MfeR = mfeAbsolute / initialRisk;
                row.MaePastSlR = maePastSl / initialRisk;
                row.MfeAtrs = t.AtrAtSignal > 0 ? mfeAbsolute / t.AtrAtSignal : 0;
                row.BarsToMfe = barsToMfe;
                row.TpTouched = tpTouched;
                row.BarsToTpTouch = barsToTpTouch;
                row.WouldRecoverToEntry = t.Direction == TradeDirection.Long
                    ? (t.ExitPrice + mfeAbsolute) >= t.EntryPrice
                    : (t.ExitPrice - mfeAbsolute) <= t.EntryPrice;

                // Classify: Recoverable (TP would have hit), Marginal (â‰¥1R recovery
                // but not TP), Pure (barely moved favorable).
                if (tpTouched) row.Classification = LossClass.Recoverable;
                else if (row.MfeR >= 1.0m) row.Classification = LossClass.Marginal;
                else row.Classification = LossClass.Pure;

                // ---- INTRA-TRADE FEATURES ----
                // Features visible while the trade is live (entry â†’ exit). These
                // are predictors we could wire into an early-exit or skip rule.
                ComputeIntraTradeFeatures(row, t, candles15M, byClose, initialRisk);
                ComputeHtfRsiFeatures(row, t, rsi4H);
                ComputeIntermediateTfFeatures(row, t, tf30M, tf1H);

                rows.Add(row);
            }

            // ---- Winner feature rows (for fakeout detection) ----
            // Run the same intra-trade / HTF / 30M / 1H feature computation on
            // winning trades so we can contrast Winner vs Loser signatures.
            var winnerRows = new List<PostmortemRow>(winners.Count);
            foreach (var t in winners)
            {
                var row = new PostmortemRow
                {
                    Index = t.Index,
                    Direction = t.Direction,
                    EntryTime = t.EntryTime,
                    EntryPrice = t.EntryPrice,
                    ExitTime = t.ExitTime,
                    ExitPrice = t.ExitPrice,
                    ExitReason = t.ExitReason,
                    StopLoss = t.StopLoss,
                    TakeProfit = t.TakeProfit,
                    Atr = t.AtrAtSignal,
                    Pnl = t.PnlUsdt,
                    ProbabilityScore = t.ProbabilityScore
                };
                decimal initialRisk = Math.Abs(t.EntryPrice - t.StopLoss);
                if (initialRisk > 0)
                {
                    ComputeIntraTradeFeatures(row, t, candles15M, byClose, initialRisk);
                    ComputeHtfRsiFeatures(row, t, rsi4H);
                    ComputeIntermediateTfFeatures(row, t, tf30M, tf1H);
                }
                winnerRows.Add(row);
            }

            // ---- Aggregate summary ----
            PrintLossClassification(rows);
            PrintByDirectionScoreMatrix(trades, rows);
            PrintIntraTradeFeatureComparison(rows);
            PrintWinnerStructureBreakRate(trades, candles15M, byClose, tf30M, tf1H);
            PrintEntryTimingAnalysis(rows, trades, candles15M);
            PrintFakeoutDetection(winnerRows, rows);
            PrintAggregates(rows);

            if (!string.IsNullOrWhiteSpace(outputCsv))
                WriteCsv(outputCsv, rows);

            return 0;
        }

        private static void PrintAggregates(List<PostmortemRow> rows)
        {
            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  AGGREGATE â€” were stops too tight?");
            Console.WriteLine("================================================================");

            int n = rows.Count;
            if (n == 0) return;

            var withMfe = rows.Where(r => r.MfeR > 0).ToList();
            Console.WriteLine($"  Trades analyzed:               {n}");
            Console.WriteLine($"  Total SL P&L:                  {rows.Sum(r => r.Pnl):F2}");
            Console.WriteLine();

            // MFE buckets â€” how far did price recover after we stopped out?
            int recoverHalfR = rows.Count(r => r.MfeR >= 0.5m);
            int recover1R    = rows.Count(r => r.MfeR >= 1.0m);
            int recover15R   = rows.Count(r => r.MfeR >= 1.5m);
            int recover2R    = rows.Count(r => r.MfeR >= 2.0m);
            int wouldHitTp   = rows.Count(r => r.TpTouched);

            Console.WriteLine("  After SL, price recovered (MFE in R measured from exit price):");
            Console.WriteLine($"    â‰¥ 0.5R  recovery:            {recoverHalfR,3}  ({(double)recoverHalfR/n:P1})");
            Console.WriteLine($"    â‰¥ 1.0R  recovery:            {recover1R,3}  ({(double)recover1R/n:P1})");
            Console.WriteLine($"    â‰¥ 1.5R  recovery:            {recover15R,3}  ({(double)recover15R/n:P1})");
            Console.WriteLine($"    â‰¥ 2.0R  recovery:            {recover2R,3}  ({(double)recover2R/n:P1})");
            Console.WriteLine($"    Original TP touched later:   {wouldHitTp,3}  ({(double)wouldHitTp/n:P1})");
            Console.WriteLine();

            if (withMfe.Count > 0)
            {
                Console.WriteLine($"  Avg MFE after SL:              {withMfe.Average(r => r.MfeR):F2} R  ({withMfe.Average(r => r.MfeAtrs):F2} ATRs)");
                Console.WriteLine($"  Median MFE after SL:           {Median(withMfe.Select(r => r.MfeR)):F2} R");
                Console.WriteLine($"  Avg bars to MFE:               {withMfe.Average(r => r.BarsToMfe):F1} Ã— 15M");
            }

            Console.WriteLine($"  Avg adverse-past-SL excursion: {rows.Average(r => r.MaePastSlR):F2} R (how far price went past SL before reversing)");
            Console.WriteLine();

            Console.WriteLine("  Interpretation:");
            Console.WriteLine("    - High % with TP touched later  â†’ SL too tight");
            Console.WriteLine("    - High avg MFE (â‰¥1R)            â†’ stops are premature");
            Console.WriteLine("    - High MAE past SL              â†’ SL triggered on real reversal, stop OK");
            Console.WriteLine("================================================================");

            // ---- Grouped by ProbabilityScore & Direction ----
            Console.WriteLine();
            Console.WriteLine("  By Direction:");
            foreach (var grp in rows.GroupBy(r => r.Direction))
            {
                var list = grp.ToList();
                int tp = list.Count(r => r.TpTouched);
                decimal avgMfe = list.Average(r => r.MfeR);
                Console.WriteLine($"    {grp.Key,-5} n={list.Count,3}  TP-touched-after-SL={tp,3} ({(double)tp/list.Count:P1})  AvgMFE={avgMfe:F2}R  SLPnL={list.Sum(r=>r.Pnl):F2}");
            }

            Console.WriteLine();
            Console.WriteLine("  By ProbabilityScore bucket:");
            foreach (var grp in rows
                         .GroupBy(r => ScoreBucket(r.ProbabilityScore))
                         .OrderBy(g => g.Key))
            {
                var list = grp.ToList();
                int tp = list.Count(r => r.TpTouched);
                decimal avgMfe = list.Average(r => r.MfeR);
                Console.WriteLine($"    {grp.Key,-8} n={list.Count,3}  TP-touched-after-SL={tp,3} ({(double)tp/list.Count:P1})  AvgMFE={avgMfe:F2}R  SLPnL={list.Sum(r=>r.Pnl):F2}");
            }
        }

        // ---- Intra-trade feature computation ----------------------------
        //
        // For each losing trade, walk the 15M candles from EntryTime â†’ ExitTime
        // and extract features that would have been visible while the trade
        // was live. These are the candidate signals for an early-exit rule or
        // a smarter entry filter.
        //
        // Features captured:
        //   FirstBarFavorable     = did the 15M bar after entry close in our favor?
        //   BarsToFirstAdverse    = # bars before first adverse close
        //   MfeDuringTradeR       = peak favorable move we saw before SL hit (in R)
        //   MfeBarIdxInTrade      = bar index at which MFE was reached
        //   AdverseAt1BarR        = price move vs entry after 1 bar (signed, favorable=+)
        //   AdverseAt2BarsR       = price move vs entry after 2 bars
        //   AdverseAt3BarsR       = price move vs entry after 3 bars
        //   HoldBars              = # 15M bars in trade
        //   ImpulsivenessRatio    = |exit - entry| / sum(|close_i - close_{i-1}|)
        //                           ~1.0 = straight line, lower = choppy
        private static void ComputeIntraTradeFeatures(
            PostmortemRow row,
            TradeRow t,
            List<ExchangeCandlestick> candles15M,
            Dictionary<DateTime, int> byClose,
            decimal initialRisk)
        {
            // Find first 15M bar strictly AFTER entry (entry may land on a boundary)
            int entryIdx = candles15M.FindIndex(c => c.CloseTime > t.EntryTime);
            int exitIdx = byClose.TryGetValue(t.ExitTime, out var idx)
                ? idx
                : candles15M.FindIndex(c => c.CloseTime >= t.ExitTime);
            if (entryIdx < 0 || exitIdx < 0 || entryIdx > exitIdx) return;

            int holdBars = exitIdx - entryIdx + 1;
            row.HoldBars = holdBars;

            bool isLong = t.Direction == TradeDirection.Long;
            decimal favorableDuringTrade = 0m;
            int mfeBarIdxInTrade = 0;
            int barsToFirstAdverseClose = -1;
            decimal pathLen = 0m;

            decimal prevClose = t.EntryPrice;
            for (int i = entryIdx; i <= exitIdx; i++)
            {
                var c = candles15M[i];
                int barNum = i - entryIdx + 1; // 1-based bar count since entry

                // MFE during the trade (in favorable direction, vs entry)
                decimal favorable = isLong
                    ? c.High - t.EntryPrice
                    : t.EntryPrice - c.Low;
                if (favorable > favorableDuringTrade)
                {
                    favorableDuringTrade = favorable;
                    mfeBarIdxInTrade = barNum;
                }

                // First adverse close
                bool closedAdverse = isLong ? c.Close < t.EntryPrice : c.Close > t.EntryPrice;
                if (closedAdverse && barsToFirstAdverseClose < 0)
                    barsToFirstAdverseClose = barNum;

                pathLen += Math.Abs(c.Close - prevClose);
                prevClose = c.Close;

                // Price change at specific bar indices (1,2,3) â€” signed, favorable=+
                decimal signedMove = isLong
                    ? (c.Close - t.EntryPrice)
                    : (t.EntryPrice - c.Close);
                if (barNum == 1) row.AdverseAt1BarR = signedMove / initialRisk;
                if (barNum == 2) row.AdverseAt2BarsR = signedMove / initialRisk;
                if (barNum == 3) row.AdverseAt3BarsR = signedMove / initialRisk;
            }

            // First bar favorable-close?
            var firstBar = candles15M[entryIdx];
            row.FirstBarFavorable = isLong
                ? firstBar.Close >= t.EntryPrice
                : firstBar.Close <= t.EntryPrice;

            row.MfeDuringTradeR = favorableDuringTrade / initialRisk;
            row.MfeBarIdxInTrade = mfeBarIdxInTrade;
            row.BarsToFirstAdverseClose = barsToFirstAdverseClose;

            decimal netMove = Math.Abs(t.ExitPrice - t.EntryPrice);
            row.ImpulsivenessRatio = pathLen > 0 ? netMove / pathLen : 0;
        }

        // ---- HTF (4H) RSI features during the trade ---------------------
        //
        // The main algorithm uses 4H RSI to pick direction at entry. But does the
        // 4H bias STAY in our favor while the trade is live, or does it flip?
        //
        // For each losing trade, we sample the 4H RSI at:
        //   - The last completed 4H bar at/before EntryTime  (entry bias)
        //   - The last completed 4H bar at/before ExitTime   (exit bias)
        //   - The most extreme 4H RSI against us during the holding period
        //
        // Features then compared between Pure and Recoverable losses.
        private static void ComputeHtfRsiFeatures(
            PostmortemRow row,
            TradeRow t,
            List<RsiResult> rsi4H)
        {
            // Find the RSI reading for the last 4H bar that closed strictly BEFORE
            // each anchor time â€” that's what the live algorithm would have seen.
            double? EntryRsi = RsiAtOrBefore(rsi4H, t.EntryTime);
            double? ExitRsi  = RsiAtOrBefore(rsi4H, t.ExitTime);

            // During-trade: find max / min of RSI readings whose 4H bar closed
            // within the open interval (EntryTime, ExitTime].
            double? minDuring = null, maxDuring = null;
            int barsWith4HClose = 0;
            foreach (var r in rsi4H)
            {
                if (r.Timestamp <= t.EntryTime) continue;
                if (r.Timestamp > t.ExitTime) break;
                if (!r.Rsi.HasValue) continue;
                barsWith4HClose++;
                if (!minDuring.HasValue || r.Rsi < minDuring) minDuring = r.Rsi;
                if (!maxDuring.HasValue || r.Rsi > maxDuring) maxDuring = r.Rsi;
            }

            row.HtfRsiAtEntryCompleted = EntryRsi ?? double.NaN;
            row.HtfRsiAtExitCompleted  = ExitRsi  ?? double.NaN;
            row.HtfRsiChangeInTrade    = (EntryRsi.HasValue && ExitRsi.HasValue)
                                            ? (ExitRsi.Value - EntryRsi.Value)
                                            : 0.0;
            row.BarsWith4HCloseInTrade = barsWith4HClose;

            // Signed "adverse RSI move": how far the 4H RSI moved against our
            // direction at worst during the trade (vs entry).
            if (EntryRsi.HasValue)
            {
                double adverseExtreme;
                if (t.Direction == TradeDirection.Long)
                    adverseExtreme = (minDuring ?? EntryRsi.Value) - EntryRsi.Value; // negative = bad
                else
                    adverseExtreme = EntryRsi.Value - (maxDuring ?? EntryRsi.Value); // negative = bad
                row.HtfRsiAdverseExtreme = adverseExtreme;
            }

            // Did the 4H RSI cross neutral (50) against us at any point during the trade?
            if (EntryRsi.HasValue)
            {
                row.HtfRsiCrossedNeutral = t.Direction == TradeDirection.Long
                    ? (minDuring.HasValue && minDuring.Value < 50)
                    : (maxDuring.HasValue && maxDuring.Value > 50);
            }
        }

        // ---- Entry-timing analysis ---------------------------------------
        //
        // For each historical trade, re-simulate entry with a deeper pullback
        // limit: (signalPrice âˆ’ X Ã— InitialRisk) for longs, the mirror for
        // shorts. Within the 15M bar after signalTime, fill at the limit if
        // the bar's low (long) / high (short) touches it. If the limit never
        // hits: skip the trade entirely (no market-fill fallback â€” that's
        // the whole point of requiring a deeper pullback).
        //
        // After fill, rebase SL and TP to the new entry price (same 1.5Ã—ATR
        // distance, same 1:1 R:R). Walk the next 15M bars, exiting on the
        // first close that breaches SL or TP (matching TradeSimulator logic).
        //
        // Report per-fraction:
        //   RECOVERABLES: how many we'd SAVE (either no-fill OR fill+TP)
        //     vs still-SL-at-deeper-entry (wasted).
        //   WINNERS:      how many we'd MISS (no-fill) vs still-win.
        //   PURE:         how many we'd skip (no-fill) â€” pure save.
        //
        // The useful fraction is the one that saves the most recoverables
        // and pures without missing too many winners.
        private static void PrintEntryTimingAnalysis(
            List<PostmortemRow> loserRows,
            List<TradeRow> trades,
            List<ExchangeCandlestick> candles15M)
        {
            var recoverables = loserRows.Where(r => r.Classification == LossClass.Recoverable).ToList();
            var pures        = loserRows.Where(r => r.Classification == LossClass.Pure).ToList();
            var marginals    = loserRows.Where(r => r.Classification == LossClass.Marginal).ToList();

            // Map index â†’ TradeRow for losers so we can re-derive signalTime/price.
            var tradeByIdx = trades.ToDictionary(t => t.Index);

            var winners = trades.Where(t => t.PnlUsdt > 0 && !t.ExitReason.StartsWith("EntryCancelled")).ToList();
            if (winners.Count == 0 || recoverables.Count == 0) return;

            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  ENTRY TIMING â€” would deeper pullback save Recoverables?");
            Console.WriteLine("================================================================");
            Console.WriteLine($"  n Winners:       {winners.Count}   P&L {winners.Sum(w => w.PnlUsdt),16:F2}");
            Console.WriteLine($"  n Pure loss:     {pures.Count}   P&L {pures.Sum(r => r.Pnl),16:F2}");
            Console.WriteLine($"  n Marginal loss: {marginals.Count}   P&L {marginals.Sum(r => r.Pnl),16:F2}");
            Console.WriteLine($"  n Recoverable:   {recoverables.Count}   P&L {recoverables.Sum(r => r.Pnl),16:F2}");
            Console.WriteLine();

            // ---- Distribution: max adverse excursion from signalPrice ----
            // How deep (in R) did price go against signalPrice before reversing?
            // If Recoverables dip deeper than Winners, a pullback entry has edge.
            Console.WriteLine("  MAX ADVERSE EXCURSION FROM SIGNAL PRICE (in R, during trade life)");
            Console.WriteLine("  â€” how far price went against us before the eventual exit/recovery");
            Console.WriteLine();
            Console.WriteLine($"    {"Group",-14}  {"n",4}  {"avg",6}  {"med",6}  {">0.10R",7}  {">0.25R",7}  {">0.50R",7}  {">1.00R",7}");
            Console.WriteLine($"    {new string('-', 72)}");
            PrintAdverseDist("Winners",     winners.Select(t => MaxAdverseR(t, candles15M)));
            PrintAdverseDist("Pure",        pures.Select(r => MaxAdverseR(tradeByIdx[r.Index], candles15M)));
            PrintAdverseDist("Marginal",    marginals.Select(r => MaxAdverseR(tradeByIdx[r.Index], candles15M)));
            PrintAdverseDist("Recoverable", recoverables.Select(r => MaxAdverseR(tradeByIdx[r.Index], candles15M)));
            Console.WriteLine();

            // ---- Per-pullback-fraction simulation ----
            Console.WriteLine("  SIMULATION: limit-only entry at signalPrice âˆ’ X Ã— InitialRisk");
            Console.WriteLine("  Outcome counts if we'd used pullback fraction X and skipped any trade that");
            Console.WriteLine("  didn't fill within one 15M bar.");
            Console.WriteLine();
            Console.WriteLine($"    {"Pullback",-8}  {"Group",-12}  {"NoFill",7}  {"NewSL",7}  {"NewTP",7}  {"TimeSt",7}");
            Console.WriteLine($"    {new string('-', 60)}");

            decimal[] fractions = { 0.10m, 0.25m, 0.50m, 0.75m, 1.00m };
            foreach (var frac in fractions)
            {
                PrintSimRow(frac, "Winners",     winners,                       candles15M);
                PrintSimRow(frac, "Pure",        pures.Select(r => tradeByIdx[r.Index]).ToList(), candles15M);
                PrintSimRow(frac, "Recoverable", recoverables.Select(r => tradeByIdx[r.Index]).ToList(), candles15M);
                Console.WriteLine();
            }

            Console.WriteLine("  Interpretation:");
            Console.WriteLine("    - High Winners NoFill at X = we're skipping real winners.");
            Console.WriteLine("    - High Recoverable NoFill + NewTP = saves (avoided the original SL).");
            Console.WriteLine("    - High Recoverable NewSL at X = deeper pullback still didn't help.");
        }

        private static decimal MaxAdverseR(TradeRow t, List<ExchangeCandlestick> candles15M)
        {
            if (t.AtrAtSignal <= 0) return 0m;
            decimal initialRisk = t.AtrAtSignal * 1.5m;
            if (initialRisk <= 0) return 0m;

            // From first 15M bar strictly AFTER signalTime, through ExitTime.
            int start = candles15M.FindIndex(c => c.CloseTime > t.SignalTime);
            if (start < 0) return 0m;

            decimal maxAdv = 0m;
            for (int i = start; i < candles15M.Count; i++)
            {
                var c = candles15M[i];
                if (c.CloseTime > t.ExitTime) break;
                decimal adv = t.Direction == TradeDirection.Long
                    ? t.SignalPrice - c.Low
                    : c.High - t.SignalPrice;
                if (adv > maxAdv) maxAdv = adv;
            }
            return maxAdv / initialRisk;
        }

        private static void PrintAdverseDist(string label, IEnumerable<decimal> values)
        {
            var list = values.Where(v => v >= 0).OrderBy(v => v).ToList();
            if (list.Count == 0) { Console.WriteLine($"    {label,-14}  {0,4}"); return; }
            decimal avg = list.Average();
            decimal med = list.Count % 2 == 1 ? list[list.Count / 2] : (list[list.Count / 2 - 1] + list[list.Count / 2]) / 2;
            int gt10 = list.Count(v => v > 0.10m);
            int gt25 = list.Count(v => v > 0.25m);
            int gt50 = list.Count(v => v > 0.50m);
            int gt100 = list.Count(v => v > 1.00m);
            double n = list.Count;
            Console.WriteLine(
                $"    {label,-14}  {list.Count,4}  {avg,6:F2}  {med,6:F2}  " +
                $"{gt10/n,7:P0}  {gt25/n,7:P0}  {gt50/n,7:P0}  {gt100/n,7:P0}");
        }

        private enum SimOutcome { NoFill, StopLoss, TakeProfit, TimeStop }

        private static SimOutcome SimulateEntry(
            TradeRow t, decimal pullbackFrac, List<ExchangeCandlestick> candles15M)
        {
            if (t.AtrAtSignal <= 0) return SimOutcome.NoFill;
            decimal initialRisk = t.AtrAtSignal * 1.5m;
            if (initialRisk <= 0) return SimOutcome.NoFill;

            // First 15M bar strictly AFTER signalTime â€” this is our entry window.
            int startIdx = candles15M.FindIndex(c => c.CloseTime > t.SignalTime);
            if (startIdx < 0) return SimOutcome.NoFill;

            decimal limit = t.Direction == TradeDirection.Long
                ? t.SignalPrice - initialRisk * pullbackFrac
                : t.SignalPrice + initialRisk * pullbackFrac;

            var first = candles15M[startIdx];
            // Limit fills if the first 15M bar's range touches the limit.
            bool fill = t.Direction == TradeDirection.Long
                ? first.Low <= limit
                : first.High >= limit;
            if (!fill) return SimOutcome.NoFill;

            decimal entry = limit;
            decimal newSl = t.Direction == TradeDirection.Long
                ? entry - initialRisk
                : entry + initialRisk;
            decimal newTp = t.Direction == TradeDirection.Long
                ? entry + initialRisk
                : entry - initialRisk;

            // Same-bar exit check on first bar's close (SL priority).
            if (CheckClose(t.Direction, first.Close, newSl, newTp) is SimOutcome o0 && o0 != SimOutcome.NoFill)
                return o0;

            // Walk up to 15 more 15M bars (16 total = 4h time stop).
            int end = Math.Min(startIdx + 16, candles15M.Count);
            for (int i = startIdx + 1; i < end; i++)
            {
                var c = candles15M[i];
                var o = CheckClose(t.Direction, c.Close, newSl, newTp);
                if (o != SimOutcome.NoFill) return o;
            }
            return SimOutcome.TimeStop;
        }

        // SL takes priority over TP when evaluating a single 15M close
        // (same order as TradeSimulator.CheckExit).
        private static SimOutcome CheckClose(TradeDirection dir, decimal close, decimal sl, decimal tp)
        {
            if (dir == TradeDirection.Long)
            {
                if (close <= sl) return SimOutcome.StopLoss;
                if (close >= tp) return SimOutcome.TakeProfit;
            }
            else
            {
                if (close >= sl) return SimOutcome.StopLoss;
                if (close <= tp) return SimOutcome.TakeProfit;
            }
            return SimOutcome.NoFill; // using as "still open" sentinel
        }

        private static void PrintSimRow(decimal frac, string label, List<TradeRow> group, List<ExchangeCandlestick> candles15M)
        {
            int nf = 0, sl = 0, tp = 0, ts = 0;
            foreach (var t in group)
            {
                var o = SimulateEntry(t, frac, candles15M);
                if (o == SimOutcome.NoFill) nf++;
                else if (o == SimOutcome.StopLoss) sl++;
                else if (o == SimOutcome.TakeProfit) tp++;
                else ts++;
            }
            int n = group.Count;
            string ShowPct(int x) => n == 0 ? "  0 (  -)" : $"{x,3} ({(double)x / n,3:P0})";
            Console.WriteLine($"    {frac:F2}R    {label,-12}  {ShowPct(nf),7}  {ShowPct(sl),7}  {ShowPct(tp),7}  {ShowPct(ts),7}");
        }

        // ---- Winners: do they break structure too? ------------------------
        //
        // Critical cross-check. The Pure-vs-Recoverable comparison only looks
        // at losing trades. An exit rule based on structure-break also cuts
        // WINNERS if they dip below the entry bar's low en route to TP.
        // This method computes Structure30mBroken / Structure1hBroken rates
        // on the winning trades so we can estimate the true false-positive
        // cost: how many winners the rule would have killed.
        private static void PrintWinnerStructureBreakRate(
            List<TradeRow> trades,
            List<ExchangeCandlestick> candles15M,
            Dictionary<DateTime, int> byClose,
            TfSeries tf30M,
            TfSeries tf1H)
        {
            var winners = trades.Where(t => t.PnlUsdt > 0 && !t.ExitReason.StartsWith("EntryCancelled")).ToList();
            if (winners.Count == 0) return;

            int structure30 = 0, structure1h = 0;
            int rsi30Cross = 0, ema30Cross = 0;
            int combo30 = 0; // structure AND rsiAdverse>=5

            var tmp = new PostmortemRow();
            foreach (var w in winners)
            {
                tmp.Rsi30mAtEntry = 0; tmp.Rsi30mAdverseExtreme = 0;
                tmp.Structure30mBroken = false; tmp.Ema30mCrossedAgainst = false; tmp.Rsi30mCrossedNeutral = false;
                tmp.Structure1hBroken = false;

                FillTf(tmp, w, tf30M, isThirty: true);
                FillTf(tmp, w, tf1H,  isThirty: false);

                if (tmp.Structure30mBroken) structure30++;
                if (tmp.Structure1hBroken)  structure1h++;
                if (tmp.Rsi30mCrossedNeutral) rsi30Cross++;
                if (tmp.Ema30mCrossedAgainst) ema30Cross++;
                if (tmp.Structure30mBroken && tmp.Rsi30mAdverseExtreme <= -5.0) combo30++;
            }

            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  CROSS-CHECK: Would the rule have cut WINNERS too?");
            Console.WriteLine("================================================================");
            Console.WriteLine($"  n Winners: {winners.Count}");
            Console.WriteLine();
            Console.WriteLine("  Rate at which each candidate rule fires on winning trades â€”");
            Console.WriteLine("  high rate means the rule would kill winners if used as early exit.");
            Console.WriteLine();
            Console.WriteLine($"    30m Structure broken                  {(double)structure30 / winners.Count,7:P1}");
            Console.WriteLine($"    1h Structure broken                   {(double)structure1h / winners.Count,7:P1}");
            Console.WriteLine($"    30m RSI crossed neutral               {(double)rsi30Cross / winners.Count,7:P1}");
            Console.WriteLine($"    30m EMA crossed against us            {(double)ema30Cross / winners.Count,7:P1}");
            Console.WriteLine($"    30m Structure + RSI adverse â‰¥ 5pts    {(double)combo30 / winners.Count,7:P1}");
        }

        // ---- Fakeout detection: Winners vs Losers signature --------------
        //
        // Hypothesis: fakeouts are losing vol-expansion signals where price
        // reverses quickly instead of following through. Real moves are
        // winning trades where price keeps going our direction.
        //
        // If the two are distinguishable in the first 1-3 15M bars (or already
        // distinguishable from state-at-entry like 30M/1H RSI/EMA) then we
        // have a real early-exit or pre-entry-filter handle.
        //
        // For each candidate feature this prints Winner vs Loser distributions,
        // then tests a set of yes/no rules with:
        //   WinCut / WinSkip   = fraction of winners the rule would kill
        //   LossSav / LossSkip = fraction of losers the rule would save
        //   Pure / Rec         = how the saved losers split by class
        //   ExpP&L             = rough expected P&L if the rule were live
        // The rule that matters is one with high LossSav and low WinCut.
        private static void PrintFakeoutDetection(
            List<PostmortemRow> winnerRows,
            List<PostmortemRow> loserRows)
        {
            if (winnerRows.Count == 0 || loserRows.Count == 0) return;

            var pures = loserRows.Where(r => r.Classification == LossClass.Pure).ToList();
            var margs = loserRows.Where(r => r.Classification == LossClass.Marginal).ToList();
            var recs  = loserRows.Where(r => r.Classification == LossClass.Recoverable).ToList();

            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  FAKEOUT DETECTION â€” Winners vs Losers (intra-trade signature)");
            Console.WriteLine("================================================================");
            Console.WriteLine("  Fakeout = losing trade where price reversed fast.");
            Console.WriteLine("  Real move = winning trade where price followed through.");
            Console.WriteLine("  Question: can we tell them apart at entry or in the first 1-3 bars?");
            Console.WriteLine();
            Console.WriteLine($"  n Winners: {winnerRows.Count}   n Losers: {loserRows.Count}  " +
                              $"(Pure={pures.Count} Marg={margs.Count} Rec={recs.Count})");
            Console.WriteLine();

            void Row4(string name, Func<PostmortemRow, decimal> f, string fmt = "F2")
            {
                decimal wAvg = winnerRows.Average(f);
                decimal lAvg = loserRows.Average(f);
                decimal wMed = Median(winnerRows.Select(f));
                decimal lMed = Median(loserRows.Select(f));
                Console.WriteLine(
                    $"    {name,-28}  Win avg {wAvg.ToString(fmt),7} med {wMed.ToString(fmt),7}   " +
                    $"|   Loss avg {lAvg.ToString(fmt),7} med {lMed.ToString(fmt),7}   " +
                    $"|   Î” {(lAvg - wAvg).ToString(fmt)}");
            }
            // Double variant: filters out NaN (RSI warmup gaps, etc.).
            void RowD(string name, Func<PostmortemRow, double> f, string fmt = "F2")
            {
                var wVals = winnerRows.Select(f).Where(v => !double.IsNaN(v)).ToList();
                var lVals = loserRows.Select(f).Where(v => !double.IsNaN(v)).ToList();
                if (wVals.Count == 0 || lVals.Count == 0) return;
                double wAvg = wVals.Average();
                double lAvg = lVals.Average();
                double wMed = MedianD(wVals);
                double lMed = MedianD(lVals);
                Console.WriteLine(
                    $"    {name,-28}  Win avg {wAvg.ToString(fmt),7} med {wMed.ToString(fmt),7}   " +
                    $"|   Loss avg {lAvg.ToString(fmt),7} med {lMed.ToString(fmt),7}   " +
                    $"|   Î” {(lAvg - wAvg).ToString(fmt)}  (nW={wVals.Count} nL={lVals.Count})");
            }
            void BoolRow4(string name, Func<PostmortemRow, bool> f)
            {
                double wRate = (double)winnerRows.Count(f) / winnerRows.Count;
                double lRate = (double)loserRows.Count(f)  / loserRows.Count;
                Console.WriteLine(
                    $"    {name,-28}  Win {wRate,7:P1}                  " +
                    $"|   Loss {lRate,7:P1}                  " +
                    $"|   Î” {(lRate - wRate),+7:P1}");
            }

            Console.WriteLine("  EARLY PRICE ACTION (first 1-3 bars after entry):");
            BoolRow4("FirstBarFavorableClose",       r => r.FirstBarFavorable);
            Row4("MoveAtBar1 (R, +=favor)",          r => r.AdverseAt1BarR, "F2");
            Row4("MoveAtBar2 (R)",                   r => r.AdverseAt2BarsR, "F2");
            Row4("MoveAtBar3 (R)",                   r => r.AdverseAt3BarsR, "F2");
            Row4("MfeDuringTrade (R)",               r => r.MfeDuringTradeR, "F2");
            Row4("BarsToFirstAdverseClose",          r => r.BarsToFirstAdverseClose, "F1");
            Row4("ImpulsivenessRatio",               r => r.ImpulsivenessRatio, "F3");
            Console.WriteLine();

            Console.WriteLine("  30M / 1H STATE AT ENTRY (visible BEFORE trade opens):");
            RowD("30mRsiAtEntry",                    r => r.Rsi30mAtEntry, "F1");
            BoolRow4("30mEmaBullishAtEntry",         r => r.Ema30mBullishAtEntry);
            RowD("1hRsiAtEntry",                     r => r.Rsi1hAtEntry, "F1");
            BoolRow4("1hEmaBullishAtEntry",          r => r.Ema1hBullishAtEntry);
            Console.WriteLine();

            // Average P&L per outcome for expected-value math.
            decimal avgWin  = winnerRows.Average(r => r.Pnl);
            decimal avgLoss = loserRows.Average(r => r.Pnl); // negative

            // ---- Early-exit rules (fire DURING the trade) ----
            Console.WriteLine("  EARLY-EXIT RULES (fire after entry â€” cut the trade early)");
            Console.WriteLine("  Assumes we'd bail at ~50% of a full stop-loss (1R â†’ ~0.5R realized).");
            Console.WriteLine();
            Console.WriteLine($"    {"Rule",-42}  {"WinCut",8}  {"LossSav",8}  {"Pure",5}  {"Rec",5}  {"ExpP&L",14}");
            Console.WriteLine($"    {new string('-', 96)}");

            void ExitRule(string name, Func<PostmortemRow, bool> trigger)
            {
                int w = winnerRows.Count(trigger);
                int l = loserRows.Count(trigger);
                int p = pures.Count(trigger);
                int rc = recs.Count(trigger);
                double wRate = (double)w / winnerRows.Count;
                double lRate = (double)l / loserRows.Count;
                // Rough P&L: save ~Â½ of a loss per loser cut, give up a full win per winner cut.
                decimal saved = l * Math.Abs(avgLoss) * 0.5m;
                decimal cost  = w * avgWin;
                decimal exp   = saved - cost;
                Console.WriteLine(
                    $"    {name,-42}  {wRate,7:P1}   {lRate,7:P1}   {p,4}   {rc,4}   {exp,14:F0}");
            }

            ExitRule("Bar1 close adverse",                    r => !r.FirstBarFavorable);
            ExitRule("Bar1 adverse > 0.25R",                  r => r.AdverseAt1BarR <= -0.25m);
            ExitRule("Bar1 adverse > 0.50R",                  r => r.AdverseAt1BarR <= -0.50m);
            ExitRule("Bar1 adverse > 0.75R",                  r => r.AdverseAt1BarR <= -0.75m);
            ExitRule("Bar2 adverse > 0.50R",                  r => r.AdverseAt2BarsR <= -0.50m);
            ExitRule("Bar2 adverse > 0.75R",                  r => r.AdverseAt2BarsR <= -0.75m);
            ExitRule("Bar1 adv AND MFE<0.25R (through bar2)", r => !r.FirstBarFavorable && r.MfeDuringTradeR < 0.25m);
            ExitRule("Bar1 adv AND Bar2 adv>0.50R",           r => !r.FirstBarFavorable && r.AdverseAt2BarsR <= -0.50m);
            ExitRule("Bar1 adv AND 30mEma against dir",
                r => !r.FirstBarFavorable && (r.Direction == TradeDirection.Long ? !r.Ema30mBullishAtEntry : r.Ema30mBullishAtEntry));
            Console.WriteLine();

            // ---- Pre-entry filter rules (skip the signal entirely) ----
            Console.WriteLine("  PRE-ENTRY FILTER RULES (would have SKIPPED the signal)");
            Console.WriteLine("  Uses only state at/before entry. Skipping avoids full loss AND full win.");
            Console.WriteLine();
            Console.WriteLine($"    {"Rule",-42}  {"WinSkp",8}  {"LossSkp",8}  {"Pure",5}  {"Rec",5}  {"ExpP&L",14}");
            Console.WriteLine($"    {new string('-', 96)}");

            void SkipRule(string name, Func<PostmortemRow, bool> skip)
            {
                int w = winnerRows.Count(skip);
                int l = loserRows.Count(skip);
                int p = pures.Count(skip);
                int rc = recs.Count(skip);
                double wRate = (double)w / winnerRows.Count;
                double lRate = (double)l / loserRows.Count;
                decimal avoided = l * Math.Abs(avgLoss);  // full loss avoided
                decimal forgone = w * avgWin;             // full win forgone
                decimal exp = avoided - forgone;
                Console.WriteLine(
                    $"    {name,-42}  {wRate,7:P1}   {lRate,7:P1}   {p,4}   {rc,4}   {exp,14:F0}");
            }

            // For LONGs, EMA bullish = favor; for SHORTs, EMA bearish = favor.
            bool EmaAgainst30m(PostmortemRow r) =>
                r.Direction == TradeDirection.Long ? !r.Ema30mBullishAtEntry : r.Ema30mBullishAtEntry;
            bool EmaAgainst1h(PostmortemRow r) =>
                r.Direction == TradeDirection.Long ? !r.Ema1hBullishAtEntry : r.Ema1hBullishAtEntry;

            SkipRule("Skip if 30m EMA against direction",    EmaAgainst30m);
            SkipRule("Skip if 1h EMA against direction",     EmaAgainst1h);
            SkipRule("Skip if 30m AND 1h EMA both against",  r => EmaAgainst30m(r) && EmaAgainst1h(r));
            SkipRule("Skip if 30m OR 1h EMA against",        r => EmaAgainst30m(r) || EmaAgainst1h(r));
            SkipRule("Skip if 30mRsi overbought(L>70)/ovs(S<30)",
                r => r.Direction == TradeDirection.Long ? r.Rsi30mAtEntry > 70 : r.Rsi30mAtEntry < 30);
            SkipRule("Skip if 30mRsi in extended zone (>75/<25)",
                r => r.Direction == TradeDirection.Long ? r.Rsi30mAtEntry > 75 : r.Rsi30mAtEntry < 25);
            SkipRule("Skip if 1hRsi overbought(L>70)/ovs(S<30)",
                r => r.Direction == TradeDirection.Long ? r.Rsi1hAtEntry > 70 : r.Rsi1hAtEntry < 30);
            SkipRule("Skip if ProbabilityScore < 40",        r => r.ProbabilityScore < 40);
            SkipRule("Skip if ProbabilityScore < 60",        r => r.ProbabilityScore < 60);
            Console.WriteLine();
            Console.WriteLine("  Interpretation:");
            Console.WriteLine("    - WinCut/WinSkp% = fraction of winners the rule sacrifices (cost)");
            Console.WriteLine("    - LossSav/LossSkp% = fraction of losers the rule prevents (gain)");
            Console.WriteLine("    - ExpP&L: rough, IGNORES compounding. Positive = plausibly worth testing live.");
            Console.WriteLine("    - A real winner-vs-loser divergence shows as a BIG LossSav%");
            Console.WriteLine("      paired with a SMALL WinCut% on the same rule.");
        }

        // ---- Intermediate TF (30M / 1H) aggregation + indicators ----------
        //
        // Fold consecutive 15M candles (on wall-clock boundaries) into higher-TF
        // OHLCV, then compute Wilder RSI(14) and EMA(8/21) on the aggregated
        // series. 15M data already has CloseTime.Minute divisible by 15 in this
        // codebase, so % minutes == 0 cleanly identifies bucket closes.
        private sealed class TfSeries
        {
            public int Minutes;
            public List<IQuote> Quotes;
            public List<RsiResult> Rsi;
            public List<EmaResult> Ema8;
            public List<EmaResult> Ema21;
        }

        private static TfSeries BuildTfSeries(List<ExchangeCandlestick> src15M, int minutes)
        {
            var quotes = new List<IQuote>();
            decimal aggOpen = 0, aggHigh = 0, aggLow = 0, aggClose = 0, aggVol = 0;
            DateTime aggCloseTime = default;
            bool started = false;

            foreach (var c in src15M)
            {
                if (!started)
                {
                    // Wait for wall-clock alignment (e.g. 30M starts at :00/:30).
                    if (c.OpenTime.Minute % minutes != 0) continue;
                    aggOpen = c.Open; aggHigh = c.High; aggLow = c.Low;
                    aggClose = c.Close; aggVol = c.Volume;
                    aggCloseTime = c.CloseTime;
                    started = true;
                }
                else
                {
                    if (c.High > aggHigh) aggHigh = c.High;
                    if (c.Low < aggLow) aggLow = c.Low;
                    aggClose = c.Close;
                    aggVol += c.Volume;
                    aggCloseTime = c.CloseTime;
                }

                if (c.CloseTime.Minute % minutes == 0)
                {
                    quotes.Add(new Quote
                    {
                        Timestamp = aggCloseTime,
                        Open = aggOpen,
                        High = aggHigh,
                        Low = aggLow,
                        Close = aggClose,
                        Volume = aggVol
                    });
                    started = false;
                }
            }

            return new TfSeries
            {
                Minutes = minutes,
                Quotes = quotes,
                Rsi = quotes.ToRsi(14).ToList(),
                Ema8 = quotes.ToEma(8).ToList(),
                Ema21 = quotes.ToEma(21).ToList()
            };
        }

        // ---- Intermediate TF features during the trade --------------------
        //
        // For each losing trade and each TF (30M, 1H), capture:
        //   - RSI at entry (last completed bar before EntryTime)
        //   - Worst adverse RSI move during trade (signed; negative=bad)
        //   - Exit RSI change vs entry
        //   - Whether RSI crossed neutral (50) against us
        //   - EMA(8) vs EMA(21) state at entry; did they cross against us?
        //   - Did any in-trade bar close break the entry bar's low (long) /
        //     high (short)? â€” simple structure break.
        //
        // Bar closes count tells us how useful each TF is: a signal that never
        // fires because no bars close during the trade is dead weight.
        private static void ComputeIntermediateTfFeatures(
            PostmortemRow row,
            TradeRow t,
            TfSeries tf30M,
            TfSeries tf1H)
        {
            FillTf(row, t, tf30M, isThirty: true);
            FillTf(row, t, tf1H,  isThirty: false);
        }

        private static void FillTf(PostmortemRow row, TradeRow t, TfSeries tf, bool isThirty)
        {
            bool isLong = t.Direction == TradeDirection.Long;

            // Entry-time readings: last completed bar at/before EntryTime.
            int entryIdx = LastIdxAtOrBefore(tf.Quotes, t.EntryTime);
            double? rsiEntry = entryIdx >= 0 ? tf.Rsi[entryIdx].Rsi : null;
            double? ema8Entry = entryIdx >= 0 ? tf.Ema8[entryIdx].Ema : null;
            double? ema21Entry = entryIdx >= 0 ? tf.Ema21[entryIdx].Ema : null;
            decimal? entryBarLow = entryIdx >= 0 ? (decimal?)tf.Quotes[entryIdx].Low : null;
            decimal? entryBarHigh = entryIdx >= 0 ? (decimal?)tf.Quotes[entryIdx].High : null;

            // In-trade bars: closed strictly after EntryTime, at or before ExitTime.
            double? rsiMin = null, rsiMax = null, rsiLast = null;
            int barsIn = 0;
            bool emaCrossAgainst = false;
            bool structureBroken = false;
            // Track EMA state bar-by-bar to detect a cross against direction.
            bool prevBullish = (ema8Entry.HasValue && ema21Entry.HasValue) && ema8Entry.Value > ema21Entry.Value;

            for (int i = Math.Max(0, entryIdx + 1); i < tf.Quotes.Count; i++)
            {
                var q = tf.Quotes[i];
                if (q.Timestamp <= t.EntryTime) continue;
                if (q.Timestamp > t.ExitTime) break;
                barsIn++;

                // RSI tracking
                var rsi = tf.Rsi[i].Rsi;
                if (rsi.HasValue)
                {
                    rsiLast = rsi;
                    if (!rsiMin.HasValue || rsi < rsiMin) rsiMin = rsi;
                    if (!rsiMax.HasValue || rsi > rsiMax) rsiMax = rsi;
                }

                // EMA crossover detection
                var e8 = tf.Ema8[i].Ema;
                var e21 = tf.Ema21[i].Ema;
                if (e8.HasValue && e21.HasValue)
                {
                    bool bullish = e8.Value > e21.Value;
                    // For a LONG, adverse = bullishâ†’bearish (e8 falls below e21).
                    // For a SHORT, adverse = bearishâ†’bullish.
                    if (isLong && prevBullish && !bullish) emaCrossAgainst = true;
                    if (!isLong && !prevBullish && bullish) emaCrossAgainst = true;
                    prevBullish = bullish;
                }

                // Structure break: close beyond the entry-bar's adverse extreme.
                if (isLong && entryBarLow.HasValue && (decimal)q.Close < entryBarLow.Value)
                    structureBroken = true;
                if (!isLong && entryBarHigh.HasValue && (decimal)q.Close > entryBarHigh.Value)
                    structureBroken = true;
            }

            double entryR = rsiEntry ?? double.NaN;
            double exitR  = rsiLast ?? entryR;
            double change = (!double.IsNaN(entryR) && !double.IsNaN(exitR)) ? exitR - entryR : 0.0;

            // Adverse RSI extreme (signed, negative = bad).
            double adverse = 0.0;
            if (rsiEntry.HasValue)
            {
                adverse = isLong
                    ? (rsiMin ?? rsiEntry.Value) - rsiEntry.Value
                    : rsiEntry.Value - (rsiMax ?? rsiEntry.Value);
            }
            bool crossedNeutral = rsiEntry.HasValue &&
                (isLong
                    ? (rsiMin.HasValue && rsiMin.Value < 50)
                    : (rsiMax.HasValue && rsiMax.Value > 50));

            bool emaBullishAtEntry = ema8Entry.HasValue && ema21Entry.HasValue &&
                                     ema8Entry.Value > ema21Entry.Value;

            if (isThirty)
            {
                row.Rsi30mAtEntry = entryR;
                row.Rsi30mAtExit = exitR;
                row.Rsi30mChange = change;
                row.Rsi30mAdverseExtreme = adverse;
                row.Rsi30mCrossedNeutral = crossedNeutral;
                row.Bars30mInTrade = barsIn;
                row.Ema30mBullishAtEntry = emaBullishAtEntry;
                row.Ema30mCrossedAgainst = emaCrossAgainst;
                row.Structure30mBroken = structureBroken;
            }
            else
            {
                row.Rsi1hAtEntry = entryR;
                row.Rsi1hAtExit = exitR;
                row.Rsi1hChange = change;
                row.Rsi1hAdverseExtreme = adverse;
                row.Rsi1hCrossedNeutral = crossedNeutral;
                row.Bars1hInTrade = barsIn;
                row.Ema1hBullishAtEntry = emaBullishAtEntry;
                row.Ema1hCrossedAgainst = emaCrossAgainst;
                row.Structure1hBroken = structureBroken;
            }
        }

        private static int LastIdxAtOrBefore(List<IQuote> quotes, DateTime t)
        {
            int lo = 0, hi = quotes.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (quotes[mid].Timestamp <= t) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }

        private static double? RsiAtOrBefore(List<RsiResult> rsi, DateTime t)
        {
            // Binary search for last RSI reading with Timestamp <= t.
            int lo = 0, hi = rsi.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (rsi[mid].Timestamp <= t) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            while (found >= 0 && !rsi[found].Rsi.HasValue) found--;
            return found >= 0 ? rsi[found].Rsi : null;
        }

        // ---- Loss classification output --------------------------------
        private static void PrintLossClassification(List<PostmortemRow> rows)
        {
            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  LOSS CLASSIFICATION (all losing trades)");
            Console.WriteLine("================================================================");
            Console.WriteLine("    Pure        = MFE < 1R after exit (price never came back)");
            Console.WriteLine("    Marginal    = MFE â‰¥ 1R after exit but never reached original TP");
            Console.WriteLine("    Recoverable = Original TP was touched within lookahead window");
            Console.WriteLine();

            int n = rows.Count;
            if (n == 0) return;

            Console.WriteLine($"  {"Class",-12}  {"Count",5}  {"% of losses",11}  {"Total P&L",14}  {"Avg P&L",12}");
            Console.WriteLine($"  {new string('-', 70)}");
            foreach (LossClass cls in new[] { LossClass.Pure, LossClass.Marginal, LossClass.Recoverable })
            {
                var group = rows.Where(r => r.Classification == cls).ToList();
                decimal total = group.Sum(r => r.Pnl);
                decimal avg = group.Count > 0 ? total / group.Count : 0;
                double pct = (double)group.Count / n;
                Console.WriteLine($"  {cls,-12}  {group.Count,5}  {pct,11:P1}  {total,14:F2}  {avg,12:F2}");
            }
            Console.WriteLine($"  {new string('-', 70)}");
            Console.WriteLine($"  {"TOTAL",-12}  {n,5}  {1.0,11:P1}  {rows.Sum(r => r.Pnl),14:F2}");
        }

        // ---- Direction Ã— Score matrix ---------------------------------
        private static void PrintByDirectionScoreMatrix(List<TradeRow> allTrades, List<PostmortemRow> loserRows)
        {
            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  WIN-RATE + LOSS-CLASSIFICATION BY DIRECTION Ã— SCORE BUCKET");
            Console.WriteLine("================================================================");
            Console.WriteLine("  AdjWR = (wins + recoverable losses) / total â€” ceiling if we could hold longer");
            Console.WriteLine();

            // Build index: trade index -> loss classification (for losers)
            var lossByIdx = loserRows.ToDictionary(r => r.Index, r => r.Classification);

            foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
            {
                Console.WriteLine($"  {dir.ToString().ToUpper()}:");
                Console.WriteLine($"    {"Score",-8}  {"Tot",4}  {"Win",4}  {"Loss",4}  {"Pure",4}  {"Marg",4}  {"Rec",4}  {"WR",7}  {"AdjWR",7}  {"NetP&L",14}");
                Console.WriteLine($"    {new string('-', 94)}");

                var buckets = new[] { "80+", "60-79", "40-59", "20-39", "<20" };
                foreach (var bucket in buckets)
                {
                    var inBucket = allTrades
                        .Where(t => t.Direction == dir && ScoreBucket(t.ProbabilityScore) == bucket)
                        .ToList();
                    if (inBucket.Count == 0) continue;

                    int tot = inBucket.Count;
                    int wins = inBucket.Count(t => t.PnlUsdt >= 0);
                    var bucketLosers = inBucket.Where(t => t.PnlUsdt < 0).ToList();
                    int losses = bucketLosers.Count;

                    int pure = bucketLosers.Count(t => lossByIdx.TryGetValue(t.Index, out var c) && c == LossClass.Pure);
                    int marg = bucketLosers.Count(t => lossByIdx.TryGetValue(t.Index, out var c) && c == LossClass.Marginal);
                    int rec  = bucketLosers.Count(t => lossByIdx.TryGetValue(t.Index, out var c) && c == LossClass.Recoverable);

                    double wr = tot > 0 ? (double)wins / tot : 0;
                    double adjWr = tot > 0 ? (double)(wins + rec) / tot : 0;
                    decimal netPnl = inBucket.Sum(t => t.PnlUsdt);

                    Console.WriteLine($"    {bucket,-8}  {tot,4}  {wins,4}  {losses,4}  {pure,4}  {marg,4}  {rec,4}  {wr,7:P1}  {adjWr,7:P1}  {netPnl,14:F2}");
                }
                Console.WriteLine();
            }

            // Overall totals per direction
            Console.WriteLine("  OVERALL:");
            Console.WriteLine($"    {"Dir",-8}  {"Tot",4}  {"Win",4}  {"Loss",4}  {"Pure",4}  {"Marg",4}  {"Rec",4}  {"WR",7}  {"AdjWR",7}  {"NetP&L",14}");
            Console.WriteLine($"    {new string('-', 94)}");
            foreach (var dir in new[] { TradeDirection.Long, TradeDirection.Short })
            {
                var d = allTrades.Where(t => t.Direction == dir).ToList();
                if (d.Count == 0) continue;
                int wins = d.Count(t => t.PnlUsdt >= 0);
                var dLosers = d.Where(t => t.PnlUsdt < 0).ToList();
                int pure = dLosers.Count(t => lossByIdx.TryGetValue(t.Index, out var c) && c == LossClass.Pure);
                int marg = dLosers.Count(t => lossByIdx.TryGetValue(t.Index, out var c) && c == LossClass.Marginal);
                int rec  = dLosers.Count(t => lossByIdx.TryGetValue(t.Index, out var c) && c == LossClass.Recoverable);
                double wr = (double)wins / d.Count;
                double adjWr = (double)(wins + rec) / d.Count;
                Console.WriteLine($"    {dir,-8}  {d.Count,4}  {wins,4}  {dLosers.Count,4}  {pure,4}  {marg,4}  {rec,4}  {wr,7:P1}  {adjWr,7:P1}  {d.Sum(t => t.PnlUsdt),14:F2}");
            }
        }

        // ---- Intra-trade feature comparison ------------------------------
        //
        // For each feature, compare the distribution between Pure and Recoverable
        // losses. A feature is useful for in-flight prediction if its mean or
        // rate differs meaningfully between the two classes.
        private static void PrintIntraTradeFeatureComparison(List<PostmortemRow> rows)
        {
            var pure = rows.Where(r => r.Classification == LossClass.Pure).ToList();
            var rec  = rows.Where(r => r.Classification == LossClass.Recoverable).ToList();
            if (pure.Count == 0 || rec.Count == 0) return;

            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  INTRA-TRADE FEATURES: Pure vs Recoverable");
            Console.WriteLine("================================================================");
            Console.WriteLine("  A feature is predictive if Pure and Recoverable distributions diverge.");
            Console.WriteLine("  Goal: find features we could see EARLY to decide skip/cut vs hold.");
            Console.WriteLine();

            string Row(string name, Func<PostmortemRow, decimal> f, string fmt = "F2")
            {
                decimal pAvg = pure.Average(f);
                decimal rAvg = rec.Average(f);
                decimal pMed = Median(pure.Select(f));
                decimal rMed = Median(rec.Select(f));
                decimal delta = rAvg - pAvg;
                return $"  {name,-26}  Pure: avg {pAvg.ToString(fmt),8} med {pMed.ToString(fmt),8}   |   " +
                       $"Rec: avg {rAvg.ToString(fmt),8} med {rMed.ToString(fmt),8}   |   Î”(R-P) {delta.ToString(fmt)}";
            }

            string BoolRow(string name, Func<PostmortemRow, bool> f)
            {
                double pRate = (double)pure.Count(f) / pure.Count;
                double rRate = (double)rec.Count(f) / rec.Count;
                return $"  {name,-26}  Pure: {pRate,7:P1}                    |   Rec: {rRate,7:P1}                    |   Î” {rRate - pRate,+7:P1}";
            }

            Console.WriteLine($"  n Pure: {pure.Count}   n Recoverable: {rec.Count}");
            Console.WriteLine();

            Console.WriteLine("  SCORE-AT-ENTRY (available before trade opens):");
            Console.WriteLine(Row("ProbabilityScore",         r => r.ProbabilityScore));
            Console.WriteLine();
            Console.WriteLine("  EARLY PRICE ACTION (available bar-by-bar):");
            Console.WriteLine(BoolRow("FirstBarFavorableClose",  r => r.FirstBarFavorable));
            Console.WriteLine(Row("BarsToFirstAdverseClose",  r => r.BarsToFirstAdverseClose, "F1"));
            Console.WriteLine(Row("MoveAtBar1  (R, +=favor)", r => r.AdverseAt1BarR, "F2"));
            Console.WriteLine(Row("MoveAtBar2  (R)",          r => r.AdverseAt2BarsR, "F2"));
            Console.WriteLine(Row("MoveAtBar3  (R)",          r => r.AdverseAt3BarsR, "F2"));
            Console.WriteLine(Row("MfeDuringTrade (R)",       r => r.MfeDuringTradeR, "F2"));
            Console.WriteLine(Row("MfeBarIdxInTrade",         r => r.MfeBarIdxInTrade, "F1"));
            Console.WriteLine(Row("HoldBars",                 r => r.HoldBars, "F1"));
            Console.WriteLine(Row("ImpulsivenessRatio",       r => r.ImpulsivenessRatio, "F3"));
            Console.WriteLine();

            Console.WriteLine("  HTF (4H) RSI BEHAVIOR DURING TRADE:");
            Console.WriteLine(Row("HtfRsiAtEntry(4H)",        r => (decimal)r.HtfRsiAtEntryCompleted, "F1"));
            Console.WriteLine(Row("HtfRsiAtExit(4H)",         r => (decimal)r.HtfRsiAtExitCompleted, "F1"));
            Console.WriteLine(Row("HtfRsiChange(Exit-Entry)", r => (decimal)r.HtfRsiChangeInTrade, "F2"));
            Console.WriteLine(Row("HtfRsiAdverseExtreme",     r => (decimal)r.HtfRsiAdverseExtreme, "F2"));
            Console.WriteLine(Row("4HBarClosesInTrade",       r => r.BarsWith4HCloseInTrade, "F2"));
            Console.WriteLine(BoolRow("HtfRsiCrossedNeutral(50)", r => r.HtfRsiCrossedNeutral));
            Console.WriteLine();

            Console.WriteLine("  INTERMEDIATE TF â€” 30M DURING TRADE:");
            Console.WriteLine(Row("30mRsiAtEntry",            r => (decimal)r.Rsi30mAtEntry, "F1"));
            Console.WriteLine(Row("30mRsiAtExit",             r => (decimal)r.Rsi30mAtExit, "F1"));
            Console.WriteLine(Row("30mRsiChange(Exit-Entry)", r => (decimal)r.Rsi30mChange, "F2"));
            Console.WriteLine(Row("30mRsiAdverseExtreme",     r => (decimal)r.Rsi30mAdverseExtreme, "F2"));
            Console.WriteLine(Row("30mBarClosesInTrade",      r => r.Bars30mInTrade, "F2"));
            Console.WriteLine(BoolRow("30mRsiCrossedNeutral(50)",  r => r.Rsi30mCrossedNeutral));
            Console.WriteLine(BoolRow("30mEmaBullishAtEntry",      r => r.Ema30mBullishAtEntry));
            Console.WriteLine(BoolRow("30mEmaCrossedAgainstUs",    r => r.Ema30mCrossedAgainst));
            Console.WriteLine(BoolRow("30mStructureBroken",        r => r.Structure30mBroken));
            Console.WriteLine();

            Console.WriteLine("  INTERMEDIATE TF â€” 1H DURING TRADE:");
            Console.WriteLine(Row("1hRsiAtEntry",             r => (decimal)r.Rsi1hAtEntry, "F1"));
            Console.WriteLine(Row("1hRsiAtExit",              r => (decimal)r.Rsi1hAtExit, "F1"));
            Console.WriteLine(Row("1hRsiChange(Exit-Entry)",  r => (decimal)r.Rsi1hChange, "F2"));
            Console.WriteLine(Row("1hRsiAdverseExtreme",      r => (decimal)r.Rsi1hAdverseExtreme, "F2"));
            Console.WriteLine(Row("1hBarClosesInTrade",       r => r.Bars1hInTrade, "F2"));
            Console.WriteLine(BoolRow("1hRsiCrossedNeutral(50)",   r => r.Rsi1hCrossedNeutral));
            Console.WriteLine(BoolRow("1hEmaBullishAtEntry",       r => r.Ema1hBullishAtEntry));
            Console.WriteLine(BoolRow("1hEmaCrossedAgainstUs",     r => r.Ema1hCrossedAgainst));
            Console.WriteLine(BoolRow("1hStructureBroken",         r => r.Structure1hBroken));
            Console.WriteLine();

            // Simple decision-rule candidates: for each threshold, how well does it discriminate?
            Console.WriteLine("  CANDIDATE EARLY-EXIT RULES:");
            Console.WriteLine("    Rule triggered if feature meets threshold after entry.");
            Console.WriteLine("    'Catches Pure' = % of Pure losses the rule would have cut early.");
            Console.WriteLine("    'False positive' = % of Recoverable losses mistakenly cut (= lost upside).");
            Console.WriteLine();
            Console.WriteLine($"    {"Rule",-42}  {"CatchesPure",12}  {"FalsePos",10}");
            Console.WriteLine($"    {new string('-', 72)}");

            void Rule(string name, Func<PostmortemRow, bool> trigger)
            {
                int p = pure.Count(trigger);
                int r = rec.Count(trigger);
                Console.WriteLine($"    {name,-42}  {(double)p / pure.Count,11:P1}   {(double)r / rec.Count,9:P1}");
            }

            Rule("Bar1 adverse > 0.5R",                  r => r.AdverseAt1BarR <= -0.5m);
            Rule("Bar1 adverse > 0.75R",                 r => r.AdverseAt1BarR <= -0.75m);
            Rule("Bar2 adverse > 0.75R",                 r => r.AdverseAt2BarsR <= -0.75m);
            Rule("Bar3 adverse > 0.75R",                 r => r.AdverseAt3BarsR <= -0.75m);
            Rule("Bar1 close adverse (not favorable)",   r => !r.FirstBarFavorable);
            Rule("MFE-during-trade < 0.25R",             r => r.MfeDuringTradeR < 0.25m);
            Rule("MFE-during-trade < 0.5R",              r => r.MfeDuringTradeR < 0.5m);
            Rule("MFE-during-trade < 1.0R",              r => r.MfeDuringTradeR < 1.0m);
            Rule("Bar1 adv>0.5R AND !FirstBarFavorable", r => r.AdverseAt1BarR <= -0.5m && !r.FirstBarFavorable);
            Rule("Bar2 adv>0.5R AND MFE<0.25R",          r => r.AdverseAt2BarsR <= -0.5m && r.MfeDuringTradeR < 0.25m);

            Console.WriteLine();
            Console.WriteLine("  CANDIDATE HTF-BASED RULES (4H RSI regime shift during trade):");
            Console.WriteLine($"    {"Rule",-48}  {"CatchesPure",12}  {"FalsePos",10}");
            Console.WriteLine($"    {new string('-', 78)}");
            Rule("HtfRsi crossed neutral 50 against us",  r => r.HtfRsiCrossedNeutral);
            Rule("HtfRsi adverse extreme > 5 pts",        r => r.HtfRsiAdverseExtreme <= -5.0);
            Rule("HtfRsi adverse extreme > 10 pts",       r => r.HtfRsiAdverseExtreme <= -10.0);
            Rule("HtfRsi change < -5 (exit-entry)",       r => r.HtfRsiChangeInTrade <= -5.0);
            Rule("HtfRsi change < -10",                   r => r.HtfRsiChangeInTrade <= -10.0);
            Rule("4H bar closed in trade",                r => r.BarsWith4HCloseInTrade > 0);
            Rule("4H close AND HtfRsi adv > 5",           r => r.BarsWith4HCloseInTrade > 0 && r.HtfRsiAdverseExtreme <= -5.0);
            Rule("4H close AND HtfRsi crossed neutral",   r => r.BarsWith4HCloseInTrade > 0 && r.HtfRsiCrossedNeutral);

            Console.WriteLine();
            Console.WriteLine("  CANDIDATE 30M RULES:");
            Console.WriteLine($"    {"Rule",-48}  {"CatchesPure",12}  {"FalsePos",10}");
            Console.WriteLine($"    {new string('-', 78)}");
            Rule("30mRsi crossed neutral 50 against us",  r => r.Rsi30mCrossedNeutral);
            Rule("30mRsi adverse extreme > 5 pts",        r => r.Rsi30mAdverseExtreme <= -5.0);
            Rule("30mRsi adverse extreme > 10 pts",       r => r.Rsi30mAdverseExtreme <= -10.0);
            Rule("30mRsi change < -5 (exit-entry)",       r => r.Rsi30mChange <= -5.0);
            Rule("30mRsi change < -10",                   r => r.Rsi30mChange <= -10.0);
            Rule("30mEma crossed against us",             r => r.Ema30mCrossedAgainst);
            Rule("30mStructure broken (entry bar L/H)",   r => r.Structure30mBroken);
            Rule("30mStructureBroken AND 30mEmaCross",    r => r.Structure30mBroken && r.Ema30mCrossedAgainst);
            Rule("30mStructureBroken AND RsiAdv > 5",     r => r.Structure30mBroken && r.Rsi30mAdverseExtreme <= -5.0);

            Console.WriteLine();
            Console.WriteLine("  CANDIDATE 1H RULES:");
            Console.WriteLine($"    {"Rule",-48}  {"CatchesPure",12}  {"FalsePos",10}");
            Console.WriteLine($"    {new string('-', 78)}");
            Rule("1hRsi crossed neutral 50 against us",   r => r.Rsi1hCrossedNeutral);
            Rule("1hRsi adverse extreme > 5 pts",         r => r.Rsi1hAdverseExtreme <= -5.0);
            Rule("1hRsi adverse extreme > 10 pts",        r => r.Rsi1hAdverseExtreme <= -10.0);
            Rule("1hRsi change < -5 (exit-entry)",        r => r.Rsi1hChange <= -5.0);
            Rule("1hEma crossed against us",              r => r.Ema1hCrossedAgainst);
            Rule("1hStructure broken (entry bar L/H)",    r => r.Structure1hBroken);
            Rule("1h close occurred AND RsiAdv > 5",      r => r.Bars1hInTrade > 0 && r.Rsi1hAdverseExtreme <= -5.0);
            Rule("1h close occurred AND Structure broken",r => r.Bars1hInTrade > 0 && r.Structure1hBroken);

            Console.WriteLine();
            Console.WriteLine("  COMBINED 30M+1H CONFIRMATION RULES:");
            Console.WriteLine($"    {"Rule",-48}  {"CatchesPure",12}  {"FalsePos",10}");
            Console.WriteLine($"    {new string('-', 78)}");
            Rule("Both 30m & 1h RSI crossed neutral",     r => r.Rsi30mCrossedNeutral && r.Rsi1hCrossedNeutral);
            Rule("Both 30m & 1h structure broken",        r => r.Structure30mBroken && r.Structure1hBroken);
            Rule("Either 30m or 1h EMA crossed against",  r => r.Ema30mCrossedAgainst || r.Ema1hCrossedAgainst);
            Rule("30mStructureBroken AND 1hRsiAdv>5",     r => r.Structure30mBroken && r.Rsi1hAdverseExtreme <= -5.0);
        }

        private static string ScoreBucket(int s) =>
            s >= 80 ? "80+" : s >= 60 ? "60-79" : s >= 40 ? "40-59" : s >= 20 ? "20-39" : "<20";

        private static decimal Median(IEnumerable<decimal> src)
        {
            var arr = src.OrderBy(x => x).ToArray();
            if (arr.Length == 0) return 0;
            int m = arr.Length / 2;
            return arr.Length % 2 == 1 ? arr[m] : (arr[m - 1] + arr[m]) / 2;
        }

        private static double MedianD(IEnumerable<double> src)
        {
            var arr = src.OrderBy(x => x).ToArray();
            if (arr.Length == 0) return 0.0;
            int m = arr.Length / 2;
            return arr.Length % 2 == 1 ? arr[m] : (arr[m - 1] + arr[m]) / 2.0;
        }

        // ---- CSV helpers ----
        private sealed class TradeRow
        {
            public int Index;
            public TradeDirection Direction;
            public DateTime SignalTime;
            public decimal SignalPrice;
            public DateTime EntryTime;
            public decimal EntryPrice;
            public DateTime ExitTime;
            public decimal ExitPrice;
            public string ExitReason;
            public decimal StopLoss;
            public decimal TakeProfit;
            public decimal AtrAtSignal;
            public int ProbabilityScore;
            public decimal PnlUsdt;
        }

        public enum LossClass { Pure, Marginal, Recoverable }

        private sealed class PostmortemRow
        {
            public int Index;
            public TradeDirection Direction;
            public DateTime EntryTime;
            public decimal EntryPrice;
            public DateTime ExitTime;
            public decimal ExitPrice;
            public string ExitReason;
            public decimal StopLoss;
            public decimal TakeProfit;
            public decimal Atr;
            public int ProbabilityScore;
            public decimal Pnl;

            public decimal MfeR;
            public decimal MaePastSlR;
            public decimal MfeAtrs;
            public int BarsToMfe;
            public bool TpTouched;
            public int BarsToTpTouch;
            public bool WouldRecoverToEntry;
            public LossClass Classification;

            // Intra-trade features (visible while trade is live)
            public int HoldBars;
            public bool FirstBarFavorable;
            public int BarsToFirstAdverseClose;
            public decimal MfeDuringTradeR;
            public int MfeBarIdxInTrade;
            public decimal AdverseAt1BarR;  // signed: favorable=+, adverse=-
            public decimal AdverseAt2BarsR;
            public decimal AdverseAt3BarsR;
            public decimal ImpulsivenessRatio;

            // HTF (4H) RSI features during the trade
            public double HtfRsiAtEntryCompleted;
            public double HtfRsiAtExitCompleted;
            public double HtfRsiChangeInTrade;     // exit - entry (signed)
            public double HtfRsiAdverseExtreme;    // worst RSI move against us (negative = adverse)
            public int BarsWith4HCloseInTrade;     // how many 4H closes happened during trade
            public bool HtfRsiCrossedNeutral;      // did 4H RSI cross 50 against us during trade?

            // Intermediate TF (30M) features
            public double Rsi30mAtEntry;
            public double Rsi30mAtExit;
            public double Rsi30mChange;
            public double Rsi30mAdverseExtreme;
            public bool Rsi30mCrossedNeutral;
            public int Bars30mInTrade;
            public bool Ema30mBullishAtEntry;
            public bool Ema30mCrossedAgainst;
            public bool Structure30mBroken;

            // Intermediate TF (1H) features
            public double Rsi1hAtEntry;
            public double Rsi1hAtExit;
            public double Rsi1hChange;
            public double Rsi1hAdverseExtreme;
            public bool Rsi1hCrossedNeutral;
            public int Bars1hInTrade;
            public bool Ema1hBullishAtEntry;
            public bool Ema1hCrossedAgainst;
            public bool Structure1hBroken;
        }

        private static IEnumerable<TradeRow> ReadTrades(string path)
        {
            using var r = new StreamReader(path);
            string header = r.ReadLine();
            if (header == null) yield break;
            var cols = header.Split(',');
            int ci(string name) { var idx = Array.IndexOf(cols, name); if (idx < 0) throw new InvalidOperationException($"missing column {name}"); return idx; }

            int cIdx = ci("Index");
            int cDir = ci("Direction");
            int cSigTime = ci("SignalTime");
            int cSigPx = ci("SignalPrice");
            int cEnTime = ci("EntryTime");
            int cEnPx = ci("EntryPrice");
            int cExTime = ci("ExitTime");
            int cExPx = ci("ExitPrice");
            int cReason = ci("ExitReason");
            int cSl = ci("StopLoss");
            int cTp = ci("TakeProfit");
            int cAtr = ci("AtrAtSignal");
            int cScore = ci("ProbabilityScore");
            int cPnl = ci("PnlUsdt");

            string line;
            while ((line = r.ReadLine()) != null)
            {
                var f = line.Split(',');
                yield return new TradeRow
                {
                    Index = int.Parse(f[cIdx], CultureInfo.InvariantCulture),
                    Direction = (TradeDirection)Enum.Parse(typeof(TradeDirection), f[cDir]),
                    SignalTime = DateTime.Parse(f[cSigTime], CultureInfo.InvariantCulture),
                    SignalPrice = decimal.Parse(f[cSigPx], CultureInfo.InvariantCulture),
                    EntryTime = DateTime.Parse(f[cEnTime], CultureInfo.InvariantCulture),
                    EntryPrice = decimal.Parse(f[cEnPx], CultureInfo.InvariantCulture),
                    ExitTime = DateTime.Parse(f[cExTime], CultureInfo.InvariantCulture),
                    ExitPrice = decimal.Parse(f[cExPx], CultureInfo.InvariantCulture),
                    ExitReason = f[cReason],
                    StopLoss = decimal.Parse(f[cSl], CultureInfo.InvariantCulture),
                    TakeProfit = decimal.Parse(f[cTp], CultureInfo.InvariantCulture),
                    AtrAtSignal = decimal.Parse(f[cAtr], CultureInfo.InvariantCulture),
                    ProbabilityScore = int.Parse(f[cScore], CultureInfo.InvariantCulture),
                    PnlUsdt = decimal.Parse(f[cPnl], CultureInfo.InvariantCulture),
                };
            }
        }

        private static void WriteCsv(string path, List<PostmortemRow> rows)
        {
            using var w = new StreamWriter(path);
            w.WriteLine("Index,Direction,ExitReason,Classification,EntryTime,EntryPrice,ExitTime,ExitPrice,StopLoss,TakeProfit,Atr,Score,Pnl,MfeR,MaePastSlR,MfeAtrs,BarsToMfe,TpTouched,BarsToTpTouch,WouldRecoverToEntry");
            foreach (var r in rows)
            {
                w.WriteLine(string.Join(",",
                    r.Index, r.Direction, r.ExitReason, r.Classification,
                    r.EntryTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    r.EntryPrice.ToString("F2", CultureInfo.InvariantCulture),
                    r.ExitTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    r.ExitPrice.ToString("F2", CultureInfo.InvariantCulture),
                    r.StopLoss.ToString("F2", CultureInfo.InvariantCulture),
                    r.TakeProfit.ToString("F2", CultureInfo.InvariantCulture),
                    r.Atr.ToString("F2", CultureInfo.InvariantCulture),
                    r.ProbabilityScore,
                    r.Pnl.ToString("F2", CultureInfo.InvariantCulture),
                    r.MfeR.ToString("F3", CultureInfo.InvariantCulture),
                    r.MaePastSlR.ToString("F3", CultureInfo.InvariantCulture),
                    r.MfeAtrs.ToString("F3", CultureInfo.InvariantCulture),
                    r.BarsToMfe,
                    r.TpTouched ? 1 : 0,
                    r.BarsToTpTouch,
                    r.WouldRecoverToEntry ? 1 : 0));
            }
            Console.WriteLine($"  Postmortem CSV written: {path}");
        }
    }
}
