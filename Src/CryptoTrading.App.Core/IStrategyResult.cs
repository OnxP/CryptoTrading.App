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
    public class StrategyResult : IStrategyResult
    {
        public bool PostTrade { get; set; }
        public decimal Amount { get; set; }
        public int Leverage { get; set; }
        public OrderSide OrderSide { get; set; }
    }
}