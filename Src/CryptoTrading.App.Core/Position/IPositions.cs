using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Position
{
    public interface IPositions
    {
        public IPosition GetPosition(string asset);
        bool CheckOpenPosition(string requestBuySymbol);
        ITrade CreateTrade(ITradeRequest request);
        bool CheckBalance(string sellSymbol, double sellAmount);
        bool CheckRequest(ITradeRequest what);
    }
}