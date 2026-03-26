using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased.EntryStrategies
{
    /// <summary>
    /// Scores entry confluence by combining multiple independent signals.
    /// Requires a minimum total score (default 2.0) to allow entry,
    /// ensuring multiple confirmations rather than relying on a single indicator.
    ///
    /// Signal scores:
    ///   StochRsi crossover in oversold/overbought zone: +1.0
    ///   Candlestick pattern (engulfing/hammer/shooting star): +0.5
    ///   Volume surge (> 1.5x average): +0.5
    ///   Price at supply/demand zone: +0.5
    ///   MACD histogram growing in trade direction: +0.5
    ///   EMA alignment (8 > 21 for long, 8 < 21 for short): +0.3
    ///   Momentum (15-bar, not 5): +0.3
    ///
    /// Maximum possible score: 3.6
    /// Minimum required: 2.0
    /// </summary>
    public class ConfluenceScorer
    {
        public decimal MinimumScore { get; set; } = 2.0m;

        private ILogger _logger;

        public void SetLogger(ILogger logger) => _logger = logger;

        /// <summary>
        /// Calculate total confluence score for a potential entry.
        /// </summary>
        public ConfluenceResult Score(
            QuoteHub<IQuote> quoteHub,
            SetupResult setup,
            decimal currentPrice)
        {
            var result = new ConfluenceResult();
            if (quoteHub?.Quotes == null || quoteHub.Quotes.Count < 50) return result;

            var quotes = quoteHub.Quotes;
            var direction = setup.Direction;

            // 1. StochRsi crossover in extreme zone (+1.0)
            var stochScore = ScoreStochRsi(quotes, direction);
            result.StochRsiScore = stochScore;
            result.TotalScore += stochScore;

            // 2. Candlestick pattern (+0.5)
            var patternScore = ScoreCandlePattern(quotes, direction);
            result.PatternScore = patternScore;
            result.TotalScore += patternScore;

            // 3. Volume surge (+0.5)
            var volumeScore = ScoreVolume(quotes);
            result.VolumeScore = volumeScore;
            result.TotalScore += volumeScore;

            // 4. Zone proximity (+0.5)
            var zoneScore = setup.IsZoneTrade ? 0.5m : 0m;
            result.ZoneScore = zoneScore;
            result.TotalScore += zoneScore;

            // 5. MACD histogram direction (+0.5)
            var macdScore = ScoreMacdMomentum(quotes, direction);
            result.MacdScore = macdScore;
            result.TotalScore += macdScore;

            // 6. EMA alignment (+0.3)
            var emaScore = ScoreEmaAlignment(quotes, direction);
            result.EmaScore = emaScore;
            result.TotalScore += emaScore;

            // 7. Extended momentum 15-bar (+0.3)
            var momentumScore = ScoreExtendedMomentum(quotes, direction);
            result.MomentumScore = momentumScore;
            result.TotalScore += momentumScore;

            result.PassesMinimum = result.TotalScore >= MinimumScore;

            _logger?.LogDebug(
                $"[CONFLUENCE {direction}] Total:{result.TotalScore:F2} Pass:{result.PassesMinimum} | " +
                $"StochRsi:{stochScore:F1} Pattern:{patternScore:F1} Vol:{volumeScore:F1} " +
                $"Zone:{zoneScore:F1} MACD:{macdScore:F1} EMA:{emaScore:F1} Mom:{momentumScore:F1}");

            return result;
        }

        private decimal ScoreStochRsi(IReadOnlyList<IQuote> quotes, TradeDirection direction)
        {
            try
            {
                var stochRsi = quotes.ToStochRsi(14, 14, 3, 3);
                if (stochRsi == null || stochRsi.Count < 2) return 0;

                var current = stochRsi[stochRsi.Count - 1];
                var previous = stochRsi[stochRsi.Count - 2];
                if (current.StochRsi == null || current.Signal == null ||
                    previous.StochRsi == null || previous.Signal == null) return 0;

                var currStoch = (decimal)current.StochRsi.Value;
                var prevStoch = (decimal)previous.StochRsi.Value;
                var currSignal = (decimal)current.Signal.Value;
                var prevSignal = (decimal)previous.Signal.Value;

                if (direction == TradeDirection.Long)
                {
                    // Oversold + bullish crossover
                    bool wasOversold = prevStoch < 25;
                    bool bullishCross = prevStoch <= prevSignal && currStoch > currSignal;
                    if (wasOversold && bullishCross) return 1.0m;
                    if (currStoch < 30 && bullishCross) return 0.5m; // Partial credit
                }
                else
                {
                    bool wasOverbought = prevStoch > 75;
                    bool bearishCross = prevStoch >= prevSignal && currStoch < currSignal;
                    if (wasOverbought && bearishCross) return 1.0m;
                    if (currStoch > 70 && bearishCross) return 0.5m;
                }
            }
            catch { /* indicator calculation failure */ }
            return 0;
        }

        private decimal ScoreCandlePattern(IReadOnlyList<IQuote> quotes, TradeDirection direction)
        {
            if (quotes.Count < 3) return 0;
            var candles = quotes.Skip(quotes.Count - 3).ToList();
            var curr = candles[^1];
            var prev = candles[^2];

            if (direction == TradeDirection.Long)
            {
                // Bullish engulfing
                if (prev.Close < prev.Open && curr.Close > curr.Open &&
                    curr.Open <= prev.Close && curr.Close > prev.Open)
                    return 0.5m;

                // Hammer
                var body = Math.Abs(curr.Close - curr.Open);
                var lowerWick = Math.Min(curr.Open, curr.Close) - curr.Low;
                if (body > 0 && lowerWick > body * 2)
                    return 0.5m;
            }
            else
            {
                // Bearish engulfing
                if (prev.Close > prev.Open && curr.Close < curr.Open &&
                    curr.Open >= prev.Close && curr.Close < prev.Open)
                    return 0.5m;

                // Shooting star
                var body = Math.Abs(curr.Close - curr.Open);
                var upperWick = curr.High - Math.Max(curr.Open, curr.Close);
                if (body > 0 && upperWick > body * 2)
                    return 0.5m;
            }
            return 0;
        }

        private decimal ScoreVolume(IReadOnlyList<IQuote> quotes)
        {
            if (quotes.Count < 21) return 0;
            var volumes = quotes.Skip(quotes.Count - 21).Select(q => q.Volume).ToList();
            var currentVol = volumes.Last();
            var avgVol = volumes.Take(20).Average();
            // 1.5x average = full score, 1.2x = half
            if (avgVol > 0 && currentVol >= avgVol * 1.5m) return 0.5m;
            if (avgVol > 0 && currentVol >= avgVol * 1.2m) return 0.25m;
            return 0;
        }

        private decimal ScoreMacdMomentum(IReadOnlyList<IQuote> quotes, TradeDirection direction)
        {
            try
            {
                var macd = quotes.ToMacd(12, 26, 9);
                if (macd == null || macd.Count < 2) return 0;

                var curr = macd[macd.Count - 1];
                var prev = macd[macd.Count - 2];
                if (curr.Histogram == null || prev.Histogram == null) return 0;

                var currHist = (decimal)curr.Histogram.Value;
                var prevHist = (decimal)prev.Histogram.Value;

                if (direction == TradeDirection.Long)
                {
                    // Growing positive histogram or crossing above zero
                    if (currHist > 0 && currHist > prevHist) return 0.5m;
                    if (prevHist < 0 && currHist > 0) return 0.5m; // Zero cross
                }
                else
                {
                    if (currHist < 0 && currHist < prevHist) return 0.5m;
                    if (prevHist > 0 && currHist < 0) return 0.5m;
                }
            }
            catch { }
            return 0;
        }

        private decimal ScoreEmaAlignment(IReadOnlyList<IQuote> quotes, TradeDirection direction)
        {
            try
            {
                var ema8 = quotes.ToEma(8);
                var ema21 = quotes.ToEma(21);
                if (ema8 == null || ema21 == null || ema8.Count < 1 || ema21.Count < 1) return 0;

                var fast = (decimal)(ema8.Last().Ema ?? 0);
                var slow = (decimal)(ema21.Last().Ema ?? 0);
                if (fast == 0 || slow == 0) return 0;

                if (direction == TradeDirection.Long && fast > slow) return 0.3m;
                if (direction == TradeDirection.Short && fast < slow) return 0.3m;
            }
            catch { }
            return 0;
        }

        private decimal ScoreExtendedMomentum(IReadOnlyList<IQuote> quotes, TradeDirection direction)
        {
            if (quotes.Count < 30) return 0;

            // 15-bar vs prior 15-bar average close
            var recent15 = quotes.Skip(quotes.Count - 15).Select(q => q.Close).Average();
            var prior15 = quotes.Skip(quotes.Count - 30).Take(15).Select(q => q.Close).Average();

            if (direction == TradeDirection.Long && recent15 > prior15) return 0.3m;
            if (direction == TradeDirection.Short && recent15 < prior15) return 0.3m;
            return 0;
        }
    }

    public class ConfluenceResult
    {
        public decimal TotalScore { get; set; }
        public bool PassesMinimum { get; set; }

        public decimal StochRsiScore { get; set; }
        public decimal PatternScore { get; set; }
        public decimal VolumeScore { get; set; }
        public decimal ZoneScore { get; set; }
        public decimal MacdScore { get; set; }
        public decimal EmaScore { get; set; }
        public decimal MomentumScore { get; set; }
    }
}
