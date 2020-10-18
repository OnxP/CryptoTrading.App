using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Broker
{
    public interface IPositions
    {
        bool CheckOpenPosition(string requestBuySymbol);
        ITrade CreateTrade(ITradeRequest request, StopLossMonitor stopLossMonitor);
        bool CheckBalance(string sellSymbol, string sellAmount);
        void UpdatePosition(Order order);
    }
}