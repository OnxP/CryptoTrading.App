using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using Xunit;

namespace CryptoTrading.App.Tests.CorePosition
{
    /// <summary>
    /// Pins the wallet-level orchestration logic in Positions:
    ///   * GetPosition is the single allocator - unknown assets get a zero
    ///     balance, never null, so callers don't NPE.
    ///   * CheckHasOpenPositionAndVolume must only consult the BASE symbol;
    ///     a pending leg on quote (USDT) shouldn't block an unrelated trade.
    ///   * CreateTrade must lock the base position around the factory call
    ///     so a concurrent CheckRequest sees HasOpenPosition=true.
    ///   * CheckRequest is the AND of "enough balance" AND "no open
    ///     position" - either side failing must short-circuit.
    /// LeveragedBalanceCheckTests covers the leverage maths in
    /// CheckHasEnoughBalance; this file fills in the remaining surface.
    /// </summary>
    public class PositionsTests
    {
        private static (Positions positions, Mock<ITradeFactory> factory) Build(
            Dictionary<string, IPosition> dict = null)
        {
            var logger = new Mock<ILogger<Positions>>();
            var factory = new Mock<ITradeFactory>();
            var positions = dict == null
                ? new Positions(logger.Object, factory.Object)
                : new Positions(logger.Object, factory.Object, dict);
            return (positions, factory);
        }

        private static Mock<ITradeRequest> Request(
            ExchangeOrderSide side = ExchangeOrderSide.Buy,
            decimal amount = 1m,
            int leverage = 1,
            string baseSymbol = "BTC",
            string quoteSymbol = "USDT")
        {
            var m = new Mock<ITradeRequest>();
            m.Setup(r => r.OrderSide).Returns(side);
            m.Setup(r => r.Amount).Returns(amount);
            m.Setup(r => r.Leverage).Returns(leverage);
            m.Setup(r => r.BaseSymbol).Returns(baseSymbol);
            m.Setup(r => r.QuoteSymbol).Returns(quoteSymbol);
            return m;
        }

        #region GetPosition

        [Fact]
        public void GetPosition_KnownAsset_ReturnsSameInstance()
        {
            var btc = new Position("BTC", 5m);
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = btc,
                ["USDT"] = new Position("USDT", 1_000m)
            });

