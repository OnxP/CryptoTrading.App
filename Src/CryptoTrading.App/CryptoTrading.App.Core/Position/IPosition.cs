using Binance;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Position
{
    public interface IPosition
    {
        string Symbol { get; }
        decimal FreeAmount { get; }
        bool CheckFunds(double sellAmount);
        bool HasOpenPosition { get; set; }
        void UpdateOrder(Order order);
        decimal CalculateStopLoss(Order order);
        TransactionLeg CreateTransaction(decimal quantity);
    }
}