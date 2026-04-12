using Binance;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using FluentAssertions;
using Moq;
using Xunit;

namespace CryptoTrading.App.Tests.HtfRsiVolExpansion
{
    public class LeveragedBalanceCheckTests
    {
        private Positions CreatePositionsWithUsdt(decimal usdtBalance)
        {
            var logger = new Mock<Microsoft.Extensions.Logging.ILogger<Positions>>();
            var factory = new Mock<ITradeFactory>();
            var dict = new Dictionary<string, IPosition>
            {
                ["BTC"] = new Position("BTC", 0m),
                ["USDT"] = new Position("USDT", usdtBalance),
                ["BNB"] = new Position("BNB", 1m)
            };
            return new Positions(logger.Object, factory.Object, dict);
        }

        private Mock<ITradeRequest> CreateMockRequest(
            OrderSide side, decimal amount, int leverage,
            string baseSymbol = "BTC", string quoteSymbol = "USDT")
        {
            var mock = new Mock<ITradeRequest>();
            mock.Setup(r => r.OrderSide).Returns(side);
            mock.Setup(r => r.Amount).Returns(amount);
            mock.Setup(r => r.Leverage).Returns(leverage);
            mock.Setup(r => r.BaseSymbol).Returns(baseSymbol);
            mock.Setup(r => r.QuoteSymbol).Returns(quoteSymbol);
            return mock;
        }

        #region Leveraged Buy (Long)

        [Fact]
        public void LeveragedBuy_ChecksQuoteAsset_NotBase()
        {
            var positions = CreatePositionsWithUsdt(50_000m);

            // 5x leveraged Buy: Amount = 200,000 USDT notional, margin = 40,000
            var request = CreateMockRequest(OrderSide.Buy, 200_000m, 5);

            var result = positions.CheckHasEnoughBalance(request.Object);

            result.Should().BeTrue(
                "margin (200k/5 = 40k) is within 50k USDT balance");
        }

        [Fact]
        public void LeveragedBuy_FailsWhenMarginExceedsBalance()
        {
            var positions = CreatePositionsWithUsdt(10_000m);

            // 5x leveraged: Amount = 200,000, margin = 40,000 > 10,000
            var request = CreateMockRequest(OrderSide.Buy, 200_000m, 5);

            var result = positions.CheckHasEnoughBalance(request.Object);

            result.Should().BeFalse(
                "margin (40k) exceeds 10k USDT balance");
        }

        #endregion

        #region Leveraged Sell (Short)

        [Fact]
        public void LeveragedSell_ChecksQuoteAsset_NotBase()
        {
            var positions = CreatePositionsWithUsdt(50_000m);

            // 5x leveraged Sell: Amount = 200,000 USDT notional, margin = 40,000
            // Before fix, this would check BTC balance (0) and fail
            var request = CreateMockRequest(OrderSide.Sell, 200_000m, 5);

            var result = positions.CheckHasEnoughBalance(request.Object);

            result.Should().BeTrue(
                "leveraged Sell should check USDT collateral, not BTC balance");
        }

        [Fact]
        public void LeveragedSell_FailsWhenMarginExceedsBalance()
        {
            var positions = CreatePositionsWithUsdt(10_000m);

            var request = CreateMockRequest(OrderSide.Sell, 200_000m, 5);

            var result = positions.CheckHasEnoughBalance(request.Object);

            result.Should().BeFalse(
                "margin (40k) exceeds 10k USDT balance");
        }

        #endregion

        #region Spot (Non-Leveraged) Unchanged Behavior

        [Fact]
        public void SpotBuy_ChecksQuoteAsset()
        {
            var positions = CreatePositionsWithUsdt(50_000m);

            var request = CreateMockRequest(OrderSide.Buy, 10_000m, 1);

            var result = positions.CheckHasEnoughBalance(request.Object);

            result.Should().BeTrue();
        }

        [Fact]
        public void SpotSell_ChecksBaseAsset()
        {
            var positions = CreatePositionsWithUsdt(50_000m);

            // BTC balance is 0 — spot sell should fail
            var request = CreateMockRequest(OrderSide.Sell, 1m, 1);

            var result = positions.CheckHasEnoughBalance(request.Object);

            result.Should().BeFalse(
                "spot Sell requires base asset (BTC) balance");
        }

        #endregion

        #region CheckRequest Integration

        [Fact]
        public void CheckRequest_LeveragedShort_PassesWithSufficientUsdt()
        {
            // Margin = 500k / 5 = 100k. With 2% fee buffer: needs > 102k USDT.
            var positions = CreatePositionsWithUsdt(110_000m);

            // Simulates the actual trade: Short BTCUSDT at 5x, ~500k notional
            var request = CreateMockRequest(OrderSide.Sell, 500_000m, 5);

            var result = positions.CheckRequest(request.Object);

            result.Should().BeTrue(
                "margin (100k) + 2% fee buffer fits within 110k USDT balance");
        }

        #endregion
    }
}
