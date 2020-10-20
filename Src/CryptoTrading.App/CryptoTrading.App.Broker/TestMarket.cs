using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Broker
{
    public class TestMarket :IMarket
    {
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
            throw new NotImplementedException();
        }

        public Task<string> CancelOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public Task<Order> SetLimitOrder(ITrade trade, decimal currentStopLoss)
        {
            throw new NotImplementedException();
        }
    }
}
