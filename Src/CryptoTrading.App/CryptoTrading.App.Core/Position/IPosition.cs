using Binance;

namespace CryptoTrading.App.Broker
{
    public interface IPosition
    {
        string Symbol { get; }
        double Amount { get; }
        bool CheckFunds(double sellAmount);
        bool HasOpenPosition { get; set; }
        void UpdateOrder(Order order);
        decimal CalculateStopLoss(Order order);
    }
}