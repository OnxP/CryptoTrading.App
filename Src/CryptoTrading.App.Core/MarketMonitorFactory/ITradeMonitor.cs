using Binance;
using CryptoTrading.App.Core.Trade;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public interface ITradeMonitor
    {
        bool Live { get; }
        string Symbol { get;}
        string KeyValue { get; set; }

        ITrade Trade { get; }
        void UpdateInitialTransaction(Order order);
        Task CancelLimitOrder(string order);
        void UpdateStopLimitOrder(Order order);
        void AddRequest(ITradeRequest trade, Position.IPositions positions);
        void CompleteTrade();
        Task SubscribetToMarketData();
    }
}