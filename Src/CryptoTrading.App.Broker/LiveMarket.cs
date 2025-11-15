using Binance;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Broker
{
    public class LiveMarket : IMarket
    {
        private readonly IBinanceApi _api;
        private readonly IBinanceApiUser _user;
        private readonly ILogger<LiveMarket> _logger;
        public LiveMarket(ILogger<LiveMarket> logger,IBinanceApi api,IBinanceApiUser user)
        {
            _api = api;
            _user = user;
            _logger = logger;
        }
        public async Task<IEnumerable<AccountBalance>> GetAccountBalances()
        {
            var accountBalances = await _api.GetAccountInfoAsync(_user);
            return accountBalances.Balances.Where(x=>x.Free > 0);
        }

        public Task<IEnumerable<Order>> GetAllOpenOrders()
        {
            throw new NotImplementedException();
        }

        public async Task<Order> SetMarketOrder(IMarketRequest trade)
        {
            var clientOrder = new MarketOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = trade.OrderType,
                Quantity = trade.Quantity
            };

            var order = await _api.PlaceAsync(clientOrder);

            return order;
        }

        public async Task<Order> SetStopLimitOrder(IStopLimitRequest trade)
        {
            var clientOrder = new LimitOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = trade.OrderType,
                Price = trade.StopPrice,
                Quantity = trade.Quantity
            };

            var order = await _api.PlaceAsync(clientOrder);
            LogOrder(order.Symbol, order.ClientOrderId, OrderStatus.Filled);

            return order;
        }

        private void LogOrder(string symbol, string order, OrderStatus filled)
        {
            _logger.LogInformation($"{symbol} - {order} {filled.ToString()}");
        }

        public async Task<string> CancelOrder(ICancelRequest order)
        {
            LogOrder(order.Symbol, order.ClientOrderId.ToString(), OrderStatus.Canceled);
            return await _api.CancelOrderAsync(_user, order.Symbol, order.ClientOrderId);
        }

        public async Task<Order> SetLimitOrder(ILimitRequest trade)
        {
            var clientOrder = new LimitOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = trade.OrderType,
                Quantity = trade.Quantity
            };

            var order = await _api.PlaceAsync(clientOrder);

            return order;
        }
    }
}
