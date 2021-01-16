using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Position
{
    public interface IPositions
    {
        bool CheckOpenPosition(string requestBuySymbol);
        ITrade CreateTrade(ITradeRequest request);
        bool CheckBalance(string sellSymbol, double sellAmount);
        void AddOrder(Order order);
        decimal CalculateStoploss(Order order);
        bool CheckRequest(ITradeRequest what);
    }
}