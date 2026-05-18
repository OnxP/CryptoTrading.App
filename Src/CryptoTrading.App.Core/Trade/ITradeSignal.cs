using CryptoTrading.App.Core.Strategy;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public interface ITradeSignal
    {
        string Symbol { get; }
        string BaseSymbol { get; }
        string QuoteSymbol { get; }
        TradeDirection Direction { get; }
        int Leverage { get; }
        decimal Quantity { get; }
        DateTime SignalTime { get; }

        decimal EntryPrice { get; }
        decimal StopLoss { get; }
        decimal TakeProfit { get; }
        decimal AtrAtSignal { get; }
        decimal InitialRisk { get; }

        decimal? HtfRsi { get; }
        decimal? VolExpansionRatio { get; }
        decimal? ProbabilityScore { get; }
        string SetupType { get; }
        string RecommendedEntryStrategy { get; }
        string RecommendedExitStrategy { get; }
    }
}
