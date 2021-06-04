using Binance;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public static class RequestBuilder
    {
        public static ITradeRequest BuildTradeRequest(double result, string ticker, decimal close, DateTime dateTime, IStopLimitTracker stopLimitTracker)
        {
            var tradeRequest = new BuyTradeRequest();
            Symbol symbol = Symbol.Cache.Get(ticker);
            tradeRequest.BaseSymbol = symbol.BaseAsset;
            tradeRequest.QuoteSymbol = symbol.QuoteAsset;
            tradeRequest.Price = close;
            tradeRequest.SellPercentage = result;
            tradeRequest.RequestDateTime = dateTime;
            tradeRequest.StopLimitTracker = stopLimitTracker;
            return tradeRequest;
        }
    }
}
