using Binance;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Position
{
    public interface IPosition
    {
        string Symbol { get; }
        decimal FreeAmount { get; }
        decimal NonFreeAmount { get; }
        bool CheckFunds(double sellAmount);
        bool HasOpenPosition { get;}
        TransactionLeg CreateTransaction(decimal quantity);
    }
}