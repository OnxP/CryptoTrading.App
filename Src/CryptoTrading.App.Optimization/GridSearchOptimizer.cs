using System;
using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Optimization
{
    /// <summary>
    /// Grid search optimizer that enumerates all parameter combinations
    /// and ranks them by an objective function (default: Sharpe ratio).
    /// </summary>
    public class GridSearchOptimizer
    {
        private readonly ParameterSpace _parameterSpace;
        private readonly Func<IDictionary<string, double>, BacktestMetrics> _runBacktest;
        private readonly Func<BacktestMetrics, decimal> _objectiveFunction;

        public List<OptimizationResult> Results { get; } = new List<OptimizationResult>();

        /// <summary>
        /// Create a grid search optimizer.
        /// </summary>
        /// <param name="parameterSpace">Parameter ranges to search</param>
        /// <param name="runBacktest">Function that runs a backtest with given parameters and returns metrics</param>
        /// <param name="objectiveFunction">Function that scores metrics (higher is better). Default: Sharpe ratio</param>
        public GridSearchOptimizer(
            ParameterSpace parameterSpace,
            Func<IDictionary<string, double>, BacktestMetrics> runBacktest,
            Func<BacktestMetrics, decimal> objectiveFunction = null)
        {
            _parameterSpace = parameterSpace;
            _runBacktest = runBacktest;
            _objectiveFunction = objectiveFunction ?? (m => m.SharpeRatio);
        }

        /// <summary>
        /// Run the optimization, testing all parameter combinations.
        /// </summary>
        public OptimizationResult Run()
        {
            Results.Clear();
            var totalCombinations = _parameterSpace.TotalCombinations;
            int completed = 0;

            foreach (var paramSet in _parameterSpace.EnumerateAll())
            {
                try
                {
                    var metrics = _runBacktest(paramSet);
                    var score = _objectiveFunction(metrics);

                    Results.Add(new OptimizationResult
                    {
                        Parameters = new Dictionary<string, double>(paramSet),
                        Metrics = metrics,
                        Score = score
                    });
                }
                catch (Exception ex)
                {
                    // Skip failed parameter combinations
                    Console.WriteLine($"Parameter set failed: {ex.Message}");
                }

                completed++;
                if (completed % 100 == 0)
                {
                    Console.WriteLine($"Progress: {completed}/{totalCombinations} ({100.0 * completed / totalCombinations:F1}%)");
                }
            }

            Results.Sort((a, b) => b.Score.CompareTo(a.Score));
            return Results.FirstOrDefault();
        }

        /// <summary>
        /// Get top N results ranked by objective function.
        /// </summary>
        public List<OptimizationResult> GetTopResults(int count)
        {
            return Results.Take(count).ToList();
        }
    }

    public class OptimizationResult
    {
        public IDictionary<string, double> Parameters { get; set; }
        public BacktestMetrics Metrics { get; set; }
        public decimal Score { get; set; }

        public override string ToString()
        {
            var paramStr = string.Join(", ", Parameters.Select(kv => $"{kv.Key}={kv.Value:F3}"));
            return $"Score: {Score:F4} | {Metrics} | Params: [{paramStr}]";
        }
    }
}
