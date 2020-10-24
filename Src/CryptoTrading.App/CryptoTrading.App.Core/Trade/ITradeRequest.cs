namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeRequest
    {
        string BuySymbol { get; set; }
        string SellSymbol { get; set; }
        double SellPercentage { get; set; }
        string SellAmount { get; set; }
    }
}
