using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Optimization
{
    /// <summary>
    /// Tracks optimization run results over time to measure whether
    /// the ML/optimization pipeline is actually improving performance.
    /// Each run is appended to a JSON-lines log file for easy comparison.
    /// </summary>
    public class OptimizationHistory
    {
        private readonly string _historyFilePath;

        public OptimizationHistory(string historyFilePath = null)
        {
            _historyFilePath = historyFilePath
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "optimization_history.jsonl");
        }

        /// <summary>
        /// Record the result of an optimization run.
        /// </summary>
        public void RecordRun(OptimizationRunRecord record)
        {
            var json = JsonSerializer.Serialize(record);
            File.AppendAllText(_historyFilePath, json + Environment.NewLine);
        }

        /// <summary>
        /// Record a run from walk-forward results.
        /// </summary>
        public void RecordWalkForwardRun(
            WalkForwardOptimizer optimizer,
            string objectiveName,
            string gitCommit = null)
        {
            var windows = optimizer.Windows;
            if (windows.Count == 0) return;

            var validWindows = windows
                .Where(w => w.OutOfSampleMetrics != null)
                .ToList();

            var record = new OptimizationRunRecord
            {
                Timestamp = DateTime.UtcNow,
                GitCommit = gitCommit ?? string.Empty,
                ObjectiveFunction = objectiveName,
                EfficiencyRatio = optimizer.GetEfficiencyRatio(),
                WindowCount = windows.Count,
                AvgInSampleSharpe = validWindows.Count > 0
                    ? validWindows.Average(w => w.InSampleMetrics?.SharpeRatio ?? 0)
                    : 0,
                AvgOutOfSampleSharpe = validWindows.Count > 0
                    ? validWindows.Average(w => w.OutOfSampleMetrics?.SharpeRatio ?? 0)
                    : 0,
                AvgInSampleWinRate = validWindows.Count > 0
                    ? validWindows.Average(w => w.InSampleMetrics?.WinRate ?? 0)
                    : 0,
                AvgOutOfSampleWinRate = validWindows.Count > 0
                    ? validWindows.Average(w => w.OutOfSampleMetrics?.WinRate ?? 0)
                    : 0,
                AvgInSampleProfitFactor = validWindows.Count > 0
                    ? validWindows.Average(w => w.InSampleMetrics?.ProfitFactor ?? 0)
                    : 0,
                AvgOutOfSampleProfitFactor = validWindows.Count > 0
                    ? validWindows.Average(w => w.OutOfSampleMetrics?.ProfitFactor ?? 0)
                    : 0,
                TotalOutOfSampleTrades = validWindows.Sum(w => w.OutOfSampleMetrics?.TotalTrades ?? 0),
                BestParameters = validWindows.LastOrDefault()?.BestParameters
                    ?? new Dictionary<string, double>()
            };

            RecordRun(record);
        }

        /// <summary>
        /// Load all recorded runs.
        /// </summary>
        public List<OptimizationRunRecord> LoadHistory()
        {
            if (!File.Exists(_historyFilePath))
                return new List<OptimizationRunRecord>();

            return File.ReadAllLines(_historyFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<OptimizationRunRecord>(line))
                .Where(r => r != null)
                .ToList();
        }

        /// <summary>
        /// Generate a report showing whether the optimization is improving over time.
        /// Compares the most recent N runs against the earliest N runs.
        /// </summary>
        public string GenerateImprovementReport(int windowSize = 3)
        {
            var history = LoadHistory();
            if (history.Count < windowSize * 2)
                return $"Insufficient history ({history.Count} runs, need {windowSize * 2}) to assess improvement trend.";

            var early = history.Take(windowSize).ToList();
            var recent = history.Skip(history.Count - windowSize).ToList();

            var report = new System.Text.StringBuilder();
            report.AppendLine("=== OPTIMIZATION IMPROVEMENT REPORT ===");
            report.AppendLine($"Comparing first {windowSize} runs vs last {windowSize} runs ({history.Count} total runs)");
            report.AppendLine();

            AppendMetricComparison(report, "Efficiency Ratio",
                early.Average(r => r.EfficiencyRatio),
                recent.Average(r => r.EfficiencyRatio));

            AppendMetricComparison(report, "OOS Sharpe Ratio",
                early.Average(r => r.AvgOutOfSampleSharpe),
                recent.Average(r => r.AvgOutOfSampleSharpe));

            AppendMetricComparison(report, "OOS Win Rate",
                early.Average(r => r.AvgOutOfSampleWinRate),
                recent.Average(r => r.AvgOutOfSampleWinRate));

            AppendMetricComparison(report, "OOS Profit Factor",
                early.Average(r => r.AvgOutOfSampleProfitFactor),
                recent.Average(r => r.AvgOutOfSampleProfitFactor));

            // Overall verdict
            var earlyScore = early.Average(r => r.AvgOutOfSampleSharpe);
            var recentScore = recent.Average(r => r.AvgOutOfSampleSharpe);
            var earlyEff = early.Average(r => r.EfficiencyRatio);
            var recentEff = recent.Average(r => r.EfficiencyRatio);

            report.AppendLine();
            if (recentScore > earlyScore && recentEff > 0.5m)
                report.AppendLine("VERDICT: Optimization IS improving. OOS performance trending up with healthy efficiency ratio.");
            else if (recentScore > earlyScore && recentEff <= 0.5m)
                report.AppendLine("VERDICT: OVERFITTING RISK. OOS performance improving but efficiency ratio < 0.5 suggests in-sample overfitting.");
            else if (recentEff < earlyEff)
                report.AppendLine("VERDICT: Optimization is NOT improving. Efficiency ratio declining — parameters may be overfitting to in-sample data.");
            else
                report.AppendLine("VERDICT: Performance is STABLE. No significant improvement or degradation detected.");

            return report.ToString();
        }

        private static void AppendMetricComparison(System.Text.StringBuilder sb, string name, decimal early, decimal recent)
        {
            var change = early != 0 ? (recent - early) / Math.Abs(early) * 100 : 0;
            var arrow = recent > early ? "+" : recent < early ? "" : "=";
            sb.AppendLine($"  {name,-22} Early: {early,8:F4}  Recent: {recent,8:F4}  Change: {arrow}{change:F1}%");
        }
    }

    public class OptimizationRunRecord
    {
        public DateTime Timestamp { get; set; }
        public string GitCommit { get; set; }
        public string ObjectiveFunction { get; set; }
        public decimal EfficiencyRatio { get; set; }
        public int WindowCount { get; set; }
        public decimal AvgInSampleSharpe { get; set; }
        public decimal AvgOutOfSampleSharpe { get; set; }
        public decimal AvgInSampleWinRate { get; set; }
        public decimal AvgOutOfSampleWinRate { get; set; }
        public decimal AvgInSampleProfitFactor { get; set; }
        public decimal AvgOutOfSampleProfitFactor { get; set; }
        public int TotalOutOfSampleTrades { get; set; }
        public IDictionary<string, double> BestParameters { get; set; }
    }
}
