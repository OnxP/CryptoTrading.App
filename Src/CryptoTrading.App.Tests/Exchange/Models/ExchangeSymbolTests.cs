using CryptoTrading.App.Core.Exchange;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange.Models
{
    public class ExchangeSymbolTests
    {
        [Fact]
        public void Constructor_SetsBasicProperties()
        {
            var symbol = new ExchangeSymbol("Binance", "BTCUSDT", "BTC", "USDT");

            symbol.ExchangeId.Should().Be("Binance");
            symbol.Ticker.Should().Be("BTCUSDT");
            symbol.BaseAsset.Should().Be("BTC");
            symbol.QuoteAsset.Should().Be("USDT");
            symbol.IsActive.Should().BeTrue();
        }

        [Fact]
        public void IsValidQuantity_AboveMin_ReturnsTrue()
        {
            var symbol = new ExchangeSymbol { MinQuantity = 0.001m, MaxQuantity = 100m };
            symbol.IsValidQuantity(0.5m).Should().BeTrue();
        }

        [Fact]
        public void IsValidQuantity_BelowMin_ReturnsFalse()
        {
            var symbol = new ExchangeSymbol { MinQuantity = 0.001m };
            symbol.IsValidQuantity(0.0001m).Should().BeFalse();
        }

        [Fact]
        public void IsValidQuantity_AboveMax_ReturnsFalse()
        {
            var symbol = new ExchangeSymbol { MinQuantity = 0.001m, MaxQuantity = 100m };
            symbol.IsValidQuantity(200m).Should().BeFalse();
        }

        [Fact]
        public void IsValidQuantity_MaxZero_IgnoresMaxLimit()
        {
            var symbol = new ExchangeSymbol { MinQuantity = 0.001m, MaxQuantity = 0 };
            symbol.IsValidQuantity(999999m).Should().BeTrue();
        }

        [Fact]
        public void MeetsMinNotional_AboveThreshold_ReturnsTrue()
        {
            var symbol = new ExchangeSymbol { MinNotional = 10m };
            symbol.MeetsMinNotional(50000m, 0.001m).Should().BeTrue(); // 50000 * 0.001 = 50
        }

        [Fact]
        public void MeetsMinNotional_BelowThreshold_ReturnsFalse()
        {
            var symbol = new ExchangeSymbol { MinNotional = 10m };
            symbol.MeetsMinNotional(50000m, 0.0001m).Should().BeFalse(); // 50000 * 0.0001 = 5
        }

        [Fact]
        public void SameTickerDifferentExchanges_AreDistinct()
        {
            var binance = new ExchangeSymbol("Binance", "BTCUSDT", "BTC", "USDT");
            var bitfinex = new ExchangeSymbol("Bitfinex", "BTCUSDT", "BTC", "USDT");

            binance.ExchangeId.Should().NotBe(bitfinex.ExchangeId);
            binance.Ticker.Should().Be(bitfinex.Ticker);
        }
    }
}
