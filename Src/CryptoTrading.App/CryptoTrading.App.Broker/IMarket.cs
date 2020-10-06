using System.Collections.Generic;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core;

namespace CryptoTrading.App.Broker
{
    public interface IMarket
    {
        Task<IEnumerable<AccountBalance>> GetAccountBalances();
        Task<IEnumerable<Order>> GetAllOpenOrders();
        Task<Order> SetMarketOrder(ITrade trade);
        Task<string> CancelOrder(Order order);
        Task<Order> SetLimitOrder(ITrade trade, decimal currentStopLoss);
    }
}