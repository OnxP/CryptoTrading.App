using Binance;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public static class RequestBuilder
    {
        public static ITradeRequest BuildTradeRequest(double result,bool fixedAmount, string ticker, decimal close, DateTime dateTime, IStopLimitTracker stopLimitTracker, decimal volume, decimal volumeLimit)
        {
            var tradeRequest = new MarketTradeRequest();
            tradeRequest.Symbol = Symbol.Cache.Get(ticker);
            tradeRequest.QuoteClosePrice = close;
            tradeRequest.FixedAmount = fixedAmount;
            tradeRequest.Amount = (decimal) result;
            tradeRequest.RequestDateTime = dateTime;
            tradeRequest.StopLimitTracker = stopLimitTracker;
            tradeRequest.Volume = volume;
            tradeRequest.VolumeLimit = volumeLimit;
            return tradeRequest;
        }


    }

    internal class Request : ITradeRequest
    {

        public string BaseSymbol => throw new NotImplementedException();

        public string QuoteSymbol => throw new NotImplementedException();

        public decimal Amount { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int Leverage { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public OrderSide OrderSide { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public DateTime? RequestDateTime { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public CandlestickInterval Interval { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public IExecutionStrategy Strategy { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public Symbol Symbol => throw new NotImplementedException();

        public bool Validate(decimal freeAmount, decimal nonFreeAmount)
        {
            throw new NotImplementedException();
        }
    }
}
