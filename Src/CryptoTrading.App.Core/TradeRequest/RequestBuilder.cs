using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public static class RequestBuilder
    {
        public static ITradeRequest BuildTradeRequest(double result,bool fixedAmount, string ticker, decimal close, DateTime dateTime, IStopLimitTracker stopLimitTracker, decimal volume, decimal volumeLimit)
        {
            var tradeRequest = new MarketTradeRequest();
            tradeRequest.Symbol = ExchangeSymbolCache.Instance.Get(ticker);
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
        public ExchangeOrderSide OrderSide { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public DateTime? RequestDateTime { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public CandleInterval Interval { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public IExecutionStrategy Strategy { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public ExchangeSymbol Symbol => throw new NotImplementedException();

        public decimal QuoteClosePrice => throw new NotImplementedException();

        public bool FixedAmount { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IStopLimitTracker StopLimitTracker { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public decimal Volume { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public decimal BaseQuantity => throw new NotImplementedException();

        public decimal QuoteQuantity => throw new NotImplementedException();

        public bool Validate(decimal freeAmount, decimal nonFreeAmount)
        {
            throw new NotImplementedException();
        }
    }
}
