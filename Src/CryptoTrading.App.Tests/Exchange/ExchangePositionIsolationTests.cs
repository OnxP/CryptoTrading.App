using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Tests.Mocks;
using FluentAssertions;
using Xunit;

namespace CryptoTrading.App.Tests.Exchange
{
    public class ExchangePositionIsolationTests
    {
        private readonly MockExchangeProvider _binance;
        private readonly MockExchangeProvider _bitfinex;

        public ExchangePositionIsolationTests()
        {
            _binance = new MockExchangeProvider("Binance")
            {
                Balances = new List<ExchangeBalance>
                {
                    new ExchangeBalance("Binance", "BTC", 1.5m, 0.5m),
                    new ExchangeBalance("Binance", "USDT", 50000m, 0m)
                }
            };

            _bitfinex = new MockExchangeProvider("Bitfinex")
            {
                Balances = new List<ExchangeBalance>
                {
                    new ExchangeBalance("Bitfinex", "BTC", 3.0m, 0m),
                    new ExchangeBalance("Bitfinex", "USDT", 100000m, 5000m)
                }
            };
        }

        [Fact]
        public async Task Positions_FromDifferentExchanges_AreTrackedSeparately()
        {
            var binanceBalances = (await _binance.GetBalancesAsync()).ToList();
            var bitfinexBalances = (await _bitfinex.GetBalancesAsync()).ToList();

            var binanceBtc = binanceBalances.First(b => b.Asset == "BTC");
            var bitfinexBtc = bitfinexBalances.First(b => b.Asset == "BTC");

            binanceBtc.Free.Should().Be(1.5m);
            bitfinexBtc.Free.Should().Be(3.0m);
            binanceBtc.ExchangeId.Should().NotBe(bitfinexBtc.ExchangeId);
        }

        [Fact]
        public async Task GetPosition_WithExchangeId_ReturnsOnlyThatExchangeBalance()
        {
            var binanceBalances = (await _binance.GetBalancesAsync()).ToList();
            var bitfinexBalances = (await _bitfinex.GetBalancesAsync()).ToList();

            binanceBalances.Should().OnlyContain(b => b.ExchangeId == "Binance");
            bitfinexBalances.Should().OnlyContain(b => b.ExchangeId == "Bitfinex");
        }

        [Fact]
        public async Task CreateTransaction_OnBinance_DoesNotAffectBitfinexPosition()
        {
            // Place order on Binance
            await _binance.PlaceMarketOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 0.1m);

            // Verify Binance recorded the order
            _binance.PlacedOrders.Should().HaveCount(1);
            _binance.PlacedOrders[0].ExchangeId.Should().Be("Binance");

            // Verify Bitfinex is untouched
            _bitfinex.PlacedOrders.Should().BeEmpty();

            // Bitfinex balances remain unchanged
            var bitfinexBalances = (await _bitfinex.GetBalancesAsync()).ToList();
            bitfinexBalances.First(b => b.Asset == "BTC").Free.Should().Be(3.0m);
        }

        [Fact]
        public async Task TotalBalance_AcrossExchanges_SumsCorrectly()
        {
            var binanceBalances = (await _binance.GetBalancesAsync()).ToList();
            var bitfinexBalances = (await _bitfinex.GetBalancesAsync()).ToList();

            var allBalances = binanceBalances.Concat(bitfinexBalances).ToList();

            var totalBtc = allBalances.Where(b => b.Asset == "BTC").Sum(b => b.Total);
            var totalUsdt = allBalances.Where(b => b.Asset == "USDT").Sum(b => b.Total);

            totalBtc.Should().Be(5.0m);   // Binance 2.0 (1.5+0.5) + Bitfinex 3.0
            totalUsdt.Should().Be(155000m); // Binance 50000 + Bitfinex 105000 (100000+5000)
        }

        [Fact]
        public async Task OpenOrders_AreIsolatedPerExchange()
        {
            await _binance.PlaceLimitOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 45000m, 1.0m);
            await _bitfinex.PlaceLimitOrderAsync("BTCUSDT", ExchangeOrderSide.Sell, 55000m, 0.5m);

            var binanceOpen = (await _binance.GetOpenOrdersAsync()).ToList();
            var bitfinexOpen = (await _bitfinex.GetOpenOrdersAsync()).ToList();

            binanceOpen.Should().HaveCount(1);
            binanceOpen[0].ExchangeId.Should().Be("Binance");
            binanceOpen[0].Side.Should().Be(ExchangeOrderSide.Buy);

            bitfinexOpen.Should().HaveCount(1);
            bitfinexOpen[0].ExchangeId.Should().Be("Bitfinex");
            bitfinexOpen[0].Side.Should().Be(ExchangeOrderSide.Sell);
        }

        [Fact]
        public async Task CancelOrder_OnOneExchange_DoesNotAffectOther()
        {
            var binanceOrder = await _binance.PlaceLimitOrderAsync("BTCUSDT", ExchangeOrderSide.Buy, 45000m, 1.0m);
            await _bitfinex.PlaceLimitOrderAsync("BTCUSDT", ExchangeOrderSide.Sell, 55000m, 0.5m);

            await _binance.CancelOrderAsync("BTCUSDT", binanceOrder.OrderId);

            var binanceOpen = (await _binance.GetOpenOrdersAsync()).ToList();
            var bitfinexOpen = (await _bitfinex.GetOpenOrdersAsync()).ToList();

            binanceOpen.Should().BeEmpty();
            bitfinexOpen.Should().HaveCount(1);
        }
    }
}
