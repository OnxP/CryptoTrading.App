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
        void AddRequest(ITradeRequest trade, Position.IPositions positions);
        void CompleteTrade();
        void SetNewRequest(ITradeRequest what);
        Task SubscribetToMarketData();
    }
}