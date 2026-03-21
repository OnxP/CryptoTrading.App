using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core.Strategy
{
    public interface IStrategyResult
    {
        bool PostTrade { get; }
        decimal Amount { get; set; }
        int Leverage { get; set; }
        ExchangeOrderSide OrderSide { get; set; }
    }
    public class StrategyResult : IStrategyResult
    {
        public bool PostTrade { get; set; }
        public decimal Amount { get; set; }
        public int Leverage { get; set; }
        public ExchangeOrderSide OrderSide { get; set; }
    }
}