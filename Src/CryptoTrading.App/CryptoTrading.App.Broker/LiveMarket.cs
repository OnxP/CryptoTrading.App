using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core;

namespace CryptoTrading.App.Broker
{
    public class LiveMarket : IMarket
    {
        private IBinanceApi _api;
        public LiveMarket(IBinanceApi api)
        {
            _api = api;
        }
        public object GetAccountBalances()
        {
            throw new NotImplementedException();
        }

        public void GetPendingTransactions()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Order>> GetAllOpenOrders(IBinanceApiUser user)
        {
            throw new NotImplementedException();
        }

        public async Task<Order> SetMarketOrder(ITrade trade, IBinanceApiUser user)
        {
            var clientOrder = new MarketOrder(user)
            {
                Symbol = trade.Symbol,
                Side = trade.OrderType,
                Quantity = trade.Quantity
            };

            var order = await _api.PlaceAsync(clientOrder);

            return order;
        }

        public async Task<Order> SetLimitOrder(ITrade trade, IBinanceApiUser user, decimal currentStopLoss)
        {
            var clientOrder = new LimitOrder(user)
            {
                Symbol = trade.Symbol,
                Side = trade.OrderType,
                Price = currentStopLoss,
                Quantity = trade.Quantity
            };

            var order = await _api.PlaceAsync(clientOrder);
            //LogOrder(order, OrderStatus.Filled);

            return order;
        }

        public async Task<Order> SetNewLimitOrder(ITrade trade,IBinanceApiUser user, decimal currentStopLoss)
        {
            var newOrder = await SetLimitOrder(trade, user, currentStopLoss);

            return newOrder;
        }

        public async Task<string> CancelOrder(Order order,IBinanceApiUser user)
        {
            return await _api.CancelOrderAsync(user, order.Symbol, order.ClientOrderId);
            //LogOrder(order,user, OrderStatus.Canceled);
        }
    }
}
