using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.TradeRequest
{
    public class BuyTradeRequestL : ITradeRequest
    {
        public string BuySymbol { get; set; }
        public string SellSymbol { get; set; }
        public string SellAmount { get; set; }
    }
}
