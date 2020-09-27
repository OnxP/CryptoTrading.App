namespace CryptoTrading.App.Core.TradeRequest
{
    public class SellTradeRequest : ITradeRequest
    {
        public string BuySymbol { get; set; }
        public string SellSymbol { get; set; }
        public string SellAmount { get; set; }
    }
}
