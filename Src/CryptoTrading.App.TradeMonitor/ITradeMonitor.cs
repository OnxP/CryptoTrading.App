using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor.Position;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Monitor
{
    public interface ITradeMonitor
    {
        bool Live { get; }
        string Symbol { get; }
        string KeyValue { get; set; }
        List<HistoricTradeRecord> CompletedTrades { get; }
        void AcceptSignal(ITradeSignal signal);
        void CompleteTrade();
        Task SetNewSignal(ITradeSignal signal);
        Task SubscribetToMarketData();
    }
}
