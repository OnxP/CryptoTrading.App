using Binance;

namespace CryptoTrading.App.Core
{
    public interface IStrategyResult
    {
        bool PostTrade { get; }
        decimal Amount { get; set; }
        int Leverage { get; set; }
        OrderSide OrderSide { get; set; }
    }
}