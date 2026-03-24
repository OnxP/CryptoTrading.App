using System;
using CryptoTrading.App.Core.Exchange;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange.Models
{
    public class ExchangeCandlestickTests
    {
        [Fact]
        public void IsBullish_WhenCloseAboveOpen_ReturnsTrue()
        {
            var candle = new ExchangeCandlestick { Open = 100m, Close = 110m };
            candle.IsBullish.Should().BeTrue();
        }

        [Fact]
        public void IsBullish_WhenCloseBelowOpen_ReturnsFalse()
        {
            var candle = new ExchangeCandlestick { Open = 110m, Close = 100m };
            candle.IsBullish.Should().BeFalse();
        }

        [Fact]
        public void IsBullish_WhenCloseEqualsOpen_ReturnsTrue()
        {
            var candle = new ExchangeCandlestick { Open = 100m, Close = 100m };
            candle.IsBullish.Should().BeTrue();
        }

        [Fact]
        public void BodySize_CalculatesCorrectly()
        {
            var candle = new ExchangeCandlestick { Open = 100m, Close = 110m };
            candle.BodySize.Should().Be(10m);
        }

        [Fact]
        public void BodySize_BearishCandle_ReturnsPositive()
        {
            var candle = new ExchangeCandlestick { Open = 110m, Close = 100m };
            candle.BodySize.Should().Be(10m);
        }

        [Fact]
        public void Range_CalculatesHighMinusLow()
        {
            var candle = new ExchangeCandlestick { High = 115m, Low = 95m };
            candle.Range.Should().Be(20m);
        }

        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var openTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var closeTime = new DateTime(2024, 1, 1, 4, 0, 0, DateTimeKind.Utc);

            var candle = new ExchangeCandlestick(
                "Binance", "BTCUSDT", CandleInterval.Hour_4,
                openTime, closeTime, 50000m, 51000m, 49500m, 50500m, 1000m);

            candle.ExchangeId.Should().Be("Binance");
            candle.Symbol.Should().Be("BTCUSDT");
            candle.Interval.Should().Be(CandleInterval.Hour_4);
            candle.OpenTime.Should().Be(openTime);
            candle.CloseTime.Should().Be(closeTime);
            candle.Open.Should().Be(50000m);
            candle.High.Should().Be(51000m);
            candle.Low.Should().Be(49500m);
            candle.Close.Should().Be(50500m);
            candle.Volume.Should().Be(1000m);
        }
    }
}
