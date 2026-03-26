using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased.EntryStrategies
{
    /// <summary>
    /// Abstract base class for all regime-based entry strategies.
    /// Provides shared pattern recognition helpers and quote management.
    /// Concrete subclasses implement specific entry logic.
    /// </summary>
    public abstract class RegimeBasedEntryStrategyBase : IEntryStrategy
    {
        protected QuoteHub<IQuote> QuoteHub;
        protected readonly SetupResult Setup;
        protected ILogger Logger;

        // Confluence scorer — requires multiple signals to confirm entry
        private readonly ConfluenceScorer _confluenceScorer = new ConfluenceScorer();

        public void SetLogger(ILogger logger)
        {
            Logger = logger;
            _confluenceScorer.SetLogger(logger);
        }

        protected RegimeBasedEntryStrategyBase(SetupResult setup)
        {
            Setup = setup;
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            QuoteHub = quoteHub;
        }

        public TradeDetails GetNextEntry(decimal currentPositionSize, decimal targetPositionSize, decimal close)
        {
            var result = new TradeDetails { ShouldTrade = false };

            if (QuoteHub?.Quotes == null || QuoteHub.Quotes.Count < 20 || Setup == null)
                return result;

            // Confluence gate: require minimum score from multiple independent signals
            // before allowing any entry strategy to fire.
            var confluence = _confluenceScorer.Score(QuoteHub, Setup, close);
            if (!confluence.PassesMinimum)
            {
                Logger?.LogDebug($"[1M ENTRY] Confluence too low ({confluence.TotalScore:F2} < {_confluenceScorer.MinimumScore}) — skipping entry");
                return result;
            }

            Logger?.LogInformation($"[1M ENTRY] Confluence PASS ({confluence.TotalScore:F2}) — evaluating {Setup.RecommendedEntryStrategy}");

            return Evaluate(close, currentPositionSize, targetPositionSize);
        }

        /// <summary>
        /// Evaluate whether an entry should be taken at the current price.
        /// Implemented by each concrete entry strategy.
        /// </summary>
        protected abstract TradeDetails Evaluate(decimal currentPrice, decimal currentPositionSize, decimal targetPositionSize);

        /// <summary>
        /// Creates the correct entry strategy subclass based on the setup's recommended entry type.
        /// </summary>
        public static RegimeBasedEntryStrategyBase Create(SetupResult setup)
        {
            return setup.RecommendedEntryStrategy switch
            {
                EntryStrategyType.LimitAtSupport => new LimitAtSupportEntryStrategy(setup),
                EntryStrategyType.LimitAtResistance => new LimitAtResistanceEntryStrategy(setup),
                EntryStrategyType.MarketOnConfirmation => new MarketOnConfirmationEntryStrategy(setup),
                EntryStrategyType.ScaleIn => new ScaleInEntryStrategy(setup),
                EntryStrategyType.BreakoutEntry => new BreakoutEntryStrategy(setup),
                EntryStrategyType.StochRsiEntry => new StochRsiEntryStrategy(setup),
                EntryStrategyType.LimitAtZoneEdge => new LimitAtZoneEdgeEntryStrategy(setup),
                _ => new StochRsiEntryStrategy(setup)
            };
        }

        #region Pattern Recognition Helpers

        protected bool IsBullishEngulfing(List<IQuote> candles)
        {
            if (candles.Count < 2) return false;
            var prev = candles[^2];
            var curr = candles[^1];
            return prev.Close < prev.Open &&
                   curr.Close > curr.Open &&
                   curr.Open <= prev.Close &&
                   curr.Close > prev.Open;
        }

        protected bool IsBearishEngulfing(List<IQuote> candles)
        {
            if (candles.Count < 2) return false;
            var prev = candles[^2];
            var curr = candles[^1];
            return prev.Close > prev.Open &&
                   curr.Close < curr.Open &&
                   curr.Open >= prev.Close &&
                   curr.Close < prev.Open;
        }

        protected bool IsHammerPattern(IQuote candle)
        {
            if (candle == null) return false;
            var range = candle.High - candle.Low;
            if (range == 0) return false;

            var body = Math.Abs(candle.Close - candle.Open);
            var lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
            var upperWick = candle.High - Math.Max(candle.Open, candle.Close);

            return lowerWick > body * 2 && upperWick < body * 0.5m;
        }

        protected bool IsShootingStarPattern(IQuote candle)
        {
            if (candle == null) return false;
            var range = candle.High - candle.Low;
            if (range == 0) return false;

            var body = Math.Abs(candle.Close - candle.Open);
            var lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
            var upperWick = candle.High - Math.Max(candle.Open, candle.Close);

            return upperWick > body * 2 && lowerWick < body * 0.5m;
        }

        protected bool IsRisingMicroMomentum()
        {
            // Extended from 5 to 15 bars for more reliable momentum signal (less noise)
            if (QuoteHub.Quotes.Count < 30) return false;
            var recent = QuoteHub.Quotes.TakeLast(15).Average(c => c.Close);
            var prior = QuoteHub.Quotes.Skip(QuoteHub.Quotes.Count - 30).Take(15).Average(c => c.Close);
            return recent > prior;
        }

        protected bool IsFallingMicroMomentum()
        {
            // Extended from 5 to 15 bars for more reliable momentum signal (less noise)
            if (QuoteHub.Quotes.Count < 30) return false;
            var recent = QuoteHub.Quotes.TakeLast(15).Average(c => c.Close);
            var prior = QuoteHub.Quotes.Skip(QuoteHub.Quotes.Count - 30).Take(15).Average(c => c.Close);
            return recent < prior;
        }

        protected decimal GetVolumeRatio()
        {
            if (QuoteHub.Quotes.Count < 21) return 1m;
            var volumes = QuoteHub.Quotes.TakeLast(21).Select(q => q.Volume).ToList();
            var currentVolume = volumes.Last();
            var avgVolume = volumes.Take(20).Average();
            return avgVolume > 0 ? (decimal)(currentVolume / avgVolume) : 1m;
        }

        #endregion
    }
}