            positions.GetPosition("BTC").Should().BeSameAs(btc);
        }

        [Fact]
        public void GetPosition_UnknownAsset_LazilyCreatesZeroBalancePosition()
        {
            var (positions, _) = Build();

            var created = positions.GetPosition("ETH");

            created.Should().NotBeNull();
            created.Symbol.Should().Be("ETH");
            created.FreeAmount.Should().Be(0m);
        }

        [Fact]
        public void GetPosition_UnknownAsset_CachesCreatedPosition()
        {
            var (positions, _) = Build();

            var first = positions.GetPosition("ETH");
            var second = positions.GetPosition("ETH");

            second.Should().BeSameAs(first,
                "subsequent lookups must reuse the lazily-created instance");
        }

        #endregion

        #region CheckHasEnoughBalance edge cases not covered by LeveragedBalanceCheckTests

        [Fact]
        public void CheckHasEnoughBalance_QuoteSymbolMissing_ReturnsFalse()
        {
            // No USDT in dictionary at all - must not NPE, just return false.
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = new Position("BTC", 1m)
            });
            var req = Request(ExchangeOrderSide.Buy, 100m, leverage: 1);

            positions.CheckHasEnoughBalance(req.Object).Should().BeFalse();
        }

        [Fact]
        public void CheckHasEnoughBalance_SpotBuy_AppliesTwoPercentFeeBuffer_OnUsdt()
        {
            // Spot Buy on BTCUSDT: balance symbol is USDT (the fee asset),
            // so the +2% fee buffer must apply. 100 USDT, ask for 99 -> 99 *
            // 1.02 = 100.98 > 100 -> fail.
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = new Position("BTC", 0m),
                ["USDT"] = new Position("USDT", 100m)
            });
            var req = Request(ExchangeOrderSide.Buy, 99m, leverage: 1);

            positions.CheckHasEnoughBalance(req.Object).Should().BeFalse(
                "spot Buy on the fee asset must reserve a 2% fee buffer");
        }

        [Fact]
        public void CheckHasEnoughBalance_SpotBuy_PassesWhenWithinFeeBuffer()
        {
            // 100 USDT, ask for 90 -> 90 * 1.02 = 91.80 < 100 -> ok.
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = new Position("BTC", 0m),
                ["USDT"] = new Position("USDT", 100m)
            });
            var req = Request(ExchangeOrderSide.Buy, 90m, leverage: 1);

            positions.CheckHasEnoughBalance(req.Object).Should().BeTrue();
        }

        #endregion

        #region CheckHasOpenPositionAndVolume

        [Fact]
        public void CheckHasOpenPositionAndVolume_FalseWhenSymbolUnknown()
        {
            var (positions, _) = Build();

            var result = positions.CheckHasOpenPositionAndVolume(
                "BTC", Request().Object);

            result.Should().BeFalse();
        }

        [Fact]
        public void CheckHasOpenPositionAndVolume_FalseWhenPositionFlat()
        {
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = new Position("BTC", 0m)
            });

            var result = positions.CheckHasOpenPositionAndVolume(
                "BTC", Request().Object);

            result.Should().BeFalse();
        }

        [Fact]
        public void CheckHasOpenPositionAndVolume_TrueWhenPendingLegExists()
        {
            var btc = new Position("BTC", 1m);
            btc.CreatePendingTransaction(0.5m); // pending buy
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = btc
            });

            var result = positions.CheckHasOpenPositionAndVolume(
                "BTC", Request().Object);

            result.Should().BeTrue();
        }

        [Fact]
        public void CheckHasOpenPositionAndVolume_TrueWhenLocked()
        {
            var btc = new Position("BTC", 0m) { IsLocked = true };
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = btc
            });

            var result = positions.CheckHasOpenPositionAndVolume(
                "BTC", Request().Object);

            result.Should().BeTrue();
        }

        #endregion

        #region CreateTrade

        [Fact]
        public void CreateTrade_PassesBaseQuoteFeePositions_ToFactory()
        {
            var btc = new Position("BTC", 0m);
            var usdt = new Position("USDT", 1_000m);
            var (positions, factory) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = btc,
                ["USDT"] = usdt
            });
            var trade = new Mock<ITrade>();
            factory.Setup(f => f.CreateTrade(btc, usdt, usdt))
                   .Returns(trade.Object);
            var req = Request();

            var result = positions.CreateTrade(req.Object);

            result.Should().BeSameAs(trade.Object);
            factory.Verify(f => f.CreateTrade(btc, usdt, usdt), Times.Once);
        }

        [Fact]
        public void CreateTrade_LocksBasePositionDuringFactoryCall()
        {
            // Captured snapshot: when the factory is invoked, IsLocked must
            // be true. After CreateTrade returns it must be false again.
            var btc = new Position("BTC", 0m);
            var usdt = new Position("USDT", 1_000m);
            var (positions, factory) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = btc,
                ["USDT"] = usdt
            });
            bool? lockStateInsideFactory = null;
            factory.Setup(f => f.CreateTrade(
                It.IsAny<IPosition>(), It.IsAny<IPosition>(), It.IsAny<IPosition>()))
                .Callback(() => lockStateInsideFactory = btc.IsLocked)
                .Returns(new Mock<ITrade>().Object);

            positions.CreateTrade(Request().Object);

            lockStateInsideFactory.Should().BeTrue(
                "base must be locked while the factory is constructing the trade");
            btc.IsLocked.Should().BeFalse(
                "lock must be released once the factory returns");
        }

        [Fact]
        public void CreateTrade_UsesUsdtAsFeePosition_EvenForNonUsdtPair()
        {
            var btc = new Position("BTC", 1m);
            var eth = new Position("ETH", 0m);
            var usdt = new Position("USDT", 1_000m);
            var (positions, factory) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = btc,
                ["ETH"] = eth,
                ["USDT"] = usdt
            });
            factory.Setup(f => f.CreateTrade(
                It.IsAny<IPosition>(), It.IsAny<IPosition>(), It.IsAny<IPosition>()))
                .Returns(new Mock<ITrade>().Object);

            // ETHBTC pair: base=ETH, quote=BTC, but fee is still USDT.
            var req = Request(baseSymbol: "ETH", quoteSymbol: "BTC");
            positions.CreateTrade(req.Object);

            factory.Verify(f => f.CreateTrade(eth, btc, usdt), Times.Once);
        }

        #endregion

        #region CheckRequest

        [Fact]
        public void CheckRequest_TrueWhenBalanceOkAndNoOpenPosition()
        {
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = new Position("BTC", 0m),
                ["USDT"] = new Position("USDT", 100_000m)
            });
            var req = Request(ExchangeOrderSide.Buy, 1_000m, leverage: 1);

            positions.CheckRequest(req.Object).Should().BeTrue();
        }

        [Fact]
        public void CheckRequest_FalseWhenBalanceInsufficient()
        {
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = new Position("BTC", 0m),
                ["USDT"] = new Position("USDT", 100m)
            });
            var req = Request(ExchangeOrderSide.Buy, 100_000m, leverage: 1);

            positions.CheckRequest(req.Object).Should().BeFalse();
        }

        [Fact]
        public void CheckRequest_FalseWhenBaseAlreadyHasOpenPosition()
        {
            // Plenty of USDT, but BTC already has an in-flight order.
            // The trade must be rejected even though balance is ample.
            var btc = new Position("BTC", 0m);
            btc.CreatePendingTransaction(0.5m);
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = btc,
                ["USDT"] = new Position("USDT", 100_000m)
            });
            var req = Request(ExchangeOrderSide.Buy, 1_000m, leverage: 1);

            positions.CheckRequest(req.Object).Should().BeFalse(
                "an open position on base must block a new request even with enough balance");
        }

        [Fact]
        public void CheckRequest_FalseWhenBaseLocked()
        {
            // Same as above but via the lock flag rather than a pending leg.
            var btc = new Position("BTC", 0m) { IsLocked = true };
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["BTC"] = btc,
                ["USDT"] = new Position("USDT", 100_000m)
            });
            var req = Request(ExchangeOrderSide.Buy, 1_000m, leverage: 1);

            positions.CheckRequest(req.Object).Should().BeFalse();
        }

        #endregion

        #region AjdustPosition

        [Fact]
        public void AjdustPosition_AddsCompletedLeg_ToExistingAsset()
        {
            var usdt = new Position("USDT", 100m);
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["USDT"] = usdt
            });

            positions.AjdustPosition("USDT", 50m);

            usdt.FreeAmount.Should().Be(150m,
                "adjust must add a completed leg, not replace");
        }

        [Fact]
        public void AjdustPosition_LazilyCreatesAsset_WhenUnknown()
        {
            // Hit on first sync of an asset we've never traded before:
            // GetPosition lazily creates the entry, then we add the leg.
            var (positions, _) = Build();

            positions.AjdustPosition("ETH", 2.5m);

            positions.GetPosition("ETH").FreeAmount.Should().Be(2.5m);
        }

        [Fact]
        public void AjdustPosition_AcceptsNegativeAdjustment()
        {
            // Exchange-driven correction: balance went down (withdrawal,
            // funding fee, etc.) - we record it as a negative completed leg.
            var usdt = new Position("USDT", 100m);
            var (positions, _) = Build(new Dictionary<string, IPosition>
            {
                ["USDT"] = usdt
            });

            positions.AjdustPosition("USDT", -30m);

            usdt.FreeAmount.Should().Be(70m);
        }

        #endregion
    }
}
