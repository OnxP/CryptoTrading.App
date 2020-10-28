using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Broker
{
    public class TestMarket :IMarket
    {
        List<ITrade> trades = new List<ITrade>();
        public Task<IEnumerable<AccountBalance>> GetAccountBalances()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Order>> GetAllOpenOrders()
        {
            throw new NotImplementedException();
        }

        public Task<Order> SetMarketOrder(ITrade trade)
        {
            trades.Add(trade);
            var order = new Order(new BinanceApiUser("Test"),
                             trade.Symbol,
                             0,
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
            Task<Order> task = new Task<Order>(()=> { return order; });
            return task;
        }

        public Task<string> CancelOrder(Order order)
        {
            return Task.Run(() => { return ""; });
        }

        public Task<Order> SetLimitOrder(ITrade trade, decimal currentStopLoss)
        {
            throw new NotImplementedException();
        }
    }
}
