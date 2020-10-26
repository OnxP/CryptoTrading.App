using CryptoTrading.App.Broker;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeFactory
    {
        ITrade CreateTrade(string requestBuySymbol, string requestSellSymbol);
        ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, ITradeRequest request);
        ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition, ITradeRequest request);
    }
}