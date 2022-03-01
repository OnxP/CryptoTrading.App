using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using System.Collections.Generic;

namespace CryptoTrading.App.Core
{
    public interface ITradeProcessor
    {
        public IPositions Positions { get; set; }
        void CompleteAllTransactions();
        void ClearInactiveTrades();
        List<ITrade> GetCompletedTrades();
        void Configure(IConfig config);
    }
}