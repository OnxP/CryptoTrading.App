using CryptoTrading.App.Core.Position;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeFactory
    {
        ITrade CreateTrade(IPosition basePosition, IPosition quotePosition, IPosition feePosition);
    }
}