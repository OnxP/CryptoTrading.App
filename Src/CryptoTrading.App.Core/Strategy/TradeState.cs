namespace CryptoTrading.App.Core.Strategy
{
    public record TradeState(
        bool IsOpen,
        decimal ProfitPct,
        decimal TotalOpenBaseQuantity,
        decimal RemainingQuantity);
}
