using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Monitor
{
    public class TestTradeFactory : ITradeFactory
    {
        public ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition, ITradeRequest request)
        {
            var trade = new Trade(buyPosition, sellPosition, feePosition, request);
            trade.Open = true;
            return trade;
            //should add a transaction for each ccy, seperate one for the fee.
            //since the same trade is used for the stop loss we can combine the transactions.
        }
    }
}
