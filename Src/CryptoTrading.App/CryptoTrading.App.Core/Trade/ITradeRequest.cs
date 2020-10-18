namespace CryptoTrading.App.Core
{
    public interface ITradeRequest
    {
        string BuySymbol { get; set; }
        string SellSymbol { get; set; }
        string SellAmount { get; set; }
    }
}
