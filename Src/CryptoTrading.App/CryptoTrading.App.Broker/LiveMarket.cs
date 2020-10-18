using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Broker
{
    public class LiveMarket : IMarket
    {
        private readonly IBinanceApi _api;
        private readonly IBinanceApiUser _user;
        public LiveMarket(IBinanceApi api,IBinanceApiUser user)
        {
            _api = api;
            _user = user;
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

        public async Task<Order> SetMarketOrder(ITrade trade)
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

        public async Task<Order> SetLimitOrder(ITrade trade, decimal currentStopLoss)
        {
            var clientOrder = new LimitOrder(_user)
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

        public async Task<string> CancelOrder(Order order)
        {
            return await _api.CancelOrderAsync(_user, order.Symbol, order.ClientOrderId);
            //LogOrder(order,_user, OrderStatus.Canceled);
        }
    }
}
