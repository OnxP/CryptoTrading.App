using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Entry strategy for precise 1-minute entry timing.
    /// Implements IEntryStrategy from Core.
    /// </summary>
    public class RegimeBasedEntryStrategy : IEntryStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private readonly SetupResult _setup;
        private readonly int _confirmationBars = 2;
        private readonly decimal _scaleInPortion = 0.33m;

        public RegimeBasedEntryStrategy(SetupResult setup)
        {
            _setup = setup;
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
        }

        public TradeDetails GetNextEntry(decimal currentPositionSize, decimal targetPositionSize, decimal close)
        {
            var result = new TradeDetails { ShouldTrade = false };

            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 20 || _setup == null)
                return result;

            return _setup.RecommendedEntryStrategy switch
            {
                EntryStrategyType.LimitAtSupport => EvaluateLimitAtSupport(close, targetPositionSize),
                EntryStrategyType.LimitAtResistance => EvaluateLimitAtResistance(close, targetPositionSize),
                EntryStrategyType.MarketOnConfirmation => EvaluateMarketOnConfirmation(close, targetPositionSize),
                EntryStrategyType.ScaleIn => EvaluateScaleIn(close, currentPositionSize, targetPositionSize),
                EntryStrategyType.BreakoutEntry => EvaluateBreakoutEntry(close, targetPositionSize),
                _ => result
            };
        }

        private TradeDetails EvaluateLimitAtSupport(decimal currentPrice, decimal targetSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            bool inEntryZone = currentPrice >= _setup.EntryZoneLow && currentPrice <= _setup.EntryZoneHigh;
            if (!inEntryZone) return result;

            var recentCandles = _quoteHub.Quotes.TakeLast(5).ToList();

            bool bullishEngulfing = IsBullishEngulfing(recentCandles);
            bool hammerPattern = IsHammerPattern(recentCandles.Last());
            bool risingMomentum = IsRisingMicroMomentum();

            if (bullishEngulfing || hammerPattern || risingMomentum)
            {
                result.ShouldTrade = true;
                result.EntryPrice = currentPrice;
                result.Price = currentPrice;
                result.Quantity = targetSize;
                result.OrderType = "LIMIT";
            }

            return result;
        }

        private TradeDetails EvaluateLimitAtResistance(decimal currentPrice, decimal targetSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            bool inEntryZone = currentPrice >= _setup.EntryZoneLow && currentPrice <= _setup.EntryZoneHigh;
            if (!inEntryZone) return result;

            var recentCandles = _quoteHub.Quotes.TakeLast(5).ToList();

            bool bearishEngulfing = IsBearishEngulfing(recentCandles);
            bool shootingStar = IsShootingStarPattern(recentCandles.Last());
            bool fallingMomentum = IsFallingMicroMomentum();

            if (bearishEngulfing || shootingStar || fallingMomentum)
            {
                result.ShouldTrade = true;
                result.EntryPrice = currentPrice;
                result.Price = currentPrice;
                result.Quantity = targetSize;
                result.OrderType = "LIMIT";
            }

            return result;
        }

        private TradeDetails EvaluateMarketOnConfirmation(decimal currentPrice, decimal targetSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            var recentCandles = _quoteHub.Quotes.TakeLast(_confirmationBars + 1).ToList();

            if (_setup.Direction == TradeDirection.Long)
            {
                bool consecutiveBullish = recentCandles.TakeLast(_confirmationBars).All(c => c.Close > c.Open);
                bool breakingMicroHigh = currentPrice > (decimal)recentCandles.SkipLast(1).Max(c => c.High);

                if (consecutiveBullish && breakingMicroHigh)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetSize;
                    result.OrderType = "MARKET";
                }
            }
            else
            {
                bool consecutiveBearish = recentCandles.TakeLast(_confirmationBars).All(c => c.Close < c.Open);
                bool breakingMicroLow = currentPrice < (decimal)recentCandles.SkipLast(1).Min(c => c.Low);

                if (consecutiveBearish && breakingMicroLow)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetSize;
                    result.OrderType = "MARKET";
                }
            }

            return result;
        }

        private TradeDetails EvaluateScaleIn(decimal currentPrice, decimal currentSize, decimal targetSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal entryZoneRange = _setup.EntryZoneHigh - _setup.EntryZoneLow;
            decimal firstLevel = _setup.EntryZoneLow + entryZoneRange * 0.33m;
            decimal secondLevel = _setup.EntryZoneLow + entryZoneRange * 0.66m;
            decimal thirdLevel = _setup.EntryZoneHigh;

            bool atFirstLevel = Math.Abs(currentPrice - firstLevel) < entryZoneRange * 0.1m;
            bool atSecondLevel = Math.Abs(currentPrice - secondLevel) < entryZoneRange * 0.1m;
            bool atThirdLevel = Math.Abs(currentPrice - thirdLevel) < entryZoneRange * 0.1m;

            var recentCandles = _quoteHub.Quotes.TakeLast(3).ToList();
            bool hasReversalSignal = _setup.Direction == TradeDirection.Long
                ? (IsBullishEngulfing(recentCandles) || IsHammerPattern(recentCandles.Last()))
                : (IsBearishEngulfing(recentCandles) || IsShootingStarPattern(recentCandles.Last()));

            if ((atFirstLevel || atSecondLevel || atThirdLevel) && hasReversalSignal)
            {
                result.ShouldTrade = true;
                result.EntryPrice = currentPrice;
                result.Price = currentPrice;
                result.Quantity = targetSize * _scaleInPortion;
                result.OrderType = "LIMIT";
            }

            return result;
        }

        private TradeDetails EvaluateBreakoutEntry(decimal currentPrice, decimal targetSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            var recentCandles = _quoteHub.Quotes.TakeLast(20).ToList();
            var lastCandle = recentCandles.Last();

            if (_setup.Direction == TradeDirection.Long)
            {
                decimal breakoutLevel = _setup.EntryZoneLow;
                bool volumeConfirm = GetVolumeRatio() > 1.5m;
                bool cleanBreak = (decimal)lastCandle.Close > breakoutLevel && (decimal)lastCandle.Open < breakoutLevel;

                bool retestingBreakout = recentCandles.Any(c => (decimal)c.Close > breakoutLevel) &&
                                         currentPrice <= breakoutLevel * 1.003m &&
                                         currentPrice >= breakoutLevel * 0.997m;

                if ((cleanBreak && volumeConfirm) || retestingBreakout)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetSize;
                    result.OrderType = cleanBreak ? "MARKET" : "LIMIT";
                }
            }
            else
            {
                decimal breakdownLevel = _setup.EntryZoneHigh;
                bool volumeConfirm = GetVolumeRatio() > 1.5m;
                bool cleanBreak = (decimal)lastCandle.Close < breakdownLevel && (decimal)lastCandle.Open > breakdownLevel;

                bool retestingBreakdown = recentCandles.Any(c => (decimal)c.Close < breakdownLevel) &&
                                          currentPrice >= breakdownLevel * 0.997m &&
                                          currentPrice <= breakdownLevel * 1.003m;

                if ((cleanBreak && volumeConfirm) || retestingBreakdown)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetSize;
                    result.OrderType = cleanBreak ? "MARKET" : "LIMIT";
                }
            }

            return result;
        }

        #region Pattern Recognition

        private bool IsBullishEngulfing(List<IQuote> candles)
        {
            if (candles.Count < 2) return false;
            var prev = candles[^2];
            var curr = candles[^1];
            return prev.Close < prev.Open &&
                   curr.Close > curr.Open &&
                   curr.Open <= prev.Close &&
                   curr.Close > prev.Open;
        }

        private bool IsBearishEngulfing(List<IQuote> candles)
        {
            if (candles.Count < 2) return false;
            var prev = candles[^2];
            var curr = candles[^1];
            return prev.Close > prev.Open &&
                   curr.Close < curr.Open &&
                   curr.Open >= prev.Close &&
                   curr.Close < prev.Open;
        }

        private bool IsHammerPattern(IQuote candle)
        {
            if (candle == null) return false;
            var range = candle.High - candle.Low;
            if (range == 0) return false;

            var body = Math.Abs(candle.Close - candle.Open);
            var lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
            var upperWick = candle.High - Math.Max(candle.Open, candle.Close);

            return lowerWick > body * 2 && upperWick < body * 0.5m;
        }

        private bool IsShootingStarPattern(IQuote candle)
        {
            if (candle == null) return false;
            var range = candle.High - candle.Low;
            if (range == 0) return false;

            var body = Math.Abs(candle.Close - candle.Open);
            var lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
            var upperWick = candle.High - Math.Max(candle.Open, candle.Close);

            return upperWick > body * 2 && lowerWick < body * 0.5m;
        }

        private bool IsRisingMicroMomentum()
        {
            if (_quoteHub.Quotes.Count < 10) return false;
            var recent = _quoteHub.Quotes.TakeLast(5).Average(c => c.Close);
            var prior = _quoteHub.Quotes.Skip(_quoteHub.Quotes.Count - 10).Take(5).Average(c => c.Close);
            return recent > prior;
        }

        private bool IsFallingMicroMomentum()
        {
            if (_quoteHub.Quotes.Count < 10) return false;
            var recent = _quoteHub.Quotes.TakeLast(5).Average(c => c.Close);
            var prior = _quoteHub.Quotes.Skip(_quoteHub.Quotes.Count - 10).Take(5).Average(c => c.Close);
            return recent < prior;
        }

        private decimal GetVolumeRatio()
        {
            if (_quoteHub.Quotes.Count < 21) return 1m;
            var volumes = _quoteHub.Quotes.TakeLast(21).Select(q => q.Volume).ToList();
            var currentVolume = volumes.Last();
            var avgVolume = volumes.Take(20).Average();
            return avgVolume > 0 ? (decimal)(currentVolume / avgVolume) : 1m;
        }

        #endregion
    }
}