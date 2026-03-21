using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Algorithm
{
    internal class TradeRequest : ITradeRequest
    {
        public TradeRequest(IStrategyResult strategyResult, IExecutionStrategy executionStrategy, ExchangeSymbol symbol, DateTime closeTime)
        {
            StrategyResult = strategyResult;
            Strategy = executionStrategy;
            Symbol = symbol;
            RequestDateTime = closeTime;
        }

        public IStrategyResult StrategyResult { get; }

        public ExchangeSymbol Symbol { get; }

        public string BaseSymbol => Symbol.BaseAsset;

        public string QuoteSymbol => Symbol.QuoteAsset;

        public decimal QuoteClosePrice { get; set; }
        public bool FixedAmount { get; set; }
        public decimal Amount { get => StrategyResult.Amount; set { } }
        public int Leverage { get => StrategyResult.Leverage; }
        public ExchangeOrderSide OrderSide { get => StrategyResult.OrderSide; }
        public DateTime? RequestDateTime { get; set; }
        public IStopLimitTracker StopLimitTracker { get; set; }
        public CandleInterval Interval { get; set; }
        public IExecutionStrategy Strategy { get; set; }
        public decimal Volume { get; set; }
        public decimal BaseQuantity { get; }
        public decimal QuoteQuantity { get; }

        public bool Validate(decimal freeAmount, decimal nonFreeAmount)
        {
            throw new NotImplementedException();
        }
    }
}
