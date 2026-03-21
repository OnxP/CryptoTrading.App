using CryptoTrading.App.Core.Exchange;
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

        ITrade Trade { get; }
        List<ITrade> HistoricTrades { get; }
        void UpdateInitialTransaction(ExchangeOrder order);
        void CancelLimitOrder(string order);
        void UpdateStopLimitOrder(ExchangeOrder order);
        void AddTrade(ITrade trade);
        void AddRequest(ITradeRequest trade, Position.IPositions positions);
        void CompleteTrade();
        Task SetNewRequest(ITradeRequest what);
        Task SubscribetToMarketData();
    }
}
