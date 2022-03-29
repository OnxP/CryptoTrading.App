using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Position
{
    public interface IPositions
    {
        public IPosition GetPosition(string asset);
        bool CheckOpenPosition(string requestBuySymbol);
        ITrade CreateTrade(ITradeRequest request);
        bool CheckBalance(ITradeRequest what);
        bool CheckRequest(ITradeRequest what);
        void AjdustPosition(string accountPositionAsset, decimal accountPositionFree);
    }
}