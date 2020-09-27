using CryptoTrading.App.Core;

namespace CryptoTrading.App.Broker
{
    public interface IPositions
    {
        bool CheckOpenPosition(string requestBuySymbol);
        ITrade CreatePosition(ITradeRequest request, StopLossMonitor stopLossMonitor);
        bool CheckBalance(string sellSymbol, string sellAmount);
    }
}