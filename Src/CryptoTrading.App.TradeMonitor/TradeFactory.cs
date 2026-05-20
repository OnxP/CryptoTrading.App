using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Monitor.Trade;

namespace CryptoTrading.App.Monitor
{
    public class TradeFactory : ITradeFactory
    {
        public ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition)
        {
            return new Trade.Trade(buyPosition, sellPosition, feePosition);
        }
    }
}
