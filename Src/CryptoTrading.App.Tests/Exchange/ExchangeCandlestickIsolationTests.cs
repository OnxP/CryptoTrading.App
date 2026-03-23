using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Tests.Mocks;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange
{
    public class ExchangeCandlestickIsolationTests
    {
        private readonly MockExchangeProvider _binance;
        private readonly MockExchangeProvider _bitfinex;

        public ExchangeCandlestickIsolationTests()
        {
            var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            _binance = new MockExchangeProvider("Binance")
            {
                CandlestickData = new Dictionary<string, List<ExchangeCandlestick>>
                {
                    ["BTCUSDT_Hour_4"] = new List<ExchangeCandlestick>
                    {
                        new ExchangeCandlestick("Binance", "BTCUSDT", CandleInterval.Hour_4,
                            baseTime, baseTime.AddHours(4), 42000m, 42500m, 41800m, 42300m, 500m),
                        new ExchangeCandlestick("Binance", "BTCUSDT", CandleInterval.Hour_4,
                            baseTime.AddHours(4), baseTime.AddHours(8), 42300m, 43000m, 42100m, 42900m, 600m)
                    }
                }
            };

            _bitfinex = new MockExchangeProvider("Bitfinex")
            {
                CandlestickData = new Dictionary<string, List<ExchangeCandlestick>>
                {
                    ["BTCUSDT_Hour_4"] = new List<ExchangeCandlestick>
                    {
                        new ExchangeCandlestick("Bitfinex", "BTCUSDT", CandleInterval.Hour_4,
                            baseTime, baseTime.AddHours(4), 42010m, 42510m, 41790m, 42310m, 450m),
                        new ExchangeCandlestick("Bitfinex", "BTCUSDT", CandleInterval.Hour_4,
                            baseTime.AddHours(4), baseTime.AddHours(8), 42310m, 43010m, 42090m, 42910m, 550m)
                    }
                }
            };
        }

        [Fact]
        public async Task Candlesticks_FromDifferentExchanges_AreRoutedCorrectly()
        {
            var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddHours(8);

            var binanceCandles = (await _binance.GetCandlesticksAsync("BTCUSDT", CandleInterval.Hour_4, from, to)).ToList();
            var bitfinexCandles = (await _bitfinex.GetCandlesticksAsync("BTCUSDT", CandleInterval.Hour_4, from, to)).ToList();

            binanceCandles.Should().HaveCount(2);
            bitfinexCandles.Should().HaveCount(2);

            // Binance candles tagged as Binance
            binanceCandles.Should().OnlyContain(c => c.ExchangeId == "Binance");
            // Bitfinex candles tagged as Bitfinex
            bitfinexCandles.Should().OnlyContain(c => c.ExchangeId == "Bitfinex");

            // Prices should be different (different exchanges have slightly different prices)
            binanceCandles[0].Open.Should().NotBe(bitfinexCandles[0].Open);
        }

        [Fact]
        public async Task CandlestickSubscription_PerExchange_IsIndependent()
        {
            var binanceReceived = new List<ExchangeCandlestick>();
            var bitfinexReceived = new List<ExchangeCandlestick>();

            await _binance.SubscribeCandlestickAsync("BTCUSDT", CandleInterval.Hour_4, c => binanceReceived.Add(c));
            await _bitfinex.SubscribeCandlestickAsync("BTCUSDT", CandleInterval.Hour_4, c => bitfinexReceived.Add(c));

            // Simulate a candle on Binance only
            var binanceCandle = new ExchangeCandlestick("Binance", "BTCUSDT", CandleInterval.Hour_4,
                DateTime.UtcNow, DateTime.UtcNow.AddHours(4), 50000m, 51000m, 49500m, 50500m, 100m);
            _binance.SimulateCandlestick(binanceCandle);

            binanceReceived.Should().HaveCount(1);
            binanceReceived[0].ExchangeId.Should().Be("Binance");
            bitfinexReceived.Should().BeEmpty();
        }

        [Fact]
        public async Task SimulateCandlestick_OnBothExchanges_KeepsDataSeparate()
        {
            var binanceReceived = new List<ExchangeCandlestick>();
            var bitfinexReceived = new List<ExchangeCandlestick>();

            await _binance.SubscribeCandlestickAsync("BTCUSDT", CandleInterval.Minute_1, c => binanceReceived.Add(c));
            await _bitfinex.SubscribeCandlestickAsync("BTCUSDT", CandleInterval.Minute_1, c => bitfinexReceived.Add(c));

            var now = DateTime.UtcNow;

            _binance.SimulateCandlestick(new ExchangeCandlestick("Binance", "BTCUSDT", CandleInterval.Minute_1,
                now, now.AddMinutes(1), 50000m, 50100m, 49900m, 50050m, 10m));
            _binance.SimulateCandlestick(new ExchangeCandlestick("Binance", "BTCUSDT", CandleInterval.Minute_1,
                now.AddMinutes(1), now.AddMinutes(2), 50050m, 50200m, 50000m, 50150m, 12m));

            _bitfinex.SimulateCandlestick(new ExchangeCandlestick("Bitfinex", "BTCUSDT", CandleInterval.Minute_1,
                now, now.AddMinutes(1), 50010m, 50110m, 49910m, 50060m, 8m));

            binanceReceived.Should().HaveCount(2);
            bitfinexReceived.Should().HaveCount(1);

            binanceReceived.Should().OnlyContain(c => c.ExchangeId == "Binance");
            bitfinexReceived.Should().OnlyContain(c => c.ExchangeId == "Bitfinex");
        }

        [Fact]
        public async Task HistoricData_FilteredByExchangeId()
        {
            var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddHours(12);

            var binanceCandles = (await _binance.GetCandlesticksAsync("BTCUSDT", CandleInterval.Hour_4, from, to)).ToList();

            binanceCandles.Should().AllSatisfy(c =>
            {
                c.ExchangeId.Should().Be("Binance");
                c.Symbol.Should().Be("BTCUSDT");
            });
        }

        [Fact]
        public async Task UnsubscribeAll_OnOneExchange_DoesNotAffectOther()
        {
            var binanceReceived = new List<ExchangeCandlestick>();
            var bitfinexReceived = new List<ExchangeCandlestick>();

            await _binance.SubscribeCandlestickAsync("BTCUSDT", CandleInterval.Hour_4, c => binanceReceived.Add(c));
            await _bitfinex.SubscribeCandlestickAsync("BTCUSDT", CandleInterval.Hour_4, c => bitfinexReceived.Add(c));

            // Unsubscribe Binance only
            await _binance.UnsubscribeAllAsync();

            // Binance should NOT receive candles anymore
            _binance.SimulateCandlestick(new ExchangeCandlestick("Binance", "BTCUSDT", CandleInterval.Hour_4,
                DateTime.UtcNow, DateTime.UtcNow.AddHours(4), 50000m, 51000m, 49500m, 50500m, 100m));

            // Bitfinex should still receive candles
            _bitfinex.SimulateCandlestick(new ExchangeCandlestick("Bitfinex", "BTCUSDT", CandleInterval.Hour_4,
                DateTime.UtcNow, DateTime.UtcNow.AddHours(4), 50010m, 51010m, 49510m, 50510m, 95m));

            binanceReceived.Should().BeEmpty();
            bitfinexReceived.Should().HaveCount(1);
        }
    }
}
