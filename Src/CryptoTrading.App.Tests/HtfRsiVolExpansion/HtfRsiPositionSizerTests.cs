using CryptoTrading.App.Algorithm.HtfRsiVolExpansion;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    /// <summary>
    /// Pins the bankruptcy / equity-floor guard and the basic sizing math.
    /// The headline rule: if equity ≤ 0 the sizer must skip with a clear
    /// reason instead of returning a zero/negative quantity that downstream
    /// code might still try to act on.
    /// </summary>
    public class HtfRsiPositionSizerTests
    {
        #region Sizing Math

        [Fact]
        public void Compute_BasicCase_ReturnsNotionalDividedByPrice()
        {
            // 100,000 USDT × 5 leverage / 50,000 price = 10 BTC
            var r = HtfRsiPositionSizer.Compute(
                equity: 100_000m, leverage: 5, entryPrice: 50_000m);

            r.Ok.Should().BeTrue();
            r.Quantity.Should().Be(10m);
            r.SkipReason.Should().BeNull();
        }

        [Fact]
        public void Compute_OneXLeverage_BehavesAsSpot()
        {
            // 50,000 USDT × 1 / 50,000 = 1 BTC
            var r = HtfRsiPositionSizer.Compute(
                equity: 50_000m, leverage: 1, entryPrice: 50_000m);

            r.Ok.Should().BeTrue();
            r.Quantity.Should().Be(1m);
        }

        #endregion

        #region Equity Floor (bankruptcy guard)

        [Fact]
        public void Compute_ZeroEquity_Skips_WithBankruptcyReason()
        {
            var r = HtfRsiPositionSizer.Compute(
                equity: 0m, leverage: 5, entryPrice: 50_000m);

            r.Ok.Should().BeFalse();
            r.Quantity.Should().Be(0m);
            r.SkipReason.Should().Contain("non-positive");
        }

        [Fact]
        public void Compute_NegativeEquity_Skips_WithBankruptcyReason()
        {
            var r = HtfRsiPositionSizer.Compute(
                equity: -1m, leverage: 5, entryPrice: 50_000m);

            r.Ok.Should().BeFalse();
            r.SkipReason.Should().Contain("non-positive");
        }

        [Fact]
        public void Compute_DeeplyNegativeEquity_Skips_BeforeMinQuantityCheck()
        {
            // Without the equity floor a deeply negative balance would still
            // be caught by the minimum-quantity guard (since negative qty is
            // below any positive minimum) — but the skip reason would be
            // misleading. This test pins that bankruptcy is reported as the
            // root cause, not "below minimum quantity".
            var r = HtfRsiPositionSizer.Compute(
                equity: -1_000_000m,
                leverage: 5,
                entryPrice: 50_000m,
                minimumQuantity: 0.001m);

            r.Ok.Should().BeFalse();
            r.SkipReason.Should().Contain("non-positive");
            r.SkipReason.Should().NotContain("below");
        }

        #endregion

        #region Other guards

        [Fact]
        public void Compute_ZeroLeverage_Skips()
        {
            var r = HtfRsiPositionSizer.Compute(
                equity: 100_000m, leverage: 0, entryPrice: 50_000m);

            r.Ok.Should().BeFalse();
            r.SkipReason.Should().Contain("Leverage");
        }

        [Fact]
        public void Compute_NegativeLeverage_Skips()
        {
            var r = HtfRsiPositionSizer.Compute(
                equity: 100_000m, leverage: -3, entryPrice: 50_000m);

            r.Ok.Should().BeFalse();
            r.SkipReason.Should().Contain("Leverage");
        }

        [Fact]
        public void Compute_ZeroPrice_Skips()
        {
            var r = HtfRsiPositionSizer.Compute(
                equity: 100_000m, leverage: 5, entryPrice: 0m);

            r.Ok.Should().BeFalse();
            r.SkipReason.Should().Contain("price");
        }

        #endregion

        #region Exchange filters

        [Fact]
        public void Compute_BelowMinimumQuantity_Skips()
        {
            // 1 USDT × 5 / 50,000 = 0.0001 BTC, below 0.001 minimum.
            var r = HtfRsiPositionSizer.Compute(
                equity: 1m,
                leverage: 5,
                entryPrice: 50_000m,
                minimumQuantity: 0.001m);

            r.Ok.Should().BeFalse();
            r.SkipReason.Should().Contain("below exchange minimum");
        }

        [Fact]
        public void Compute_AtMinimumQuantity_Allows()
        {
            // 10 USDT × 5 / 50,000 = 0.001 BTC, exactly at minimum.
            var r = HtfRsiPositionSizer.Compute(
                equity: 10m,
                leverage: 5,
                entryPrice: 50_000m,
                minimumQuantity: 0.001m);

            r.Ok.Should().BeTrue();
            r.Quantity.Should().Be(0.001m);
        }

        [Fact]
        public void Compute_RoundsDown_ToQuantityIncrement()
        {
            // 1234 USDT × 5 / 50,000 = 0.1234 BTC.
            // With 0.001 increment this rounds DOWN to 0.123.
            var r = HtfRsiPositionSizer.Compute(
                equity: 1234m,
                leverage: 5,
                entryPrice: 50_000m,
                quantityIncrement: 0.001m);

            r.Ok.Should().BeTrue();
            r.Quantity.Should().Be(0.123m);
        }

        [Fact]
        public void Compute_IncrementRoundsToZero_Skips()
        {
            // 0.5 USDT × 5 / 50,000 = 0.00005 BTC. With 0.001 increment that
            // floors to 0 — must skip rather than emit a zero-qty trade.
            var r = HtfRsiPositionSizer.Compute(
                equity: 0.5m,
                leverage: 5,
                entryPrice: 50_000m,
                quantityIncrement: 0.001m);

            r.Ok.Should().BeFalse();
            r.SkipReason.Should().Contain("zero");
        }

        [Fact]
        public void Compute_NoExchangeFilters_AllowsExactQuantity()
        {
            // 100,000 USDT × 5 / 33,333 = ~15.000150 BTC.
            // No min, no increment → return raw computed value.
            var r = HtfRsiPositionSizer.Compute(
                equity: 100_000m, leverage: 5, entryPrice: 33_333m);

            r.Ok.Should().BeTrue();
            r.Quantity.Should().BeGreaterThan(15m).And.BeLessThan(15.01m);
        }

        #endregion
    }
}
