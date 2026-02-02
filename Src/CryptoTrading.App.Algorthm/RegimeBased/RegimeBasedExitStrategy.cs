using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;
using System;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
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