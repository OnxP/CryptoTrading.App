using CryptoTrading.App.Core;
using Skender.Stock.Indicators;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoTrading.App.Algorithm
{
    public class ExitStrategy : IExitStrategy
    {
        public TradeDetails GetNextExit(decimal currentPositionSize, decimal close, decimal profit)
        {
            throw new NotImplementedException();
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            throw new NotImplementedException();
        }
    }
}
