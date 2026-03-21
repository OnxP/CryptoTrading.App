using System.Collections.Generic;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.TradeRequest;

namespace CryptoTrading.App.Broker
{
    public interface IMarket
    {
        Task<IEnumerable<ExchangeBalance>> GetAccountBalances();
        Task<IEnumerable<ExchangeOrder>> GetAllOpenOrders();
        Task<ExchangeOrder> SetMarketOrder(IMarketRequest request);
        Task<string> CancelOrder(ICancelRequest request);
        Task<ExchangeOrder> SetLimitOrder(IStopLimitRequest request);
    }
}
