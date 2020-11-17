using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public class BuyTradeRequest : ITradeRequest
    {
        public string BaseSymbol { get; set; }
        public string QuoteSymbol { get; set; }
        public decimal SellAmount { get; set; }
        public double SellPercentage { get; set; }
        public decimal Price { get; internal set; }
        public DateTime? RequestDateTime { get; set; }
    }
}
