using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Position
{
    public interface IPosition
    {
        string Symbol { get; }
        decimal FreeAmount { get; }
        decimal NonFreeAmount { get; }
        bool HasOpenPosition { get;}
        bool IsLocked { get; set; }

        TransactionLeg CreatePendingTransaction(decimal quantity);
        TransactionLeg CreateTransaction(decimal quantity);
        bool CheckFunds(ITradeRequest sellAmount);
    }
}