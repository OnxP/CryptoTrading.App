using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core
{
    public interface IBroker
    {
        void ClosePosition(ITrade trade);
    }
}
