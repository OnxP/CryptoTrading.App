using System.Collections.Generic;
using CryptoTrading.App.Core.Trade;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Trade
{
    public class BacktestMetricsTests
    {
        [Fact]
        public void Calculate_EmptyTrades_ReturnsZeroMetrics()
        {
            var metrics = BacktestMetrics.Calculate(new List<decimal>());

            metrics.TotalTrades.Should().Be(0);
            metrics.WinRate.Should().Be(0);
            metrics.TotalReturn.Should().Be(0);
        }

        [Fact]
        public void Calculate_AllWins_100PercentWinRate()
        {
            var pnls = new List<decimal> { 100m, 200m, 150m };
            var metrics = BacktestMetrics.Calculate(pnls);

            metrics.TotalTrades.Should().Be(3);
            metrics.WinningTrades.Should().Be(3);
            metrics.LosingTrades.Should().Be(0);
            metrics.WinRate.Should().Be(1.0m);
            metrics.TotalReturn.Should().Be(450m);
        }

        [Fact]
        public void Calculate_MixedTrades_CorrectWinRate()
        {
            var pnls = new List<decimal> { 100m, -50m, 200m, -30m, 150m };
            var metrics = BacktestMetrics.Calculate(pnls);

            metrics.TotalTrades.Should().Be(5);
            metrics.WinningTrades.Should().Be(3);
            metrics.LosingTrades.Should().Be(2);
            metrics.WinRate.Should().Be(0.6m);
        }

        [Fact]
        public void Calculate_ProfitFactor_CorrectRatio()
        {
            var pnls = new List<decimal> { 100m, -50m, 200m, -50m };
            var metrics = BacktestMetrics.Calculate(pnls);

            // Gross profit = 300, Gross loss = 100
            metrics.ProfitFactor.Should().Be(3.0m);
        }

        [Fact]
        public void Calculate_MaxDrawdown_CorrectValue()
        {
            var pnls = new List<decimal> { 100m, -50m, -80m, 200m, -30m };
            var metrics = BacktestMetrics.Calculate(pnls);

            // Equity curve: 100, 50, -30, 170, 140
            // Peak at 100, trough at -30 => DD = 130
            metrics.MaxDrawdown.Should().Be(130m);
        }

        [Fact]
        public void Calculate_MaxConsecutiveLosses_CorrectCount()
        {
            var pnls = new List<decimal> { 100m, -50m, -30m, -20m, 200m, -10m };
            var metrics = BacktestMetrics.Calculate(pnls);

            metrics.MaxConsecutiveLosses.Should().Be(3);
        }

        [Fact]
        public void Calculate_Expectancy_CorrectPerTrade()
        {
            var pnls = new List<decimal> { 100m, -50m, 200m, -50m, 100m };
            var metrics = BacktestMetrics.Calculate(pnls);

            // Total = 300, Trades = 5
            metrics.Expectancy.Should().Be(60m);
        }

        [Fact]
        public void Calculate_AverageWinLoss_CorrectValues()
        {
            var pnls = new List<decimal> { 100m, -50m, 200m, -30m };
            var metrics = BacktestMetrics.Calculate(pnls);

            metrics.AverageWin.Should().Be(150m);   // (100 + 200) / 2
            metrics.AverageLoss.Should().Be(-40m);   // (-50 + -30) / 2
        }

        [Fact]
        public void Calculate_SharpeRatio_IsNonZeroForProfitableTrades()
        {
            var pnls = new List<decimal> { 100m, 50m, 150m, 80m, 120m };
            var metrics = BacktestMetrics.Calculate(pnls);

            metrics.SharpeRatio.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Calculate_CalmarRatio_ReturnOverDrawdown()
        {
            var pnls = new List<decimal> { 100m, -50m, 200m };
            var metrics = BacktestMetrics.Calculate(pnls);

            // Total return = 250, Max DD = 50
            metrics.CalmarRatio.Should().Be(5.0m);
        }

        [Fact]
        public void ToString_ContainsKeyMetrics()
        {
            var pnls = new List<decimal> { 100m, -50m, 200m };
            var metrics = BacktestMetrics.Calculate(pnls);

            var output = metrics.ToString();
            output.Should().Contain("Trades:");
            output.Should().Contain("Win Rate:");
            output.Should().Contain("Sharpe:");
            output.Should().Contain("PF:");
        }
    }
}
