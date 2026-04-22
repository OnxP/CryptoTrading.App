using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;
using CryptoTrading.App.Exchange.BinanceAdapter;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker
{
    /// <summary>
    /// Paper-trading IMarket that hits Binance TEST endpoints (no real fills).
    /// Same adapter pattern as LiveMarket — returns neutral types only.
    /// </summary>
    public class TestLiveMarket : IMarket
    {
        private readonly IBinanceApi _api;
        private readonly IBinanceApiUser _user;
        private readonly ILogger<TestLiveMarket> _logger;
        public TestLiveMarket(ILogger<TestLiveMarket> logger,IBinanceApi api,IBinanceApiUser user)
        {
            _api = api;
            _user = user;
            _logger = logger;
        }
        public async Task<IEnumerable<ExchangeBalance>> GetAccountBalances()
        {
            var accountBalances = await _api.GetAccountInfoAsync(_user);
            return accountBalances.Balances
                .Where(x => x.Free > 0)
                .Select(BinanceMapper.ToExchangeBalance);
        }

        public Task<IEnumerable<ExchangeOrder>> GetAllOpenOrders()
        {
            throw new NotImplementedException();
        }

        public async Task<ExchangeOrder> SetMarketOrder(IMarketRequest trade)
        {
            var clientOrder = new MarketOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = BinanceMapper.MapToBinanceOrderSide(trade.OrderType ?? ExchangeOrderSide.Buy),
                Quantity = trade.Quantity
            };

            var order = await _api.TestPlaceAsync(clientOrder);
            return BinanceMapper.ToExchangeOrder(order);
        }

        public async Task<ExchangeOrder> SetStopLimitOrder(IStopLimitRequest trade)
        {
            var clientOrder = new LimitOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = BinanceMapper.MapToBinanceOrderSide(trade.OrderType ?? ExchangeOrderSide.Buy),
                Price = trade.StopPrice,
                Quantity = trade.Quantity
            };

            var order = await _api.TestPlaceAsync(clientOrder);
            return BinanceMapper.ToExchangeOrder(order);
        }

        private void LogOrder(string symbol, string order, OrderStatus filled)
        {
            _logger.LogInformation($"{symbol} - {order} {filled.ToString()}");
        }

        public async Task<string> CancelOrder(ICancelRequest order)
        {
            _logger.LogInformation($"{order.Symbol} - {order.ClientOrderId} Cancelled");
            return await Task.Run(() => "");
        }

        public async Task<ExchangeOrder> SetLimitOrder(ILimitRequest trade)
        {
            var clientOrder = new LimitOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = BinanceMapper.MapToBinanceOrderSide(trade.OrderType ?? ExchangeOrderSide.Buy),
                Quantity = trade.Quantity
            };

            var order = await _api.TestPlaceAsync(clientOrder);
            return BinanceMapper.ToExchangeOrder(order);
        }
    }
}
