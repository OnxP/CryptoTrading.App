using CryptoTrading.App.Broker;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeFactory
    {
        ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition, ITradeRequest request);
    }
}