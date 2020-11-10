using System.Collections.Generic;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.TradeRequest;

namespace CryptoTrading.App.Broker
{
    public interface IMarket
    {
        Task<IEnumerable<AccountBalance>> GetAccountBalances();
        Task<IEnumerable<Order>> GetAllOpenOrders();
        Task<Order> SetMarketOrder(IMarketRequest request);
        Task<string> CancelOrder(ICancelRequest request);
        Task<Order> SetLimitOrder(IStopLimitRequest request);
    }
}