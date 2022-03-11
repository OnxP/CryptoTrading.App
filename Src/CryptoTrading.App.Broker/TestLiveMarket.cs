using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.TradeRequest;
using log4net.Config;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker
{
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

            await _api.TestPlaceAsync(clientOrder);
            var order = new Order(_user,
                trade.Symbol,
                1,
                "",
                trade.Price,
                trade.Quantity,
                trade.Quantity,
                0,
                OrderStatus.Filled,
                TimeInForce.IOC,
                OrderType.Market,
                OrderSide.Buy,
                0,
                0,
                DateTime.Now,
                DateTime.Now,
                true);
            return order;
        }

        public async Task<Order> SetLimitOrder(IStopLimitRequest trade)
        {
            var clientOrder = new LimitOrder(_user)
            {
                Symbol = trade.Symbol,
                Side = trade.OrderType,
                Price = trade.StopPrice,
                Quantity = trade.Quantity
            };

            await _api.TestPlaceAsync(clientOrder);
            var order = new Order(_user,
                trade.Symbol,
                1,
                "",
                trade.StopPrice,
                trade.Quantity,
                trade.Quantity,
                0,
                OrderStatus.New,
                TimeInForce.IOC,
                OrderType.StopLossLimit,
                OrderSide.Sell,
                trade.StopPrice,
                0,
                DateTime.Now,
                DateTime.Now,
                true);
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
    }
}
