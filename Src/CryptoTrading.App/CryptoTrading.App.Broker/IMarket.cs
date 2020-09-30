using System.Collections.Generic;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core;

namespace CryptoTrading.App.Broker
{
    public interface IMarket
    {
        object GetAccountBalances();
        void GetPendingTransactions();
        Task<IEnumerable<Order>> GetAllOpenOrders(IBinanceApiUser user);
        Task<Order> SetMarketOrder(ITrade trade, IBinanceApiUser user);
        Task<string> CancelOrder(Order order, IBinanceApiUser user);
        Task<Order> SetLimitOrder(ITrade trade, IBinanceApiUser user, decimal currentStopLoss);
    }
}