using Binance;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.TradeRequest
{
    public static class RequestBuilder
    {
        public static ITradeRequest BuildTradeRequest(double result, string ticker, decimal close)
        {
            var tradeRequest = new BuyTradeRequest();
            Symbol symbol = Symbol.Cache.Get(ticker);
            tradeRequest.BuySymbol = symbol.BaseAsset;
            tradeRequest.SellSymbol = symbol.QuoteAsset;
            tradeRequest.Price = close;
            tradeRequest.SellPercentage = result;
            return tradeRequest;
        }
    }
}
