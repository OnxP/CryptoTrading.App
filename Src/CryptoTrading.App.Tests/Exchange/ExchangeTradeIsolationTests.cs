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
    public class ExchangeTradeIsolationTests
    {
        private readonly MockExchangeProvider _binance;
        private readonly MockExchangeProvider _bitfinex;

        public ExchangeTradeIsolationTests()
        {
            _binance = new MockExchangeProvider("Binance")
            {
                FeeSchedule = new ExchangeFeeSchedule("Binance", 0.001m, 0.001m, "BNB")
            };

            _bitfinex = new MockExchangeProvider("Bitfinex")
            {
                FeeSchedule = new ExchangeFeeSchedule("Bitfinex", 0.001m, 0.002m, "USD")
            };
        }

        [Fact]
        public async Task Trade_BelongsToCorrectExchange()
        {
            var binanceOrder = await _binance.PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 0.5m);
            var bitfinexOrder = await _bitfinex.PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Sell, 1.0m);

            binanceOrder.ExchangeId.Should().Be("Binance");
            bitfinexOrder.ExchangeId.Should().Be("Bitfinex");
        }

        [Fact]
        public async Task CompletedTrades_FilterByExchange()
        {
            await _binance.PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 0.5m);
            await _binance.PlaceMarketOrderAsync("ETHUSDT", ExchangeOrderSide.Buy, 10m);
            await _bitfinex.PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Sell, 1.0m);

            var binanceTrades = _binance.PlacedOrders.Where(o => o.ExchangeId == "Binance").ToList();
            var bitfinexTrades = _bitfinex.PlacedOrders.Where(o => o.ExchangeId == "Bitfinex").ToList();

            binanceTrades.Should().HaveCount(2);
            bitfinexTrades.Should().HaveCount(1);

            binanceTrades.Should().OnlyContain(o => o.ExchangeId == "Binance");
            bitfinexTrades.Should().OnlyContain(o => o.ExchangeId == "Bitfinex");
        }

        [Fact]
        public async Task TradeProfit_UsesCorrectFeeSchedule()
        {
            var binanceFees = await _binance.GetFeeScheduleAsync();
            var bitfinexFees = await _bitfinex.GetFeeScheduleAsync();

            var orderValue = 50000m;

            var binanceTakerFee = binanceFees.CalculateTakerFee(orderValue);
            var bitfinexTakerFee = bitfinexFees.CalculateTakerFee(orderValue);

            // Binance: 50000 * 0.001 = 50
            binanceTakerFee.Should().Be(50m);
            // Bitfinex: 50000 * 0.002 = 100
            bitfinexTakerFee.Should().Be(100m);

            // Different fee assets
            binanceFees.FeeAsset.Should().Be("BNB");
            bitfinexFees.FeeAsset.Should().Be("USD");
        }

        [Fact]
        public async Task OrderRouting_GoesToCorrectExchange()
        {
            // Place orders on both exchanges
            await _binance.PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 1.0m);
            await _bitfinex.PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Sell, 2.0m);

            // Verify routing: each exchange only recorded its own orders
            _binance.PlacedOrders.Should().HaveCount(1);
            _binance.PlacedOrders[0].Quantity.Should().Be(1.0m);
            _binance.PlacedOrders[0].Side.Should().Be(ExchangeOrderSide.Buy);

            _bitfinex.PlacedOrders.Should().HaveCount(1);
            _bitfinex.PlacedOrders[0].Quantity.Should().Be(2.0m);
            _bitfinex.PlacedOrders[0].Side.Should().Be(ExchangeOrderSide.Sell);
        }

        [Fact]
        public async Task StopLimitOrder_RoutedToCorrectExchange()
        {
            var binanceOrder = await _binance.PlaceStopLimitOrderAsync(
                "BTCUSDT", ExchangeOrderSide.Sell, 48000m, 47900m, 0.5m);
            var bitfinexOrder = await _bitfinex.PlaceStopLimitOrderAsync(
                "BTCUSDT", ExchangeOrderSide.Sell, 48000m, 47900m, 1.0m);

            binanceOrder.ExchangeId.Should().Be("Binance");
            binanceOrder.Type.Should().Be(ExchangeOrderType.StopLimit);
            binanceOrder.StopPrice.Should().Be(48000m);

            bitfinexOrder.ExchangeId.Should().Be("Bitfinex");
            bitfinexOrder.Quantity.Should().Be(1.0m);
        }

        [Fact]
        public async Task SimulateFill_OnOneExchange_DoesNotAffectOther()
        {
            // Place limit orders on both
            var binanceOrder = await _binance.PlaceLimitOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 45000m, 1.0m);
            var bitfinexOrder = await _bitfinex.PlaceLimitOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 45000m, 0.5m);

            // Fill only Binance
            _binance.SimulateFill(binanceOrder.OrderId, 44900m);

            // Binance order filled
            var filledBinance = _binance.PlacedOrders.First(o => o.OrderId == binanceOrder.OrderId);
            filledBinance.Status.Should().Be(ExchangeOrderStatus.Filled);
            filledBinance.Price.Should().Be(44900m);

            // Bitfinex order still open
            var openBitfinex = _bitfinex.PlacedOrders.First(o => o.OrderId == bitfinexOrder.OrderId);
            openBitfinex.Status.Should().Be(ExchangeOrderStatus.New);
        }

        [Fact]
        public async Task MultipleSymbols_SameExchange_TrackSeparately()
        {
            await _binance.PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 0.5m);
            await _binance.PlaceMarketOrderAsync("ETHUSDT", ExchangeOrderSide.Buy, 5.0m);
            await _binance.PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Sell, 0.3m);

            var btcOrders = _binance.PlacedOrders.Where(o => o.Symbol == "BTCUSDT").ToList();
            var ethOrders = _binance.PlacedOrders.Where(o => o.Symbol == "ETHUSDT").ToList();

            btcOrders.Should().HaveCount(2);
            ethOrders.Should().HaveCount(1);
            _binance.PlacedOrders.Should().OnlyContain(o => o.ExchangeId == "Binance");
        }

        [Fact]
        public async Task ExchangeProviderFactory_CreatesCorrectProviders()
        {
            // Simulate what ExchangeProviderFactory would do
            var providers = new Dictionary<string, IExchangeProvider>
            {
                ["Binance"] = _binance,
                ["Bitfinex"] = _bitfinex
            };

            providers.Should().HaveCount(2);
            providers["Binance"].ExchangeId.Should().Be("Binance");
            providers["Bitfinex"].ExchangeId.Should().Be("Bitfinex");

            // Each provider maintains its own state
            await providers["Binance"].PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 1m);
            (providers["Bitfinex"] as MockExchangeProvider).PlacedOrders.Should().BeEmpty();
        }
    }
}
