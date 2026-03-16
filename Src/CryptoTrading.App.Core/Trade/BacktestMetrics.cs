using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core.Trade
{
    /// <summary>
    /// Calculates comprehensive backtesting metrics from a list of completed trades.
    /// Includes risk-adjusted return measures for strategy optimization.
    /// </summary>
    public class BacktestMetrics
    {
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public decimal WinRate { get; set; }
        public decimal TotalReturn { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal SharpeRatio { get; set; }
        public decimal SortinoRatio { get; set; }
        public decimal ProfitFactor { get; set; }
        public decimal Expectancy { get; set; }
        public decimal AverageWin { get; set; }
        public decimal AverageLoss { get; set; }
        public int MaxConsecutiveLosses { get; set; }
        public decimal AverageHoldingPeriodHours { get; set; }
        public decimal CalmarRatio { get; set; }

        /// <summary>
        /// Calculate metrics from a list of trade P&L values.
        /// </summary>
        /// <param name="tradePnLs">List of profit/loss values per trade</param>
        /// <param name="holdingPeriodsHours">Optional list of holding periods in hours</param>
        /// <param name="annualRiskFreeRate">Annual risk-free rate (default 0.05 = 5%)</param>
        /// <returns>Computed BacktestMetrics</returns>
        public static BacktestMetrics Calculate(
            IList<decimal> tradePnLs,
            IList<double> holdingPeriodsHours = null,
            decimal annualRiskFreeRate = 0.05m)
        {
            var metrics = new BacktestMetrics();

            if (tradePnLs == null || tradePnLs.Count == 0)
                return metrics;

            metrics.TotalTrades = tradePnLs.Count;
            metrics.WinningTrades = tradePnLs.Count(p => p > 0);
            metrics.LosingTrades = tradePnLs.Count(p => p < 0);
            metrics.WinRate = metrics.TotalTrades > 0
                ? (decimal)metrics.WinningTrades / metrics.TotalTrades
                : 0m;

            metrics.TotalReturn = tradePnLs.Sum();

            var wins = tradePnLs.Where(p => p > 0).ToList();
            var losses = tradePnLs.Where(p => p < 0).ToList();

            metrics.AverageWin = wins.Count > 0 ? wins.Average() : 0m;
            metrics.AverageLoss = losses.Count > 0 ? losses.Average() : 0m;

            // Profit Factor
            var grossProfit = wins.Sum();
            var grossLoss = Math.Abs(losses.Sum());
            metrics.ProfitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? decimal.MaxValue : 0m;

            // Expectancy per trade
            metrics.Expectancy = metrics.TotalTrades > 0 ? metrics.TotalReturn / metrics.TotalTrades : 0m;

            // Max Drawdown
            metrics.MaxDrawdown = CalculateMaxDrawdown(tradePnLs);

            // Max Consecutive Losses
            metrics.MaxConsecutiveLosses = CalculateMaxConsecutiveLosses(tradePnLs);

            // Sharpe Ratio (annualized, assuming ~252 trading days)
            var returns = tradePnLs.Select(p => (double)p).ToArray();
            var avgReturn = returns.Average();
            var stdDev = CalculateStdDev(returns);
            var dailyRiskFreeRate = (double)annualRiskFreeRate / 252.0;

            metrics.SharpeRatio = stdDev > 0
                ? (decimal)((avgReturn - dailyRiskFreeRate) / stdDev * Math.Sqrt(252))
                : 0m;

            // Sortino Ratio (uses downside deviation only)
            var downsideReturns = returns.Where(r => r < dailyRiskFreeRate).ToArray();
            var downsideDev = downsideReturns.Length > 0 ? CalculateStdDev(downsideReturns) : 0;
            metrics.SortinoRatio = downsideDev > 0
                ? (decimal)((avgReturn - dailyRiskFreeRate) / downsideDev * Math.Sqrt(252))
                : 0m;

            // Calmar Ratio
            metrics.CalmarRatio = metrics.MaxDrawdown > 0
                ? metrics.TotalReturn / metrics.MaxDrawdown
                : 0m;

            // Average Holding Period
            if (holdingPeriodsHours != null && holdingPeriodsHours.Count > 0)
            {
                metrics.AverageHoldingPeriodHours = (decimal)holdingPeriodsHours.Average();
            }

            return metrics;
        }

        private static decimal CalculateMaxDrawdown(IList<decimal> pnls)
        {
            decimal peak = 0m;
            decimal equity = 0m;
            decimal maxDrawdown = 0m;

            foreach (var pnl in pnls)
            {
                equity += pnl;
                if (equity > peak)
                    peak = equity;

                var drawdown = peak - equity;
                if (drawdown > maxDrawdown)
                    maxDrawdown = drawdown;
            }

            return maxDrawdown;
        }

        private static int CalculateMaxConsecutiveLosses(IList<decimal> pnls)
        {
            int maxConsecutive = 0;
            int current = 0;

            foreach (var pnl in pnls)
            {
                if (pnl < 0)
                {
                    current++;
                    if (current > maxConsecutive)
                        maxConsecutive = current;
                }
                else
                {
                    current = 0;
                }
            }

            return maxConsecutive;
        }

        private static double CalculateStdDev(double[] values)
        {
            if (values.Length < 2) return 0;
            var avg = values.Average();
            var sumSquares = values.Sum(v => (v - avg) * (v - avg));
            return Math.Sqrt(sumSquares / (values.Length - 1));
        }

        public override string ToString()
        {
            return $"Trades: {TotalTrades} | Win Rate: {WinRate:P1} | Return: {TotalReturn:F2} | " +
                   $"Max DD: {MaxDrawdown:F2} | Sharpe: {SharpeRatio:F2} | Sortino: {SortinoRatio:F2} | " +
                   $"PF: {ProfitFactor:F2} | Expectancy: {Expectancy:F2} | " +
                   $"Avg Win: {AverageWin:F2} | Avg Loss: {AverageLoss:F2} | " +
                   $"Max Consec Losses: {MaxConsecutiveLosses} | Calmar: {CalmarRatio:F2}";
        }
    }
}
