using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Position
{
    public interface IPositions
    {
        public IPosition GetPosition(string asset);
        ITrade CreateTrade(ITradeRequest request);
        bool CheckRequest(ITradeRequest what);
        void AjdustPosition(string accountPositionAsset, decimal accountPositionFree);
    }
}