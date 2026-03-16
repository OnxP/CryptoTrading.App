using System;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Exchange.BitfinexAdapter;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange.Providers
{
    public class BitfinexMapperTests
    {
        [Theory]
        [InlineData("BTCUSDT", "tBTCUST")]
        [InlineData("ETHUSDT", "tETHUST")]
        [InlineData("BTCUSD", "tBTCUSD")]
        public void TobitfinexSymbol_ConvertsCorrectly(string standard, string expected)
        {
            BitfinexMapper.TobitfinexSymbol(standard).Should().Be(expected);
        }

        [Theory]
        [InlineData("tBTCUST", "BTCUSDT")]
        [InlineData("tETHUST", "ETHUSDT")]
        [InlineData("tBTCUSD", "BTCUSD")]
        public void ToStandardSymbol_ConvertsCorrectly(string bitfinex, string expected)
        {
            BitfinexMapper.ToStandardSymbol(bitfinex).Should().Be(expected);
        }

        [Fact]
        public void SymbolConversion_RoundTrips()
        {
            var original = "BTCUSDT";
            var bfx = BitfinexMapper.TobitfinexSymbol(original);
            var back = BitfinexMapper.ToStandardSymbol(bfx);
            back.Should().Be(original);
        }

        [Theory]
        [InlineData(100.0, ExchangeOrderSide.Buy)]
        [InlineData(-50.0, ExchangeOrderSide.Sell)]
        public void MapOrderSideFromAmount_PositiveBuy_NegativeSell(decimal amount, ExchangeOrderSide expected)
        {
            BitfinexMapper.MapOrderSideFromAmount(amount).Should().Be(expected);
        }

        [Theory]
        [InlineData("MARKET", ExchangeOrderType.Market)]
        [InlineData("EXCHANGE MARKET", ExchangeOrderType.Market)]
        [InlineData("LIMIT", ExchangeOrderType.Limit)]
        [InlineData("EXCHANGE LIMIT", ExchangeOrderType.Limit)]
        [InlineData("STOP LIMIT", ExchangeOrderType.StopLimit)]
        [InlineData("EXCHANGE STOP LIMIT", ExchangeOrderType.StopLimit)]
        public void MapOrderType_AllValues_Covered(string type, ExchangeOrderType expected)
        {
            BitfinexMapper.MapOrderType(type).Should().Be(expected);
        }

        [Theory]
        [InlineData("ACTIVE", ExchangeOrderStatus.New)]
        [InlineData("PARTIALLY FILLED", ExchangeOrderStatus.PartiallyFilled)]
        [InlineData("EXECUTED", ExchangeOrderStatus.Filled)]
        [InlineData("CANCELED", ExchangeOrderStatus.Cancelled)]
        public void MapOrderStatus_AllValues_Covered(string status, ExchangeOrderStatus expected)
        {
            BitfinexMapper.MapOrderStatus(status).Should().Be(expected);
        }

        [Theory]
        [InlineData(CandleInterval.Minute_1, "1m")]
        [InlineData(CandleInterval.Minute_5, "5m")]
        [InlineData(CandleInterval.Minute_15, "15m")]
        [InlineData(CandleInterval.Hour_1, "1h")]
        [InlineData(CandleInterval.Hour_4, "4h")]
        [InlineData(CandleInterval.Day_1, "1D")]
        [InlineData(CandleInterval.Week_1, "1W")]
        [InlineData(CandleInterval.Month_1, "1M")]
        public void MapCandleIntervalToTimeframe_CorrectValues(CandleInterval interval, string expected)
        {
            BitfinexMapper.MapCandleIntervalToTimeframe(interval).Should().Be(expected);
        }

        [Fact]
        public void ParseCandlestick_CorrectValues()
        {
            // Bitfinex format: [MTS, OPEN, CLOSE, HIGH, LOW, VOLUME]
            var data = new decimal[] { 1704067200000m, 42000m, 42300m, 42500m, 41800m, 500m };

            var candle = BitfinexMapper.ParseCandlestick(data, "BTCUSDT", CandleInterval.Hour_4);

            candle.ExchangeId.Should().Be("Bitfinex");
            candle.Symbol.Should().Be("BTCUSDT");
            candle.Interval.Should().Be(CandleInterval.Hour_4);
            candle.Open.Should().Be(42000m);
            candle.Close.Should().Be(42300m); // Index 2 in Bitfinex
            candle.High.Should().Be(42500m);  // Index 3
            candle.Low.Should().Be(41800m);   // Index 4
            candle.Volume.Should().Be(500m);
        }

        [Fact]
        public void TimestampConversion_RoundTrips()
        {
            var original = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var ms = BitfinexMapper.ToUnixMilliseconds(original);
            var back = BitfinexMapper.FromUnixMilliseconds(ms);
            back.Should().Be(original);
        }
    }
}
