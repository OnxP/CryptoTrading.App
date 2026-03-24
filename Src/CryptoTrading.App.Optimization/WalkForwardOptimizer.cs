using System;
using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Optimization
{
    /// <summary>
    /// Walk-forward optimizer that splits data into rolling windows:
    /// in-sample (training) and out-of-sample (validation).
    /// Prevents overfitting by validating parameters on unseen data.
    /// </summary>
    public class WalkForwardOptimizer
    {
        private readonly ParameterSpace _parameterSpace;
        private readonly Func<IDictionary<string, double>, DateTime, DateTime, BacktestMetrics> _runBacktest;
        private readonly Func<BacktestMetrics, decimal> _objectiveFunction;

        public int InSampleMonths { get; set; } = 6;
        public int OutOfSampleMonths { get; set; } = 2;
        public int StepMonths { get; set; } = 2;

        public List<WalkForwardWindow> Windows { get; } = new List<WalkForwardWindow>();

        /// <summary>
        /// Create a walk-forward optimizer.
        /// </summary>
        /// <param name="parameterSpace">Parameter ranges to search</param>
        /// <param name="runBacktest">Function that runs a backtest for a date range and returns metrics</param>
        /// <param name="objectiveFunction">Scoring function (higher is better)</param>
        public WalkForwardOptimizer(
            ParameterSpace parameterSpace,
            Func<IDictionary<string, double>, DateTime, DateTime, BacktestMetrics> runBacktest,
            Func<BacktestMetrics, decimal> objectiveFunction = null)
        {
            _parameterSpace = parameterSpace;
            _runBacktest = runBacktest;
            _objectiveFunction = objectiveFunction ?? (m => m.SharpeRatio);
        }

        /// <summary>
        /// Run walk-forward optimization over a date range.
        /// </summary>
        public void Run(DateTime startDate, DateTime endDate)
        {
            Windows.Clear();

            var windowStart = startDate;
            while (windowStart.AddMonths(InSampleMonths + OutOfSampleMonths) <= endDate)
            {
                var inSampleEnd = windowStart.AddMonths(InSampleMonths);
                var outOfSampleEnd = inSampleEnd.AddMonths(OutOfSampleMonths);

                // Grid search on in-sample period
                var inSampleOptimizer = new GridSearchOptimizer(
                    _parameterSpace,
                    paramSet => _runBacktest(paramSet, windowStart, inSampleEnd),
                    _objectiveFunction);

                var bestInSample = inSampleOptimizer.Run();

                // Validate best parameters on out-of-sample period
                BacktestMetrics outOfSampleMetrics = null;
                if (bestInSample != null)
                {
                    outOfSampleMetrics = _runBacktest(bestInSample.Parameters, inSampleEnd, outOfSampleEnd);
                }

                Windows.Add(new WalkForwardWindow
                {
                    InSampleStart = windowStart,
                    InSampleEnd = inSampleEnd,
                    OutOfSampleStart = inSampleEnd,
                    OutOfSampleEnd = outOfSampleEnd,
                    BestParameters = bestInSample?.Parameters,
                    InSampleMetrics = bestInSample?.Metrics,
                    OutOfSampleMetrics = outOfSampleMetrics,
                    InSampleScore = bestInSample?.Score ?? 0
                });

                windowStart = windowStart.AddMonths(StepMonths);
            }
        }

        /// <summary>
        /// Get the efficiency ratio: how well in-sample results predict out-of-sample.
        /// Ratio > 0.5 suggests parameters are not overfit.
        /// </summary>
        public decimal GetEfficiencyRatio()
        {
            if (Windows.Count == 0) return 0;

            var windowsWithResults = Windows
                .Where(w => w.InSampleMetrics != null && w.OutOfSampleMetrics != null)
                .ToList();

            if (windowsWithResults.Count == 0) return 0;

            var avgInSample = windowsWithResults.Average(w => _objectiveFunction(w.InSampleMetrics));
            var avgOutOfSample = windowsWithResults.Average(w => _objectiveFunction(w.OutOfSampleMetrics));

            return avgInSample != 0 ? avgOutOfSample / avgInSample : 0;
        }
    }

    public class WalkForwardWindow
    {
        public DateTime InSampleStart { get; set; }
        public DateTime InSampleEnd { get; set; }
        public DateTime OutOfSampleStart { get; set; }
        public DateTime OutOfSampleEnd { get; set; }
        public IDictionary<string, double> BestParameters { get; set; }
        public BacktestMetrics InSampleMetrics { get; set; }
        public BacktestMetrics OutOfSampleMetrics { get; set; }
        public decimal InSampleScore { get; set; }
    }
}
