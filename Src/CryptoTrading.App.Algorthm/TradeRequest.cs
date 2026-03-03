using Binance;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Algorithm
{
    internal class TradeRequest : ITradeRequest
    {
        public TradeRequest(IStrategyResult strategyResult, IExecutionStrategy executionStrategy, Symbol symbol, DateTime closeTime)
        {
            StrategyResult = strategyResult;
            Strategy = executionStrategy;
            Symbol = symbol;
            RequestDateTime = closeTime;
        }

        public IStrategyResult StrategyResult { get; }

        public Symbol Symbol { get; }

        public string BaseSymbol => Symbol.BaseAsset;

        public string QuoteSymbol => Symbol.QuoteAsset;

        public decimal Amount { get => StrategyResult.Amount; }
        public int Leverage { get => StrategyResult.Leverage; }
        public OrderSide OrderSide { get => StrategyResult.OrderSide; }
        public DateTime? RequestDateTime { get; }
        public IExecutionStrategy Strategy { get; set; }

        public bool Validate(decimal freeAmount, decimal nonFreeAmount)
        {
            throw new NotImplementedException();
        }
    }
}