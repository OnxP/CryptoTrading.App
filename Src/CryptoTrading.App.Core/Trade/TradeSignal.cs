using CryptoTrading.App.Core.Strategy;
using System;

namespace CryptoTrading.App.Core.Trade
{
    public class TradeSignal : ITradeSignal
    {
        public string Symbol { get; set; }
        public string BaseSymbol { get; set; }
        public string QuoteSymbol { get; set; }
        public TradeDirection Direction { get; set; }
        public int Leverage { get; set; }
        public decimal Quantity { get; set; }
        public DateTime SignalTime { get; set; }

        public decimal EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal AtrAtSignal { get; set; }
        public decimal InitialRisk { get; set; }

        public decimal? HtfRsi { get; set; }
        public decimal? VolExpansionRatio { get; set; }
        public decimal? ProbabilityScore { get; set; }
        public string SetupType { get; set; }
        public string RecommendedEntryStrategy { get; set; }
        public string RecommendedExitStrategy { get; set; }
    }
}
