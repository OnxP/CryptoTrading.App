using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// 1-minute timeframe execution strategy.
    /// Implements IExecutionStrategy from Core.
    /// 
    /// Handles precise entry timing and active position management.
    /// </summary>
    public class RegimeBasedExecutionStrategy : IExecutionStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private readonly SetupResult _setup;

        public IEntryStrategy EntryStrategy { get; set; }
        public IExitStrategy ExitStrategy { get; set; }
        public decimal Quantity { get; set; }

        public RegimeBasedExecutionStrategy(QuoteHub<IQuote> quoteHub, SetupResult setup)
        {
            _setup = setup;
            Quantity = 0.1m; // Default, will be set by caller

            EntryStrategy = new RegimeBasedEntryStrategy(setup);
            ExitStrategy = new RegimeBasedExitStrategy(setup);

            SetQuotes(quoteHub);
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
            EntryStrategy?.SetQuotes(quoteHub);
            ExitStrategy?.SetQuotes(quoteHub);
        }

        public decimal GetEntryPrice()
        {
            if (_setup == null) return 0;
            return (_setup.EntryZoneHigh + _setup.EntryZoneLow) / 2;
        }

        public StrategyStatus ProcessStrategy(ITrade trade)
        {
            var status = new StrategyStatus
            {
                StrategyAction = StrategyAction.NoAction,
                StrategyState = StrategyState.WaitingForEntry
            };

            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 20)
                return status;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;

            // Not in trade - check for entry
            if (!trade.Open)
            {
                var entryDetails = EntryStrategy.GetNextEntry(0, Quantity, currentPrice);
                if (entryDetails.ShouldTrade)
                {
                    status.StrategyAction = StrategyAction.OpenTrade;
                    status.StrategyState = StrategyState.WaitingForEntry;
                }
                return status;
            }

            // In trade - check for exit
            status.StrategyState = StrategyState.WaitingForExit;
            var exitDetails = ExitStrategy.GetNextExit(trade.Quantity, currentPrice, trade.Profit);
            if (exitDetails.ShouldTrade)
            {
                status.StrategyAction = StrategyAction.CloseTrade;
                status.StrategyState = StrategyState.ExitSubmitted;
            }

            return status;
        }
    }

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

    /// <summary>
    /// Exit strategy for position management.
    /// Implements IExitStrategy from Core.
    /// </summary>
    public class RegimeBasedExitStrategy : IExitStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private readonly SetupResult _setup;

        // Position tracking
        private decimal _highestPrice;
        private decimal _lowestPrice;
        private int _barsHeld;
        private decimal _entryPrice;
        private bool _initialized;

        // Configuration
        private readonly decimal _trailingStartMultiple = 1.0m;
        private readonly decimal _trailingAtrMultiple = 1.5m;
        private readonly int _timeStopBars = 30;

        public RegimeBasedExitStrategy(SetupResult setup)
        {
            _setup = setup;
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
        }

        public void InitializePosition(decimal entryPrice)
        {
            _entryPrice = entryPrice;
            _highestPrice = entryPrice;
            _lowestPrice = entryPrice;
            _barsHeld = 0;
            _initialized = true;
        }

        public TradeDetails GetNextExit(decimal currentPositionSize, decimal close, decimal profit)
        {
            var result = new TradeDetails { ShouldTrade = false };

            if (_quoteHub?.Quotes == null || _setup == null || currentPositionSize == 0)
                return result;

            // Auto-initialize if not done
            if (!_initialized)
            {
                _entryPrice = close;
                _highestPrice = close;
                _lowestPrice = close;
                _initialized = true;
            }

            // Update tracking
            if (close > _highestPrice) _highestPrice = close;
            if (close < _lowestPrice) _lowestPrice = close;
            _barsHeld++;

            // Check hard stop
            if (CheckStopLoss(close))
            {
                result.ShouldTrade = true;
                result.Price = _setup.StopLoss;
                result.Quantity = currentPositionSize;
                result.OrderType = "MARKET";
                return result;
            }

            // Check take profit
            if (CheckTakeProfit(close))
            {
                result.ShouldTrade = true;
                result.Price = _setup.TakeProfit;
                result.Quantity = currentPositionSize;
                result.OrderType = "MARKET";
                return result;
            }

            // Strategy-specific exit
            return _setup.RecommendedExitStrategy switch
            {
                ExitStrategyType.FixedTarget => ManageFixedTarget(close, currentPositionSize),
                ExitStrategyType.TrailingStop => ManageTrailingStop(close, currentPositionSize),
                ExitStrategyType.StructureBreak => ManageStructureBreak(close, currentPositionSize),
                ExitStrategyType.TimeBasedExit => ManageTimeBased(close, currentPositionSize),
                ExitStrategyType.ScaleOut => ManageScaleOut(close, currentPositionSize),
                _ => result
            };
        }

        private bool CheckStopLoss(decimal currentPrice)
        {
            return _setup.Direction == TradeDirection.Long
                ? currentPrice <= _setup.StopLoss
                : currentPrice >= _setup.StopLoss;
        }

        private bool CheckTakeProfit(decimal currentPrice)
        {
            return _setup.Direction == TradeDirection.Long
                ? currentPrice >= _setup.TakeProfit
                : currentPrice <= _setup.TakeProfit;
        }

        private TradeDetails ManageFixedTarget(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            // Check for momentum exhaustion near target
            decimal totalTargetDistance = Math.Abs(_setup.TakeProfit - _entryPrice);
            decimal distanceToTarget = _setup.Direction == TradeDirection.Long
                ? _setup.TakeProfit - currentPrice
                : currentPrice - _setup.TakeProfit;
            decimal progressToTarget = totalTargetDistance > 0 ? 1 - (distanceToTarget / totalTargetDistance) : 0;

            if (progressToTarget > 0.8m && IsMomentumExhausted())
            {
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }

            return result;
        }

        private TradeDetails ManageTrailingStop(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal riskDistance = Math.Abs(_entryPrice - _setup.StopLoss);
            decimal currentProfit = _setup.Direction == TradeDirection.Long
                ? currentPrice - _entryPrice
                : _entryPrice - currentPrice;

            decimal rMultiple = riskDistance > 0 ? currentProfit / riskDistance : 0;

            if (rMultiple >= _trailingStartMultiple)
            {
                decimal atr = GetCurrentAtr();
                decimal trailingDistance = atr * _trailingAtrMultiple;

                decimal trailingStopLevel = _setup.Direction == TradeDirection.Long
                    ? _highestPrice - trailingDistance
                    : _lowestPrice + trailingDistance;

                bool trailingStopHit = _setup.Direction == TradeDirection.Long
                    ? currentPrice <= trailingStopLevel
                    : currentPrice >= trailingStopLevel;

                if (trailingStopHit)
                {
                    result.ShouldTrade = true;
                    result.Price = currentPrice;
                    result.Quantity = positionSize;
                    result.OrderType = "MARKET";
                }
            }

            return result;
        }

        private TradeDetails ManageStructureBreak(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            var recentCandles = _quoteHub.Quotes.TakeLast(10).ToList();

            if (_setup.Direction == TradeDirection.Long)
            {
                var prevLow = (decimal)recentCandles.SkipLast(1).Min(c => c.Low);
                var currentLow = (decimal)recentCandles.Last().Low;

                if (currentLow < prevLow && recentCandles.Last().Close < recentCandles.Last().Open)
                {
                    result.ShouldTrade = true;
                    result.Price = currentPrice;
                    result.Quantity = positionSize;
                    result.OrderType = "MARKET";
                }
            }
            else
            {
                var prevHigh = (decimal)recentCandles.SkipLast(1).Max(c => c.High);
                var currentHigh = (decimal)recentCandles.Last().High;

                if (currentHigh > prevHigh && recentCandles.Last().Close > recentCandles.Last().Open)
                {
                    result.ShouldTrade = true;
                    result.Price = currentPrice;
                    result.Quantity = positionSize;
                    result.OrderType = "MARKET";
                }
            }

            return result;
        }

        private TradeDetails ManageTimeBased(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal pnlPercent = _entryPrice != 0
                ? (_setup.Direction == TradeDirection.Long
                    ? (currentPrice - _entryPrice) / _entryPrice * 100
                    : (_entryPrice - currentPrice) / _entryPrice * 100)
                : 0;

            bool tradeStagnant = Math.Abs(pnlPercent) < 0.5m;

            if (_barsHeld >= _timeStopBars && tradeStagnant)
            {
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }

            return result;
        }

        private TradeDetails ManageScaleOut(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal riskDistance = Math.Abs(_entryPrice - _setup.StopLoss);
            decimal currentProfit = _setup.Direction == TradeDirection.Long
                ? currentPrice - _entryPrice
                : _entryPrice - currentPrice;

            decimal rMultiple = riskDistance > 0 ? currentProfit / riskDistance : 0;

            // Scale out at R-multiples
            if (rMultiple >= 3.0m)
            {
                // Final exit
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }
            else if (rMultiple >= 2.0m && IsMomentumExhausted())
            {
                // Exit remaining on reversal
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }
            else if (rMultiple >= 1.0m)
            {
                // Partial exit at 1R (take 1/3)
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize * 0.33m;
                result.OrderType = "LIMIT";
            }

            return result;
        }

        private bool IsMomentumExhausted()
        {
            var rsi = _quoteHub.Quotes.ToRsi(14).LastOrDefault()?.Rsi ?? 50;

            if (_setup.Direction == TradeDirection.Long)
                return rsi > 70 && IsFallingRsi();
            else
                return rsi < 30 && IsRisingRsi();
        }

        private bool IsFallingRsi()
        {
            var rsiValues = _quoteHub.Quotes.ToRsi(14).TakeLast(10).ToList();
            if (rsiValues.Count < 10) return false;

            var recent = rsiValues.TakeLast(5).Average(r => r.Rsi ?? 50);
            var prior = rsiValues.Take(5).Average(r => r.Rsi ?? 50);

            return recent < prior - 5;
        }

        private bool IsRisingRsi()
        {
            var rsiValues = _quoteHub.Quotes.ToRsi(14).TakeLast(10).ToList();
            if (rsiValues.Count < 10) return false;

            var recent = rsiValues.TakeLast(5).Average(r => r.Rsi ?? 50);
            var prior = rsiValues.Take(5).Average(r => r.Rsi ?? 50);

            return recent > prior + 5;
        }

        private decimal GetCurrentAtr()
        {
            var atr = _quoteHub.Quotes.ToAtr(14).LastOrDefault()?.Atr;
            return (decimal)(atr ?? 0);
        }
    }
}