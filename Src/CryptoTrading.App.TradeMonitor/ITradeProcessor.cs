using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core;
using CryptoTrading.App.Monitor.Position;
using System.Collections.Generic;

namespace CryptoTrading.App.Monitor
{
    public interface ITradeProcessor
    {
        IPositions Positions { get; set; }
        void CompleteAllTransactions();
        void ClearInactiveTrades();
        List<HistoricTradeRecord> GetCompletedTrades();
        void Configure(IConfig config);
    }
}
