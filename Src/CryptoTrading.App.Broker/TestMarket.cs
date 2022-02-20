using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.TradeRequest;

namespace CryptoTrading.App.Broker
{
    public class TestMarket :IMarket
    {
        List<IRequest> trades = new List<IRequest>();
        public Task<IEnumerable<AccountBalance>> GetAccountBalances()
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Order>> GetAllOpenOrders()
        {
            throw new NotImplementedException();
        }

        public Task<Order> SetMarketOrder(IMarketRequest trade)
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
            task.Start();
            return task;
        }

        public Task<string> CancelOrder(ICancelRequest request)
        {
            return Task.Run(() => { return ""; });
        }

        public Task<Order> SetLimitOrder(IStopLimitRequest trade)
        {
            trades.Add(trade);
            var order = new Order(new BinanceApiUser("Test"),
                             trade.Symbol,
                             0,
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
            Task<Order> task = new Task<Order>(() => { return order; });
            task.Start();
            return task;
        }
    }
}
