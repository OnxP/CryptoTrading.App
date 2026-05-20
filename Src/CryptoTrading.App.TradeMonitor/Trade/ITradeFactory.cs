using CryptoTrading.App.Broker.Position;

namespace CryptoTrading.App.Monitor.Trade
{
    public interface ITradeFactory
    {
        ITrade CreateTrade(IPosition basePosition, IPosition quotePosition, IPosition feePosition);
    }
}
