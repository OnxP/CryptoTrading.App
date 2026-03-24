using CryptoTrading.App.Core.Exchange;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange.Models
{
    public class ExchangeBalanceTests
    {
        [Fact]
        public void Total_ReturnsSum_OfFreeAndLocked()
        {
            var balance = new ExchangeBalance("Binance", "BTC", 1.5m, 0.5m);
            balance.Total.Should().Be(2.0m);
        }

        [Fact]
        public void Total_WithNoLocked_ReturnsFree()
        {
            var balance = new ExchangeBalance("Binance", "BTC", 1.5m);
            balance.Total.Should().Be(1.5m);
        }

        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var balance = new ExchangeBalance("Bitfinex", "ETH", 10m, 2m);

            balance.ExchangeId.Should().Be("Bitfinex");
            balance.Asset.Should().Be("ETH");
            balance.Free.Should().Be(10m);
            balance.Locked.Should().Be(2m);
        }

        [Fact]
        public void Balances_SameAsset_DifferentExchanges_AreIndependent()
        {
            var binanceBtc = new ExchangeBalance("Binance", "BTC", 1.0m, 0.5m);
            var bitfinexBtc = new ExchangeBalance("Bitfinex", "BTC", 2.0m, 0.0m);

            binanceBtc.ExchangeId.Should().NotBe(bitfinexBtc.ExchangeId);
            binanceBtc.Free.Should().NotBe(bitfinexBtc.Free);
            binanceBtc.Total.Should().Be(1.5m);
            bitfinexBtc.Total.Should().Be(2.0m);
        }

        [Fact]
        public void DefaultConstructor_SetsZeroBalances()
        {
            var balance = new ExchangeBalance();
            balance.Free.Should().Be(0m);
            balance.Locked.Should().Be(0m);
            balance.Total.Should().Be(0m);
        }
    }
}
