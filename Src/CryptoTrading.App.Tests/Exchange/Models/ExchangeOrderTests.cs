using System;
using CryptoTrading.App.Core.Exchange;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange.Models
{
    public class ExchangeOrderTests
    {
        [Fact]
        public void CreateFilledOrder_SetsAllProperties()
        {
            var order = ExchangeOrder.CreateFilledOrder(
                "Binance", "BTCUSDT", ExchangeOrderSide.Buy, 50000m, 0.5m, DateTime.UtcNow);

            order.ExchangeId.Should().Be("Binance");
            order.Symbol.Should().Be("BTCUSDT");
            order.Side.Should().Be(ExchangeOrderSide.Buy);
            order.Status.Should().Be(ExchangeOrderStatus.Filled);
            order.Price.Should().Be(50000m);
            order.Quantity.Should().Be(0.5m);
            order.FilledQuantity.Should().Be(0.5m);
            order.QuoteQuantity.Should().Be(25000m);
            order.OrderId.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void IsFilled_WhenStatusFilled_ReturnsTrue()
        {
            var order = new ExchangeOrder { Status = ExchangeOrderStatus.Filled };
            order.IsFilled.Should().BeTrue();
        }

        [Fact]
        public void IsFilled_WhenStatusNew_ReturnsFalse()
        {
            var order = new ExchangeOrder { Status = ExchangeOrderStatus.New };
            order.IsFilled.Should().BeFalse();
        }

        [Theory]
        [InlineData(ExchangeOrderStatus.New, true)]
        [InlineData(ExchangeOrderStatus.PartiallyFilled, true)]
        [InlineData(ExchangeOrderStatus.Filled, false)]
        [InlineData(ExchangeOrderStatus.Cancelled, false)]
        [InlineData(ExchangeOrderStatus.Rejected, false)]
        public void IsActive_ReturnsCorrectly(ExchangeOrderStatus status, bool expected)
        {
            var order = new ExchangeOrder { Status = status };
            order.IsActive.Should().Be(expected);
        }

        [Fact]
        public void CreateFilledOrder_GeneratesUniqueOrderIds()
        {
            var order1 = ExchangeOrder.CreateFilledOrder("Binance", "BTCUSDT", ExchangeOrderSide.Buy, 50000m, 1m, DateTime.UtcNow);
            var order2 = ExchangeOrder.CreateFilledOrder("Binance", "BTCUSDT", ExchangeOrderSide.Buy, 50000m, 1m, DateTime.UtcNow);

            order1.OrderId.Should().NotBe(order2.OrderId);
        }

        [Fact]
        public void Order_PreservesExchangeId()
        {
            var binanceOrder = ExchangeOrder.CreateFilledOrder("Binance", "BTCUSDT", ExchangeOrderSide.Buy, 50000m, 1m, DateTime.UtcNow);
            var bitfinexOrder = ExchangeOrder.CreateFilledOrder("Bitfinex", "BTCUSDT", ExchangeOrderSide.Buy, 50000m, 1m, DateTime.UtcNow);

            binanceOrder.ExchangeId.Should().Be("Binance");
            bitfinexOrder.ExchangeId.Should().Be("Bitfinex");
        }
    }
}
