using Binance;
using CryptoTrading.App.Core.Trade;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.MarketMonitorFactory
{
    public interface ITradeMonitor
    {
        bool Live { get; }
        string Symbol { get;}
        string KeyValue { get; set; }
        List<ITrade> HistoricTrades { get; }
        void AddRequest(ITradeRequest trade, Position.IPositions positions);
        void CompleteTrade();
        Task SetNewRequest(ITradeRequest what);
        Task SubscribetToMarketData();
    }
}