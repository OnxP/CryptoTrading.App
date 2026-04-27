using Binance;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Algorithm
{
    internal class TradeRequest : ITradeRequest
    {
        // Binance lot-size / tick-size / notional info is still carried in a
        // Binance.Symbol because the rest of the algorithm's sizing path
        // depends on it. The ITradeRequest surface hides that by exposing
        // the pair as a string; IStrategyResult is mapped Binance -> neutral.
        private readonly Symbol _binanceSymbol;

        public TradeRequest(IStrategyResult strategyResult, IExecutionStrategy executionStrategy, Symbol symbol, DateTime closeTime)
        {
            StrategyResult = strategyResult;
            Strategy = executionStrategy;
            _binanceSymbol = symbol;
            RequestDateTime = closeTime;
        }

        public IStrategyResult StrategyResult { get; }

        public string Symbol => _binanceSymbol;  // implicit Symbol -> string
        public string BaseSymbol => _binanceSymbol.BaseAsset;
        public string QuoteSymbol => _binanceSymbol.QuoteAsset;

        public decimal Amount => StrategyResult.Amount;
        public int Leverage => StrategyResult.Leverage;

        public ExchangeOrderSide OrderSide => StrategyResult.OrderSide;

        public DateTime? RequestDateTime { get; }
        public IExecutionStrategy Strategy { get; set; }

        public bool Validate(decimal freeAmount, decimal nonFreeAmount)
        {
            throw new NotImplementedException();
        }
    }
}
