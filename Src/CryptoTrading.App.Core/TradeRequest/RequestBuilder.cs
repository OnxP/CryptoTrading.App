using Binance;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public static class RequestBuilder
    {
        public static ITradeRequest BuildTradeRequest(double result,bool fixedAmount, string ticker, decimal close, DateTime dateTime, IStopLimitTracker stopLimitTracker, decimal volume, decimal volumeLimit)
        {
            var tradeRequest = new BuyTradeRequest();
            Symbol symbol = Symbol.Cache.Get(ticker);
            tradeRequest.BaseSymbol = symbol.BaseAsset;
            tradeRequest.QuoteSymbol = symbol.QuoteAsset;
            tradeRequest.Price = close;
            tradeRequest.FixedAmount = fixedAmount;
            tradeRequest.Amount = result;
            tradeRequest.RequestDateTime = dateTime;
            tradeRequest.StopLimitTracker = stopLimitTracker;
            tradeRequest.Volume = volume;
            tradeRequest.VolumeLimit = volumeLimit;
            return tradeRequest;
        }
    }
}
