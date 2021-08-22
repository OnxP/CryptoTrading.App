using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using System.Collections.Generic;

namespace CryptoTrading.App.Core
{
    public interface ITradeProcessor
    {
        public List<ITrade> Trades { get; set; }
        public IPositions Positions { get; set; }

        void CompleteAllTransactions();
    }
}