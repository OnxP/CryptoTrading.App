using CryptoTrading.App.Core.Trade;
using System.Collections.Generic;

namespace CryptoTrading.App.Core
{
    public interface ITradeProcessor
    {
        public List<ITrade> Trades { get; set; }

        void CompleteAllTransactions();
    }
}