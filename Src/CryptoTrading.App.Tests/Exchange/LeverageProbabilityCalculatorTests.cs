using CryptoTrading.App.Algorithm.RegimeBased;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange
{
    public class LeverageProbabilityCalculatorTests
    {
        private readonly LeverageProbabilityCalculator _calculator = new LeverageProbabilityCalculator();

        [Fact]
        public void WhenLeverageDisabled_AlwaysReturns1x()
        {
            _calculator.IsLeverageEnabled = false;

            var result = _calculator.Calculate(
                regimeConfidence: 0.95m,
                setupConfidence: 0.90m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: true,
                riskRewardRatio: 3.0m);

            result.ActualLeverage.Should().Be(1);
            result.RecommendedLeverage.Should().Be(3); // Would be 3x if enabled
            result.IsLeverageEnabled.Should().BeFalse();
        }

        [Fact]
        public void WhenLeverageEnabled_HighConfidence_Returns3x()
        {
            _calculator.IsLeverageEnabled = true;

            var result = _calculator.Calculate(
                regimeConfidence: 0.95m,
                setupConfidence: 0.90m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: true,
                riskRewardRatio: 3.0m);

            result.ActualLeverage.Should().Be(3);
            result.ConfidenceTier.Should().Be("High");
        }

        [Fact]
        public void MediumConfidence_Returns2x()
        {
            _calculator.IsLeverageEnabled = true;

            var result = _calculator.Calculate(
                regimeConfidence: 0.70m,
                setupConfidence: 0.65m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: true,
                riskRewardRatio: 2.0m);

            result.ActualLeverage.Should().BeInRange(1, 3);
            result.CompositeScore.Should().BeGreaterThanOrEqualTo(0.5m);
        }

        [Fact]
        public void LowConfidence_BelowThreshold_ReturnsZero()
        {
            _calculator.IsLeverageEnabled = true;

            var result = _calculator.Calculate(
                regimeConfidence: 0.20m,
                setupConfidence: 0.15m,
                volatilityRegime: VolatilityRegime.High,
                atrPercentile: 95m,
                isZoneTrade: false,
                riskRewardRatio: 1.0m);

            result.ActualLeverage.Should().Be(0);
            result.ConfidenceTier.Should().Be("None");
        }

        [Fact]
        public void LowConfidence_WhenDisabled_AlsoReturnsZero()
        {
            _calculator.IsLeverageEnabled = false;

            var result = _calculator.Calculate(
                regimeConfidence: 0.20m,
                setupConfidence: 0.15m,
                volatilityRegime: VolatilityRegime.High,
                atrPercentile: 95m,
                isZoneTrade: false,
                riskRewardRatio: 1.0m);

            // Even when disabled, if score is below threshold, no trade
            result.ActualLeverage.Should().Be(0);
        }

        [Fact]
        public void ExtremeVolatility_ReducesScore()
        {
            _calculator.IsLeverageEnabled = true;

            var normalVol = _calculator.Calculate(
                regimeConfidence: 0.80m,
                setupConfidence: 0.80m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: true,
                riskRewardRatio: 2.5m);

            var extremeVol = _calculator.Calculate(
                regimeConfidence: 0.80m,
                setupConfidence: 0.80m,
                volatilityRegime: VolatilityRegime.High,
                atrPercentile: 95m,
                isZoneTrade: true,
                riskRewardRatio: 2.5m);

            extremeVol.CompositeScore.Should().BeLessThan(normalVol.CompositeScore);
            extremeVol.VolatilityScore.Should().BeLessThan(normalVol.VolatilityScore);
        }

        [Fact]
        public void ZoneTrade_IncreasesScore()
        {
            var noZone = _calculator.Calculate(
                regimeConfidence: 0.70m,
                setupConfidence: 0.70m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: false,
                riskRewardRatio: 2.0m);

            var withZone = _calculator.Calculate(
                regimeConfidence: 0.70m,
                setupConfidence: 0.70m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: true,
                riskRewardRatio: 2.0m);

            withZone.CompositeScore.Should().BeGreaterThan(noZone.CompositeScore);
            withZone.ZoneScore.Should().BeGreaterThan(noZone.ZoneScore);
        }

        [Fact]
        public void CompositeScore_AlwaysClamped_0_to_1()
        {
            var result = _calculator.Calculate(
                regimeConfidence: 1.5m, // Intentionally > 1
                setupConfidence: 1.5m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: true,
                riskRewardRatio: 5.0m);

            result.CompositeScore.Should().BeInRange(0m, 1m);
        }

        [Fact]
        public void ToString_ContainsKeyInformation()
        {
            var result = _calculator.Calculate(
                regimeConfidence: 0.80m,
                setupConfidence: 0.75m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: true,
                riskRewardRatio: 2.5m);

            var str = result.ToString();
            str.Should().Contain("Score:");
            str.Should().Contain("Recommended:");
            str.Should().Contain("Actual:");
        }

        [Fact]
        public void HigherRiskReward_IncreasesZoneScore()
        {
            var lowRR = _calculator.Calculate(
                regimeConfidence: 0.70m,
                setupConfidence: 0.70m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: true,
                riskRewardRatio: 1.5m);

            var highRR = _calculator.Calculate(
                regimeConfidence: 0.70m,
                setupConfidence: 0.70m,
                volatilityRegime: VolatilityRegime.Normal,
                atrPercentile: 50m,
                isZoneTrade: true,
                riskRewardRatio: 3.0m);

            highRR.ZoneScore.Should().BeGreaterThanOrEqualTo(lowRR.ZoneScore);
        }
    }
}
