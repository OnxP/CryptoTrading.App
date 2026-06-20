using CryptoTrading.App.Algorithm.DualRegime;
using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CryptoTrading.App.BackTesting
{
    /// <summary>
    /// Dedicated backtest driver for the Dual-Regime strategy.
    ///
    /// Unlike the HtfRsi replay path (which reads SQL and drives a live
    /// execution strategy), this harness validates the PORTED SIGNAL PIPELINE
    /// against the Python reference. It feeds the exact same 15M CSV Python
    /// used through the real <see cref="DualRegimeCandleAggregator"/> +
    /// <see cref="FeatureEngine"/> + ONNX <see cref="DualRegimeModelArtifact"/>,
    /// producing one prediction row per completed 1H bar, then runs a faithful
    /// C# port of Python's run_combined_backtest over the same test window.
    ///
    /// Only the BACKTEST LOOP is re-implemented here (it is measurement, not
    /// production strategy code); every feature, classification and prediction
    /// comes from the real ported components under test. The point is an
    /// apples-to-apples comparison: same data, same 1H-bar exit granularity,
    /// same PnL/compounding math as the Python ground truth.
    ///
    /// Note on causal-vs-lookahead divergence: the C# aggregator is a CAUSAL
    /// streaming aggregator (it only sees closed sub-bars), whereas the Python
    /// reference vectorises with mild lookahead in two spots — the phase-shift
    /// class_combined vote and the 4H-regime containment merge. The C# port
    /// deliberately removes that lookahead for live correctness, so an exact
    /// trade-for-trade match is not expected; a balanced long/short split and a
    /// comparable trade count confirm the prior 99%-short bug is fixed.
    /// </summary>
    internal static class DualRegimeBacktest
    {
        // Python test window (from combined_equity.csv): the 1H decision bars
        // run 2026-01-08 19:45 → 2026-03-31 14:45 inclusive.
        private static readonly DateTime DefaultTestStart = new(2026, 1, 8, 19, 45, 0);
        private static readonly DateTime DefaultTestEnd = new(2026, 3, 31, 14, 45, 0);

        public sealed class Options
        {
            public string CsvPath = @"C:\Temp\BTCUSDT 15M.csv";
            public string ModelDir;                                    // resolved if null
            public string RefTradesCsv = @"C:\Temp\regime-sim\combined_trades.csv";
            public string OutputCsv;
            public DateTime TestStart = DefaultTestStart;
            public DateTime TestEnd = DefaultTestEnd;
            public double InitialCapital = 10000.0;

            // When set, runs the decisive validation against Python's authoritative
            // per-bar rows (dump_testdf.py → py_test_df.csv) instead of the live
            // streaming pipeline. Proves loop fidelity + ONNX≈xgboost in isolation.
            public string ValidatePyRowsCsv;
        }

        // ── One 1H decision row: OHLC + the live predictions/classes ──
        private sealed class BtRow
        {
            public DateTime Ts;
            public double Open, High, Low, Close;
            public double Pred4h;
            public int Pred4hClass, Pred1hClass;
            public int ClassCombined;
            public double RollingSigma24;
        }

        private sealed class BtTrade
        {
            public DateTime EntryTime, ExitTime;
            public string Side;          // long / short
            public string Strategy;      // trend / chop
            public string SignalType;    // bull_bull / ... / chop
            public double FillPrice, ExitPrice, GrossRet, NetPnl;
            public string ExitReason;
            public int BarsHeld;
            public double Notional, CapitalAfter;
        }

        public static int Run(Options opts)
        {
            Console.WriteLine("================================================================");
            Console.WriteLine("  DUAL-REGIME — BACKTEST (signal-pipeline parity vs Python)");
            Console.WriteLine("================================================================");

            string modelDir = ResolveModelDir(opts.ModelDir);
            Console.WriteLine($"  CSV:          {opts.CsvPath}");
            Console.WriteLine($"  Model dir:    {modelDir}");
            Console.WriteLine($"  Test window:  {opts.TestStart:yyyy-MM-dd HH:mm} → {opts.TestEnd:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"  Ref trades:   {opts.RefTradesCsv}");
            Console.WriteLine("----------------------------------------------------------------");

            if (!File.Exists(opts.CsvPath))
            {
                Console.Error.WriteLine($"15M CSV not found: {opts.CsvPath}");
                return 2;
            }

            using var artifact = DualRegimeModelArtifact.Load(modelDir);
            Console.WriteLine($"  Model: trained {artifact.TrainedAt:yyyy-MM-dd}, " +
                              $"sigma15m={artifact.Sigma15M:F6} sigma1h={artifact.Sigma1H:F6} sigma4h={artifact.Sigma4H:F6}, " +
                              $"thr1h={artifact.Thr1H:F6} thr4h={artifact.Thr4H:F6}");

            // Decisive validation path: replay Python's authoritative per-bar rows.
            if (!string.IsNullOrWhiteSpace(opts.ValidatePyRowsCsv))
                return RunValidatePyRows(opts, artifact);

            // 1) Load 15M bars (Python mapping: timestamp=open_time, volume=volume_btc)
            var candles15M = LoadCsv15M(opts.CsvPath);
            Console.WriteLine($"  Loaded {candles15M.Count} 15M bars " +
                              $"({candles15M[0].CloseTime:yyyy-MM-dd HH:mm} → {candles15M[^1].CloseTime:yyyy-MM-dd HH:mm})");

            // 2) Stream every 15M bar through the REAL ported aggregator; capture a
            //    prediction row each time a 1H bar completes with valid features.
            var aggregator = new DualRegimeCandleAggregator(artifact);
            var rows = new List<BtRow>();
            int predDistN = 0;
            var p4 = new int[3]; // [-1,0,+1]
            var p1 = new int[3];

            foreach (var candle in candles15M)
            {
                var bar1H = aggregator.AddBar15M(candle);
                if (bar1H == null) continue;

                var features = aggregator.GetLatestFeatures();
                if (features == null) continue;          // warmup: <25 1H bars

                double pred1h = artifact.Predict1H(features);
                double pred4h = artifact.Predict4H(features);
                int p1c = artifact.ClassifyPred(pred1h, artifact.Thr1H);
                int p4c = artifact.ClassifyPred(pred4h, artifact.Thr4H);

                rows.Add(new BtRow
                {
                    Ts = bar1H.Timestamp,
                    Open = (double)bar1H.Open,
                    High = (double)bar1H.High,
                    Low = (double)bar1H.Low,
                    Close = (double)bar1H.Close,
                    Pred4h = pred4h,
                    Pred4hClass = p4c,
                    Pred1hClass = p1c,
                    ClassCombined = bar1H.ClassCombined,
                    RollingSigma24 = aggregator.GetRollingSigma24(),
                });
            }

            // 3) Restrict to the Python test window.
            var testRows = rows.Where(r => r.Ts >= opts.TestStart && r.Ts <= opts.TestEnd)
                               .OrderBy(r => r.Ts)
                               .ToList();

            if (testRows.Count == 0)
            {
                Console.Error.WriteLine("No 1H bars fell inside the test window — check CSV/timestamps.");
                return 3;
            }

            foreach (var r in testRows)
            {
                predDistN++;
                p4[r.Pred4hClass + 1]++;
                p1[r.Pred1hClass + 1]++;
            }

            Console.WriteLine($"  1H bars total: {rows.Count} | in test window: {testRows.Count} " +
                              $"({testRows[0].Ts:yyyy-MM-dd HH:mm} → {testRows[^1].Ts:yyyy-MM-dd HH:mm})");
            Console.WriteLine($"  pred_4h_class dist (-1/0/+1): {p4[0]}/{p4[1]}/{p4[2]}");
            Console.WriteLine($"  pred_1h_class dist (-1/0/+1): {p1[0]}/{p1[1]}/{p1[2]}");
            Console.WriteLine("----------------------------------------------------------------");

            // 4) Faithful port of Python run_combined_backtest.
            var prm = new DualRegimeParams();
            var trades = RunCombinedBacktest(testRows, prm, opts.InitialCapital, out double finalCapital);

            // 5) Report + compare.
            PrintReport(trades, opts.InitialCapital, finalCapital);
            CompareToReference(opts.RefTradesCsv, trades, opts.InitialCapital, finalCapital);

            if (!string.IsNullOrWhiteSpace(opts.OutputCsv))
                WriteTradesCsv(opts.OutputCsv, trades);

            return 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  DECISIVE VALIDATION — replay Python's authoritative per-bar rows
        //
        //  Splits the "does the C# port match Python" question into two
        //  independent, falsifiable checks using py_test_df.csv (produced by
        //  dump_testdf.py from the exact reference pipeline):
        //
        //   [A] Backtest-loop fidelity. Feed Python's own OHLC + predictions +
        //       class_combined + rolling_sigma_24 into RunCombinedBacktest. If
        //       the C# loop is a faithful port, it MUST reproduce Python's
        //       ~354 trades / ~85.9% win / ~+352.5% return. Any deviation here
        //       is a bug in the loop math, NOT in features/ONNX.
        //
        //   [B] ONNX vs xgboost. Feed Python's 17 feature columns (already in
        //       manifest feat_cols order) through the C# ONNX models and compare
        //       pred_1h/pred_4h and their classes against Python's xgboost
        //       outputs. Near-zero deltas prove inference parity.
        //
        //  Together these isolate the live streaming gap to exactly one cause:
        //  the intentional causal-vs-lookahead feature divergence.
        // ─────────────────────────────────────────────────────────────
        private static int RunValidatePyRows(Options opts, DualRegimeModelArtifact artifact)
        {
            string path = opts.ValidatePyRowsCsv;
            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  VALIDATION — replay Python authoritative per-bar rows");
            Console.WriteLine($"  Source: {path}");
            Console.WriteLine("================================================================");

            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"py_test_df CSV not found: {path}");
                return 2;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2)
            {
                Console.Error.WriteLine("py_test_df CSV is empty.");
                return 3;
            }

            // Known dump layout (dump_testdf.py):
            //   0 timestamp, 1 open, 2 high, 3 low, 4 close,
            //   5..21 feat_cols (manifest order: [0]=rolling_sigma_24 ... [9]=class_combined ... [16]=first_two_15m_ret),
            //   22 pred_1h, 23 pred_4h, 24 pred_1h_class, 25 pred_4h_class
            const int FeatBase = 5, FeatCount = 17;
            const int ColPred1h = 22, ColPred4h = 23, ColPred1hClass = 24, ColPred4hClass = 25;
            const int ColRollingSigma24 = FeatBase + 0;   // 5
            const int ColClassCombined = FeatBase + 9;    // 14
            const int MinCols = 26;

            var btRows = new List<BtRow>();
            var feats = new List<float[]>();
            var pyPred1h = new List<double>();
            var pyPred4h = new List<double>();
            var pyPred1hClass = new List<int>();
            var pyPred4hClass = new List<int>();

            int parsed = 0, skipped = 0;
            for (int i = 1; i < lines.Length; i++)   // row 0 is the header
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var f = lines[i].Split(',');
                if (f.Length < MinCols) { skipped++; continue; }
                if (!TryParseTs(f[0], out DateTime ts)) { skipped++; continue; }

                var feat = new float[FeatCount];
                bool ok = true;
                for (int k = 0; k < FeatCount; k++)
                {
                    if (!double.TryParse(f[FeatBase + k], NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    { ok = false; break; }
                    feat[k] = (float)v;
                }
                if (!ok) { skipped++; continue; }

                double Col(int idx) => double.Parse(f[idx], NumberStyles.Any, CultureInfo.InvariantCulture);

                btRows.Add(new BtRow
                {
                    Ts = ts,
                    Open = Col(1), High = Col(2), Low = Col(3), Close = Col(4),
                    Pred4h = Col(ColPred4h),
                    Pred1hClass = (int)Math.Round(Col(ColPred1hClass)),
                    Pred4hClass = (int)Math.Round(Col(ColPred4hClass)),
                    ClassCombined = (int)Math.Round(Col(ColClassCombined)),
                    RollingSigma24 = Col(ColRollingSigma24),
                });
                feats.Add(feat);
                pyPred1h.Add(Col(ColPred1h));
                pyPred4h.Add(Col(ColPred4h));
                pyPred1hClass.Add((int)Math.Round(Col(ColPred1hClass)));
                pyPred4hClass.Add((int)Math.Round(Col(ColPred4hClass)));
                parsed++;
            }

            if (parsed == 0)
            {
                Console.Error.WriteLine("No valid rows parsed from py_test_df CSV.");
                return 3;
            }

            Console.WriteLine($"  Parsed {parsed} rows ({skipped} skipped) " +
                              $"({btRows[0].Ts:yyyy-MM-dd HH:mm} → {btRows[^1].Ts:yyyy-MM-dd HH:mm})");

            // ── [A] Backtest-loop fidelity over Python's own predictions ──
            Console.WriteLine();
            Console.WriteLine("  [A] Backtest loop over Python rows  (expect ≈Python: ~354 / ~85.9% / ~+352.5%)");
            Console.WriteLine("  ----------------------------------------------------------------");
            var prm = new DualRegimeParams();
            var trades = RunCombinedBacktest(btRows, prm, opts.InitialCapital, out double finalCapital);
            PrintReport(trades, opts.InitialCapital, finalCapital);
            CompareToReference(opts.RefTradesCsv, trades, opts.InitialCapital, finalCapital);

            // ── [B] C# ONNX inference vs Python xgboost predictions ──
            Console.WriteLine();
            Console.WriteLine("  [B] C# ONNX inference vs Python xgboost (per-bar feature replay)");
            Console.WriteLine("  ----------------------------------------------------------------");
            int n = feats.Count;
            double maxAbs1h = 0, sumAbs1h = 0, maxAbs4h = 0, sumAbs4h = 0;
            int agree1h = 0, agree4h = 0;
            for (int i = 0; i < n; i++)
            {
                double c1 = artifact.Predict1H(feats[i]);
                double c4 = artifact.Predict4H(feats[i]);
                double d1 = Math.Abs(c1 - pyPred1h[i]);
                double d4 = Math.Abs(c4 - pyPred4h[i]);
                maxAbs1h = Math.Max(maxAbs1h, d1); sumAbs1h += d1;
                maxAbs4h = Math.Max(maxAbs4h, d4); sumAbs4h += d4;
                if (artifact.ClassifyPred(c1, artifact.Thr1H) == pyPred1hClass[i]) agree1h++;
                if (artifact.ClassifyPred(c4, artifact.Thr4H) == pyPred4hClass[i]) agree4h++;
            }
            Console.WriteLine($"  pred_1h:  mean|Δ|={sumAbs1h / n:E3}   max|Δ|={maxAbs1h:E3}   " +
                              $"class agree {agree1h}/{n} ({100.0 * agree1h / n:F2}%)");
            Console.WriteLine($"  pred_4h:  mean|Δ|={sumAbs4h / n:E3}   max|Δ|={maxAbs4h:E3}   " +
                              $"class agree {agree4h}/{n} ({100.0 * agree4h / n:F2}%)");

            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  INTERPRETATION");
            Console.WriteLine("    [A] match  → C# backtest loop is a faithful port of Python.");
            Console.WriteLine("    [B] ~0 Δ   → C# ONNX inference matches Python xgboost.");
            Console.WriteLine("    If both hold, the live-pipeline gap is fully attributable to");
            Console.WriteLine("    the intentional causal (no-lookahead) feature construction.");
            Console.WriteLine("================================================================");

            if (!string.IsNullOrWhiteSpace(opts.OutputCsv))
                WriteTradesCsv(opts.OutputCsv, trades);

            return 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  Python run_combined_backtest — faithful C# port
        // ─────────────────────────────────────────────────────────────
        private sealed class Position
        {
            public string Side;          // long / short
            public string SignalType;    // chop for chop trades
            public double FillPrice, StopPrice, TargetPrice, Notional, EntryCost;
            public DateTime EntryTime;
            public int BarsSinceOpen;
            public double Mfe, Mae, PeakPrice;
            public bool BeEngaged;        // chop only
        }

        private static List<BtTrade> RunCombinedBacktest(
            List<BtRow> testData, DualRegimeParams prm, double initialCapital, out double capital)
        {
            capital = initialCapital;
            Position position = null;
            string positionType = null;  // trend / chop
            var trades = new List<BtTrade>();

            foreach (var row in testData)
            {
                bool skipEntry = false;

                // ── Manage open position ──
                if (position != null)
                {
                    double barLow = row.Low, barHigh = row.High;

                    // MAE / MFE update
                    double mfeNow;
                    if (position.Side == "long")
                    {
                        double maeNow = (barLow - position.FillPrice) / position.FillPrice;
                        position.Mae = Math.Min(position.Mae, maeNow);
                        double newPeak = Math.Max(position.PeakPrice, barHigh);
                        mfeNow = (newPeak - position.FillPrice) / position.FillPrice;
                        position.PeakPrice = newPeak;
                    }
                    else
                    {
                        double maeNow = (position.FillPrice - barHigh) / position.FillPrice;
                        position.Mae = Math.Min(position.Mae, maeNow);
                        double newPeak = Math.Min(position.PeakPrice, barLow);
                        mfeNow = (position.FillPrice - newPeak) / position.FillPrice;
                        position.PeakPrice = newPeak;
                    }
                    position.Mfe = Math.Max(position.Mfe, mfeNow);

                    bool exitTriggered = false;
                    double exitPrice = 0;
                    string exitReason = null;
                    double preBarStop = position.StopPrice;

                    // Stop check (uses the stop as it stood at bar open)
                    if (position.Side == "long")
                    {
                        if (barLow <= preBarStop) { exitTriggered = true; exitPrice = preBarStop; exitReason = "stop"; }
                    }
                    else
                    {
                        if (barHigh >= preBarStop) { exitTriggered = true; exitPrice = preBarStop; exitReason = "stop"; }
                    }

                    if (!exitTriggered)
                    {
                        double mfe = position.Mfe;
                        if (positionType == "trend")
                        {
                            if (position.Side == "long")
                            {
                                double beLevel = position.FillPrice * (1 + prm.TrendBeOffset);
                                if (mfe >= prm.TrendTrailTrigger)
                                {
                                    double lockf = mfe * prm.TrendTrailLockFraction;
                                    position.StopPrice = Math.Max(position.StopPrice, position.FillPrice * (1 + lockf));
                                }
                                else if (mfe >= prm.TrendBeTrigger)
                                {
                                    position.StopPrice = Math.Max(position.StopPrice, beLevel);
                                }
                                if (barHigh >= position.TargetPrice)
                                {
                                    exitTriggered = true; exitPrice = position.TargetPrice; exitReason = "target";
                                }
                            }
                            else
                            {
                                double beLevel = position.FillPrice * (1 - prm.TrendBeOffset);
                                if (mfe >= prm.TrendTrailTrigger)
                                {
                                    double lockf = mfe * prm.TrendTrailLockFraction;
                                    position.StopPrice = Math.Min(position.StopPrice, position.FillPrice * (1 - lockf));
                                }
                                else if (mfe >= prm.TrendBeTrigger)
                                {
                                    position.StopPrice = Math.Min(position.StopPrice, beLevel);
                                }
                                if (barLow <= position.TargetPrice)
                                {
                                    exitTriggered = true; exitPrice = position.TargetPrice; exitReason = "target";
                                }
                            }
                        }
                        else // chop
                        {
                            if (mfe >= prm.ChopBeTrigger && !position.BeEngaged)
                            {
                                if (position.Side == "long")
                                {
                                    double beStop = position.FillPrice * (1 + prm.ChopBeOffset);
                                    position.StopPrice = Math.Max(position.StopPrice, beStop);
                                }
                                else
                                {
                                    double beStop = position.FillPrice * (1 - prm.ChopBeOffset);
                                    position.StopPrice = Math.Min(position.StopPrice, beStop);
                                }
                                position.BeEngaged = true;
                            }

                            if (position.BeEngaged)
                            {
                                double lockPct = prm.ChopLockFraction * mfe;
                                if (position.Side == "long")
                                {
                                    double ratchet = position.FillPrice * (1 + lockPct);
                                    if (ratchet > position.StopPrice) position.StopPrice = ratchet;
                                }
                                else
                                {
                                    double ratchet = position.FillPrice * (1 - lockPct);
                                    if (ratchet < position.StopPrice) position.StopPrice = ratchet;
                                }
                            }

                            // Chop regime break
                            if (!exitTriggered)
                            {
                                int p4c = row.Pred4hClass;
                                if (position.Side == "long" && p4c == -1)
                                {
                                    exitTriggered = true;
                                    exitPrice = row.Close * (1 - prm.Slippage);
                                    exitReason = "regime_break";
                                }
                                else if (position.Side == "short" && p4c == 1)
                                {
                                    exitTriggered = true;
                                    exitPrice = row.Close * (1 + prm.Slippage);
                                    exitReason = "regime_break";
                                }
                            }
                        }
                    }

                    // Max hold (both strategies use 8 bars here)
                    int maxHold = prm.MaxHoldBars;
                    position.BarsSinceOpen += 1;
                    if (!exitTriggered && position.BarsSinceOpen >= maxHold)
                    {
                        exitTriggered = true;
                        exitPrice = row.Close * (position.Side == "long" ? 1 - prm.Slippage : 1 + prm.Slippage);
                        exitReason = "max_hold";
                    }

                    if (exitTriggered)
                    {
                        double grossRet = position.Side == "long"
                            ? (exitPrice - position.FillPrice) / position.FillPrice
                            : (position.FillPrice - exitPrice) / position.FillPrice;
                        double exitCost = position.Notional * prm.CostPerSide;
                        double netPnl = position.Notional * grossRet - position.EntryCost - exitCost;
                        capital += netPnl;

                        trades.Add(new BtTrade
                        {
                            EntryTime = position.EntryTime,
                            ExitTime = row.Ts,
                            Side = position.Side,
                            Strategy = positionType,
                            SignalType = position.SignalType ?? "chop",
                            FillPrice = position.FillPrice,
                            ExitPrice = exitPrice,
                            GrossRet = grossRet,
                            NetPnl = netPnl,
                            ExitReason = exitReason,
                            BarsHeld = position.BarsSinceOpen,
                            Notional = position.Notional,
                            CapitalAfter = capital,
                        });
                        position = null;
                        positionType = null;
                        // fall through to entry (same-bar re-entry allowed, matching Python)
                    }
                    else
                    {
                        skipEntry = true;
                    }
                }

                if (skipEntry) continue;

                // ── Entry logic (no position) ──
                int pred4hClass = row.Pred4hClass;
                int pred1hClass = row.Pred1hClass;

                // Try trend first (pred_4h != 0)
                if (pred4hClass != 0)
                {
                    string signal = GetSignalType(pred4hClass, pred1hClass);
                    if (signal != null && Math.Abs(row.Pred4h) >= prm.EvThreshold)
                    {
                        string side = signal.StartsWith("bull") ? "long" : "short";
                        double stopPct = prm.TrendStops[signal];
                        double targetPct = ComputeTargetPct(row.RollingSigma24, prm);
                        double fill = row.Close * (side == "long" ? 1 + prm.Slippage : 1 - prm.Slippage);
                        double notional = capital * prm.TrendCapitalRisk / stopPct;
                        double entryCost = notional * prm.CostPerSide;
                        double stopPrice, targetPrice;
                        if (side == "long")
                        {
                            stopPrice = fill * (1 - stopPct);
                            targetPrice = fill * (1 + targetPct);
                        }
                        else
                        {
                            stopPrice = fill * (1 + stopPct);
                            targetPrice = fill * (1 - targetPct);
                        }
                        position = new Position
                        {
                            Side = side,
                            SignalType = signal,
                            FillPrice = fill,
                            EntryTime = row.Ts,
                            StopPrice = stopPrice,
                            TargetPrice = targetPrice,
                            Notional = notional,
                            EntryCost = entryCost,
                            BarsSinceOpen = 0,
                            Mfe = 0,
                            Mae = 0,
                            PeakPrice = fill,
                        };
                        positionType = "trend";
                        continue;
                    }
                }

                // Try chop (Mode C, pred_4h == 0)
                if (pred4hClass == 0)
                {
                    int cc = row.ClassCombined;
                    string side = null;
                    if ((cc == 3 || cc == 4) && pred1hClass == 1) side = "long";
                    else if ((cc == 0 || cc == 1) && pred1hClass == -1) side = "short";

                    if (side != null)
                    {
                        double sigmaNow = row.RollingSigma24;
                        if (double.IsNaN(sigmaNow) || sigmaNow <= 0) sigmaNow = 0.004;
                        double stopPct = prm.ChopStopMult * sigmaNow;
                        stopPct = Math.Max(prm.ChopStopMin, Math.Min(prm.ChopStopMax, stopPct));
                        double fill = row.Close * (side == "long" ? 1 + prm.Slippage : 1 - prm.Slippage);
                        double notional = capital * prm.ChopCapitalRisk / stopPct;
                        double entryCost = notional * prm.CostPerSide;
                        double stopPrice = side == "long" ? fill * (1 - stopPct) : fill * (1 + stopPct);
                        position = new Position
                        {
                            Side = side,
                            SignalType = "chop",
                            FillPrice = fill,
                            EntryTime = row.Ts,
                            StopPrice = stopPrice,
                            TargetPrice = 0,
                            Notional = notional,
                            EntryCost = entryCost,
                            BarsSinceOpen = 0,
                            Mfe = 0,
                            Mae = 0,
                            PeakPrice = fill,
                            BeEngaged = false,
                        };
                        positionType = "chop";
                    }
                }
            }

            return trades;
        }

        private static string GetSignalType(int p4Class, int p1Class)
        {
            if (p4Class == 1 && p1Class == 1) return "bull_bull";
            if (p4Class == 1 && p1Class == 0) return "bull_neutral";
            if (p4Class == -1 && p1Class == -1) return "bear_bear";
            if (p4Class == -1 && p1Class == 0) return "bear_neutral";
            return null;
        }

        private static double ComputeTargetPct(double sigma1hNow, DualRegimeParams prm)
        {
            if (double.IsNaN(sigma1hNow) || sigma1hNow <= 0) return prm.TargetMinPct;
            double sigma4hEst = sigma1hNow * 2.0;
            double target = prm.TargetVolMultiplier * sigma4hEst;
            return Math.Max(prm.TargetMinPct, Math.Min(prm.TargetMaxPct, target));
        }

        // ─────────────────────────────────────────────────────────────
        //  Reporting
        // ─────────────────────────────────────────────────────────────
        private static void PrintReport(List<BtTrade> trades, double startCap, double endCap)
        {
            int n = trades.Count;
            int longs = trades.Count(t => t.Side == "long");
            int shorts = trades.Count(t => t.Side == "short");
            int trend = trades.Count(t => t.Strategy == "trend");
            int chop = trades.Count(t => t.Strategy == "chop");
            int wins = trades.Count(t => t.NetPnl > 0);
            double winRate = n > 0 ? 100.0 * wins / n : 0;
            double ret = startCap > 0 ? 100.0 * (endCap - startCap) / startCap : 0;

            Console.WriteLine("================================================================");
            Console.WriteLine("  C# DUAL-REGIME RESULTS");
            Console.WriteLine("================================================================");
            Console.WriteLine($"  Total trades:    {n}");
            Console.WriteLine($"  Long / Short:    {longs} / {shorts}");
            Console.WriteLine($"  Trend / Chop:    {trend} / {chop}");
            Console.WriteLine($"  Win rate:        {winRate:F1}%  ({wins}/{n})");
            Console.WriteLine($"  Start capital:   {startCap:F2}");
            Console.WriteLine($"  End capital:     {endCap:F2}");
            Console.WriteLine($"  Return:          {ret:+0.0;-0.0}%");
            Console.WriteLine();
            Console.WriteLine("  Signal types:");
            foreach (var g in trades.GroupBy(t => t.SignalType).OrderByDescending(g => g.Count()))
                Console.WriteLine($"    {g.Key,-14} {g.Count(),4}");
            Console.WriteLine();
            Console.WriteLine("  Exit reasons:");
            foreach (var g in trades.GroupBy(t => t.ExitReason).OrderByDescending(g => g.Count()))
                Console.WriteLine($"    {g.Key,-14} {g.Count(),4}   PnL: {g.Sum(x => x.NetPnl),10:F2}");
            Console.WriteLine("================================================================");
        }

        // ── Parse the Python reference trades and print a side-by-side table ──
        private static void CompareToReference(string refCsv, List<BtTrade> cs, double startCap, double endCap)
        {
            if (!File.Exists(refCsv))
            {
                Console.WriteLine($"  (reference trades CSV not found: {refCsv} — skipping comparison)");
                return;
            }

            // columns: entry_time,exit_time,side,strategy,signal_type,fill_price,
            //          exit_price,gross_ret,net_pnl,exit_reason,mfe,mae,bars_held,
            //          notional,capital_after
            var lines = File.ReadAllLines(refCsv);
            int rLong = 0, rShort = 0, rTrend = 0, rChop = 0, rWins = 0, rN = 0;
            double rLastCapital = startCap;
            var rSig = new Dictionary<string, int>();
            var rExit = new Dictionary<string, int>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var f = lines[i].Split(',');
                if (f.Length < 15) continue;
                rN++;
                string side = f[2], strat = f[3], sig = f[4], exit = f[9];
                if (side == "long") rLong++; else if (side == "short") rShort++;
                if (strat == "trend") rTrend++; else if (strat == "chop") rChop++;
                if (double.TryParse(f[8], NumberStyles.Any, CultureInfo.InvariantCulture, out double netPnl) && netPnl > 0) rWins++;
                if (double.TryParse(f[14], NumberStyles.Any, CultureInfo.InvariantCulture, out double capAfter)) rLastCapital = capAfter;
                rSig[sig] = rSig.GetValueOrDefault(sig) + 1;
                rExit[exit] = rExit.GetValueOrDefault(exit) + 1;
            }

            double rRet = startCap > 0 ? 100.0 * (rLastCapital - startCap) / startCap : 0;
            double rWinRate = rN > 0 ? 100.0 * rWins / rN : 0;

            int csN = cs.Count;
            int csLong = cs.Count(t => t.Side == "long");
            int csShort = cs.Count(t => t.Side == "short");
            int csTrend = cs.Count(t => t.Strategy == "trend");
            int csChop = cs.Count(t => t.Strategy == "chop");
            int csWins = cs.Count(t => t.NetPnl > 0);
            double csWinRate = csN > 0 ? 100.0 * csWins / csN : 0;
            double csRet = startCap > 0 ? 100.0 * (endCap - startCap) / startCap : 0;

            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine("  C#  vs  PYTHON  (reference: combined_trades.csv)");
            Console.WriteLine("================================================================");
            Console.WriteLine($"  {"Metric",-20} {"C#",12} {"Python",12}");
            Console.WriteLine("  ------------------------------------------------------------");
            Console.WriteLine($"  {"Total trades",-20} {csN,12} {rN,12}");
            Console.WriteLine($"  {"Long",-20} {csLong,12} {rLong,12}");
            Console.WriteLine($"  {"Short",-20} {csShort,12} {rShort,12}");
            Console.WriteLine($"  {"Trend",-20} {csTrend,12} {rTrend,12}");
            Console.WriteLine($"  {"Chop",-20} {csChop,12} {rChop,12}");
            Console.WriteLine($"  {"Win rate %",-20} {csWinRate,12:F1} {rWinRate,12:F1}");
            Console.WriteLine($"  {"Return %",-20} {csRet,12:F1} {rRet,12:F1}");
            Console.WriteLine("  ------------------------------------------------------------");
            Console.WriteLine("  Signal types (C# / Python):");
            foreach (var key in new[] { "bull_bull", "bull_neutral", "bear_bear", "bear_neutral", "chop" })
            {
                int c = cs.Count(t => t.SignalType == key);
                int rr = rSig.GetValueOrDefault(key);
                Console.WriteLine($"    {key,-14} {c,6} / {rr,-6}");
            }
            Console.WriteLine("  Exit reasons (C# / Python):");
            foreach (var key in new[] { "stop", "target", "max_hold", "regime_break" })
            {
                int c = cs.Count(t => t.ExitReason == key);
                int rr = rExit.GetValueOrDefault(key);
                Console.WriteLine($"    {key,-14} {c,6} / {rr,-6}");
            }
            Console.WriteLine("================================================================");
            Console.WriteLine("  NOTE: exact parity is not expected — the C# aggregator is causal");
            Console.WriteLine("  while the Python reference has mild lookahead in class_combined and");
            Console.WriteLine("  the 4H-regime merge. A balanced long/short split and comparable");
            Console.WriteLine("  trade count confirm the prior ~99%-short bug is fixed.");
            Console.WriteLine("================================================================");
        }

        private static void WriteTradesCsv(string path, List<BtTrade> trades)
        {
            using var w = new StreamWriter(path);
            w.WriteLine("entry_time,exit_time,side,strategy,signal_type,fill_price,exit_price,gross_ret,net_pnl,exit_reason,bars_held,notional,capital_after");
            foreach (var t in trades)
            {
                w.WriteLine(string.Join(",",
                    t.EntryTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    t.ExitTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    t.Side, t.Strategy, t.SignalType,
                    t.FillPrice.ToString("F2", CultureInfo.InvariantCulture),
                    t.ExitPrice.ToString("F2", CultureInfo.InvariantCulture),
                    t.GrossRet.ToString("F6", CultureInfo.InvariantCulture),
                    t.NetPnl.ToString("F2", CultureInfo.InvariantCulture),
                    t.ExitReason,
                    t.BarsHeld,
                    t.Notional.ToString("F2", CultureInfo.InvariantCulture),
                    t.CapitalAfter.ToString("F2", CultureInfo.InvariantCulture)));
            }
            Console.WriteLine($"  Trade CSV written: {path}");
        }

        // ─────────────────────────────────────────────────────────────
        //  CSV / model-dir helpers
        // ─────────────────────────────────────────────────────────────
        private static readonly string[] TsFormats =
        {
            "yyyy-MM-dd HH:mm:ss.ffffff",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
        };

        /// <summary>
        /// Loads the raw 15M CSV using Python's column mapping. The file has NO
        /// header; columns are:
        ///   0 close_time, 1 open_time, 2 open, 3 high, 4 low, 5 close,
        ///   6 volume_btc, 7 volume_quote, 8 trades, 9 symbol, 10 tf_id.
        /// Python uses timestamp = open_time and volume = volume_btc, so each
        /// candle's CloseTime is set to open_time to keep the aggregator's 1H
        /// bar timestamp (and therefore hour/weekend volume normalisation)
        /// aligned with the reference.
        /// </summary>
        private static List<ExchangeCandlestick> LoadCsv15M(string path)
        {
            var list = new List<ExchangeCandlestick>();
            foreach (var raw in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var f = raw.Split(',');
                if (f.Length < 7) continue;

                if (!TryParseTs(f[1], out DateTime ts)) continue;
                if (!decimal.TryParse(f[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var open)) continue;
                if (!decimal.TryParse(f[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var high)) continue;
                if (!decimal.TryParse(f[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var low)) continue;
                if (!decimal.TryParse(f[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var close)) continue;
                decimal.TryParse(f[6], NumberStyles.Any, CultureInfo.InvariantCulture, out var vol);

                list.Add(new ExchangeCandlestick
                {
                    Symbol = "BTCUSDT",
                    Interval = CandleInterval.Minute_15,
                    OpenTime = ts,
                    CloseTime = ts,    // Python timestamp convention (open_time)
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = vol,
                    IsClosed = true,
                });
            }
            // Sort by timestamp to mirror Python's sort_values("timestamp").
            list.Sort((a, b) => a.CloseTime.CompareTo(b.CloseTime));
            return list;
        }

        private static bool TryParseTs(string s, out DateTime ts)
        {
            s = s.Trim();
            if (DateTime.TryParseExact(s, TsFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out ts))
                return true;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out ts);
        }

        private static string ResolveModelDir(string overrideDir)
        {
            if (!string.IsNullOrWhiteSpace(overrideDir) && Directory.Exists(overrideDir))
                return overrideDir;

            var env = Environment.GetEnvironmentVariable("DUAL_REGIME_MODEL_DIR");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
                return env;

            // Known export location next to the Python reference.
            var known = @"C:\Temp\regime-sim\model_artifact";
            if (Directory.Exists(known))
                return known;

            // App-relative fallback (matches the algorithm's resolver).
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var rel = Path.Combine(baseDir, "Models", "DualRegime");
            if (Directory.Exists(rel))
                return rel;

            throw new DirectoryNotFoundException(
                "Could not resolve the DualRegime model dir. Pass --model-dir or set DUAL_REGIME_MODEL_DIR.");
        }
    }
}
