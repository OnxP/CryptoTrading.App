using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core
{
    public interface IBroker
    {
        Task<IBrokerResult> SubmitTradeRequest(ITradeRequest request);
        Task<ExchangeOrder> SubmitMarketOrder(string symbol, ExchangeOrderSide side, decimal quantity);
        Task<ExchangeOrder> SubmitLimitOrder(string symbol, ExchangeOrderSide side, decimal quantity, decimal price);
        Task<ExchangeOrder> CancelOrder(string symbol, string orderId);
    }

    public interface IBrokerResult
    {
        bool Accepted { get; }
        string RejectionReason { get; }
    }
}
