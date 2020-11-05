using System;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeRequest
    {
        string BaseSymbol { get; set; }
        string QuoteSymbol { get; set; }
        double SellPercentage { get; set; }
        string SellAmount { get; set; }
        decimal Price { get; }
        DateTime? RequestDateTime { get; set; }
    }
}
