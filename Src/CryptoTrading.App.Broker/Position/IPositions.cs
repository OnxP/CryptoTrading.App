using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Broker.Position
{
    public interface IPositions
    {
        IPosition GetPosition(string asset);
        bool CheckRequest(ITradeRequest what);
        void AjdustPosition(string accountPositionAsset, decimal accountPositionFree);
    }
}
