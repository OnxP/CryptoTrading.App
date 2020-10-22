using Binance;

namespace CryptoTrading.App.Broker
{
    public interface IPosition
    {
        bool CheckFunds(string sellAmount);
        bool HasOpenPosition { get; set; }
        void UpdateOrder(Order order);
        decimal CalculateStopLoss(Order order);
    }
}