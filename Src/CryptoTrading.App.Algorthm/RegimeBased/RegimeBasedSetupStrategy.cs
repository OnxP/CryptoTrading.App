using Binance;
using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// 15-minute timeframe setup detection strategy.
    /// Implements IStrategy from Core.
    ///
    /// Two evaluation paths based on the 4H regime:
    /// 1. MACD Trend (Bull/Bear regime) - MACD crossover/histogram signals for trending entries
    /// 2. Bollinger Band Mean Reversion (Ranging + High/Normal vol) - BBands + RSI for mean reversion
    ///
    /// No-trade path: Ranging + Low Vol → empty AllowedSetups → returns PostTrade = false immediately.
    ///
    /// Zone proximity: if price is within 2×ATR of a supply/demand zone from the 4H layer,
    /// the entry strategy is set to LimitAtZoneEdge; otherwise StochRsiEntry is used.
    /// </summary>
    public class RegimeBasedSetupStrategy : IStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private ILogger _logger;

        public void SetLogger(ILogger logger) => _logger = logger;

        // Streaming indicators
        private AtrHub<IQuote> _atr;

        // Batch indicators (refreshed on each Calculate call)
        private IReadOnlyList<MacdResult> _macd;
        private IReadOnlyList<BollingerBandsResult> _bollingerBands;
        private IReadOnlyList<RsiResult> _rsi;

        // Leverage probability calculator
        private readonly LeverageProbabilityCalculator _leverageCalculator = new LeverageProbabilityCalculator();

        // Performance tracker for anti-martingale sizing and circuit breaker
        public TradePerformanceTracker PerformanceTracker { get; set; }

        // Account equity for position sizing (set by the algorithm layer)
        public decimal Equity { get; set; } = 10000m;

        // Symbol-specific order constraints (min qty, step size, min notional)
        // Set by the algorithm layer from the exchange symbol info.
        public SymbolConstraints SymbolConstraints { get; set; }
        // Configuration
        private readonly decimal _minRiskRewardRatio = 1.5m;
        private readonly int _macdFast = 12;
        private readonly int _macdSlow = 26;
        private readonly int _macdSignal = 9;
        private readonly int _bbPeriod = 20;
        private readonly int _bbStdDev = 2;
        private readonly int _rsiPeriod = 14;
        private readonly decimal _rsiOversold = 25m;      // Tightened from 35 → 25 for higher-probability reversals
        private readonly decimal _rsiOverbought = 75m;     // Tightened from 65 → 75 for higher-probability reversals
        private readonly decimal _zoneProximityMultiple = 2m; // 2x ATR

        // Minimum SL distance as percentage of entry price — prevents stops too tight in narrow ranges
        private readonly decimal _minStopDistancePct = 0.005m; // 0.5% minimum SL distance
        // MACD histogram must be at least this fraction of ATR to be considered a valid signal
        private readonly decimal _minHistogramStrength = 0.1m; // histogram > 10% of ATR

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
            _atr = _quoteHub.ToAtr(14);
            RefreshIndicators();
        }

        private void RefreshIndicators()
        {
            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 50) return;
            _macd = _quoteHub.Quotes.ToMacd(_macdFast, _macdSlow, _macdSignal);
            _bollingerBands = _quoteHub.Quotes.ToBollingerBands(_bbPeriod, _bbStdDev);
            _rsi = _quoteHub.Quotes.ToRsi(_rsiPeriod);
        }

        public (IStrategyResult, IExecutionStrategy) Calculate(IMarketStructureResult marketStructure)
        {
            RefreshIndicators();

            // Cast to our extended result
            var regimeResult = marketStructure as RegimeBasedMarketStructureResult;
            if (regimeResult == null || !regimeResult.AllowedSetups.Any() ||
                regimeResult.AllowedDirection == AllowedDirection.None)
            {
                // No-trade path: ranging + low vol, or no valid regime
                _logger?.LogDebug($"[15M SKIP] No allowed setups. Regime: {regimeResult?.MarketRegime} | Vol: {regimeResult?.VolatilityRegime} | Dir: {regimeResult?.AllowedDirection}");
                return (new StrategyResult { PostTrade = false }, null);
            }

            // Circuit breaker: stop trading when drawdown exceeds daily or peak limits
            if (PerformanceTracker != null && PerformanceTracker.IsCircuitBreakerActive())
            {
                _logger?.LogWarning($"[15M SKIP] Circuit breaker ACTIVE — {PerformanceTracker.GetStatus()}");
                return (new StrategyResult { PostTrade = false }, null);
            }

            // Find the best valid setup from the allowed list
            SetupResult bestSetup = null;
            foreach (var setupType in regimeResult.AllowedSetups)
            {
                var setup = EvaluateSetup(setupType, regimeResult);
                if (setup != null && setup.IsValid)
                {
                    _logger?.LogDebug($"[15M EVAL] {setupType} VALID R:R={setup.RiskRewardRatio:F2}");
                    if (bestSetup == null || setup.RiskRewardRatio > bestSetup.RiskRewardRatio)
                        bestSetup = setup;
                }
                else
                {
                    _logger?.LogDebug($"[15M EVAL] {setupType} REJECTED - {setup?.Reasoning ?? "no signal"}");
                }
            }

            if (bestSetup == null || !bestSetup.IsValid)
            {
                _logger?.LogDebug($"[15M SKIP] All setups invalid. Evaluated: {string.Join(", ", regimeResult.AllowedSetups)}");
                return (new StrategyResult { PostTrade = false }, null);
            }

            // Calculate leverage probability score
            var leverageRec = _leverageCalculator.Calculate(
                regimeConfidence: regimeResult.Confidence,
                setupConfidence: bestSetup.Confidence,
                volatilityRegime: regimeResult.VolatilityRegime,
                atrPercentile: regimeResult.AtrPercentile,
                isZoneTrade: bestSetup.IsZoneTrade,
                riskRewardRatio: bestSetup.RiskRewardRatio);

            // Skip trade if composite score is below minimum threshold
            if (leverageRec.ActualLeverage == 0)
                return (new StrategyResult { PostTrade = false }, null);

            // Position sizing: volatility-adjusted based on SL distance + regime + streak
            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var effectiveEntry = bestSetup.IsZoneTrade
                ? (bestSetup.Direction == TradeDirection.Long ? bestSetup.EntryZoneHigh : bestSetup.EntryZoneLow)
                : currentPrice;

            decimal streakMultiplier = PerformanceTracker?.GetSizeMultiplier() ?? 1.0m;
            decimal regimeMultiplier = PositionSizer.GetRegimeMultiplier(
                regimeResult.MarketRegime,
                regimeResult.VolatilityRegime,
                regimeResult.Confidence,
                regimeResult.TrendStrength);

            decimal positionSize = PositionSizer.Calculate(
                equity: Equity,
                entryPrice: effectiveEntry,
                stopLoss: bestSetup.StopLoss,
                symbolConstraints: SymbolConstraints,
                streakMultiplier: streakMultiplier,
                regimeMultiplier: regimeMultiplier);

            _logger?.LogInformation(
                $"[15M SETUP] {bestSetup.SetupType} entry:{effectiveEntry:F2} SL:{bestSetup.StopLoss:F2} TP:{bestSetup.TakeProfit:F2} " +
                $"R:R:{bestSetup.RiskRewardRatio:F2} size:{positionSize:F5} streak:{streakMultiplier:F2} regime:{regimeMultiplier:F2}");

            // Create result and execution strategy
            var strategyResult = new RegimeBasedStrategyResult
            {
                PostTrade = true,
                Amount = positionSize,
                Leverage = leverageRec.ActualLeverage,
                OrderSide = bestSetup.Direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell,
                Setup = bestSetup,
                LeverageRecommendation = leverageRec
            };

            var executionStrategy = new RegimeBasedExecutionStrategy(_quoteHub, bestSetup);
            executionStrategy.Quantity = positionSize;

            return (strategyResult, executionStrategy);
        }

        #region Setup Evaluation

        private SetupResult EvaluateSetup(SetupType setupType, RegimeBasedMarketStructureResult regimeResult)
        {
            return setupType switch
            {
                SetupType.MacdTrendLong => EvaluateMacdTrend(TradeDirection.Long, regimeResult),
                SetupType.MacdTrendShort => EvaluateMacdTrend(TradeDirection.Short, regimeResult),
                SetupType.BbMeanRevLong => EvaluateBbMeanReversion(TradeDirection.Long, regimeResult),
                SetupType.BbMeanRevShort => EvaluateBbMeanReversion(TradeDirection.Short, regimeResult),
                _ => null
            };
        }

        /// <summary>
        /// MACD-based setup for trending markets (Bull/Bear regime).
        ///
        /// Signal: MACD histogram crosses zero OR MACD line crosses signal line.
        /// Zone check: if price within 2×ATR of a demand (long) or supply (short) zone
        ///   → entry = LimitAtZoneEdge, otherwise StochRsiEntry.
        /// Stop: below 20-bar swing low (long) or above swing high (short).
        /// Target: 50-bar structure swing or 3×ATR fallback.
        /// Minimum R:R ≥ 1.5.
        /// </summary>
        private SetupResult EvaluateMacdTrend(TradeDirection direction, RegimeBasedMarketStructureResult regimeResult)
        {
            var result = new SetupResult
            {
                SetupType = direction == TradeDirection.Long ? SetupType.MacdTrendLong : SetupType.MacdTrendShort,
                Direction = direction
            };

            if (!HasSufficientData() || _macd == null || _macd.Count < 2)
            {
                result.Reasoning = "insufficient data";
                return result;
            }

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);
            if (atr == 0)
            {
                result.Reasoning = "ATR=0";
                return result;
            }

            var currentMacd = _macd[_macd.Count - 1];
            var previousMacd = _macd[_macd.Count - 2];

            if (currentMacd.Macd == null || currentMacd.Signal == null ||
                currentMacd.Histogram == null || previousMacd.Histogram == null)
                return result;

            decimal currHistogram = (decimal)currentMacd.Histogram.Value;
            decimal prevHistogram = (decimal)previousMacd.Histogram.Value;
            decimal currMacdLine = (decimal)currentMacd.Macd.Value;
            decimal currSignalLine = (decimal)currentMacd.Signal.Value;
            decimal prevMacdLine = (decimal)(previousMacd.Macd ?? 0);
            decimal prevSignalLine = (decimal)(previousMacd.Signal ?? 0);

            bool macdSignal;
            decimal stop, target;

            // Volume confirmation: current volume must be above average to validate signal
            var volumes = _quoteHub.Quotes.TakeLast(20).Select(q => (decimal)q.Volume).ToList();
            var avgVolume = volumes.Count > 0 ? volumes.Average() : 0;
            var currentVolume = volumes.Count > 0 ? volumes.Last() : 0;
            bool volumeConfirmed = avgVolume > 0 && currentVolume >= avgVolume * 0.8m; // At least 80% of avg

            if (direction == TradeDirection.Long)
            {
                // Bullish signal: histogram crosses above zero OR MACD crosses above signal
                bool histogramCross = prevHistogram <= 0 && currHistogram > 0;
                bool macdCross = prevMacdLine <= prevSignalLine && currMacdLine > currSignalLine;
                // Histogram strength filter: must be meaningful relative to ATR (not just barely > 0)
                bool histogramStrong = Math.Abs(currHistogram) > atr * _minHistogramStrength;
                macdSignal = (histogramCross || macdCross) && histogramStrong;

                var swingLow = (decimal)_quoteHub.Quotes.TakeLast(30).Min(q => q.Low); // Widened from 20 to 30 bars
                var structureHigh = (decimal)_quoteHub.Quotes.TakeLast(50).Max(q => q.High);

                stop = swingLow - (0.3m * atr); // Widened buffer from 0.1 to 0.3 ATR
                target = structureHigh > currentPrice ? structureHigh : currentPrice + (3m * atr);
            }
            else
            {
                // Bearish signal: histogram crosses below zero OR MACD crosses below signal
                bool histogramCross = prevHistogram >= 0 && currHistogram < 0;
                bool macdCross = prevMacdLine >= prevSignalLine && currMacdLine < currSignalLine;
                bool histogramStrong = Math.Abs(currHistogram) > atr * _minHistogramStrength;
                macdSignal = (histogramCross || macdCross) && histogramStrong;

                var swingHigh = (decimal)_quoteHub.Quotes.TakeLast(30).Max(q => q.High); // Widened from 20 to 30 bars
                var structureLow = (decimal)_quoteHub.Quotes.TakeLast(50).Min(q => q.Low);

                stop = swingHigh + (0.3m * atr); // Widened buffer from 0.1 to 0.3 ATR
                target = structureLow < currentPrice ? structureLow : currentPrice - (3m * atr);
            }

            if (!macdSignal)
            {
                result.Reasoning = $"no MACD cross (hist:{currHistogram:F4} prev:{prevHistogram:F4} macd:{currMacdLine:F4} sig:{currSignalLine:F4} histStrong:{Math.Abs(currHistogram) > atr * _minHistogramStrength})";
                return result;
            }

            if (!volumeConfirmed)
            {
                result.Reasoning = $"volume too low ({currentVolume:F0} < {avgVolume * 0.8m:F0} = 80% avg)";
                return result;
            }

            // Enforce minimum SL distance to prevent tight stops in narrow ranges
            var minStopDistance = currentPrice * _minStopDistancePct;
            if (direction == TradeDirection.Long)
                stop = Math.Min(stop, currentPrice - minStopDistance);
            else
                stop = Math.Max(stop, currentPrice + minStopDistance);

            // Check zone proximity: within 2×ATR of a relevant zone?
            var zoneType = direction == TradeDirection.Long ? ZoneType.Demand : ZoneType.Supply;
            var nearestZone = FindNearestZone(currentPrice, regimeResult.ActiveZones, zoneType);
            bool isZoneTrade = nearestZone != null && nearestZone.DistanceTo(currentPrice) <= atr * _zoneProximityMultiple;

            if (isZoneTrade)
            {
                result.NearestZone = nearestZone;
                result.IsZoneTrade = true;
                result.RecommendedEntryStrategy = EntryStrategyType.LimitAtZoneEdge;
                result.EntryZoneHigh = nearestZone.High;
                result.EntryZoneLow = nearestZone.Low;

                // Recalculate SL/TP relative to the zone edge where entry will actually happen.
                // Long enters at zone.High, Short enters at zone.Low.
                var entryPrice = direction == TradeDirection.Long ? nearestZone.High : nearestZone.Low;
                stop = RecalculateStopForZone(direction, entryPrice, nearestZone, atr);
                target = RecalculateTargetForZone(direction, entryPrice, target, atr);
            }
            else
            {
                result.IsZoneTrade = false;
                result.RecommendedEntryStrategy = EntryStrategyType.StochRsiEntry;
                result.EntryZoneHigh = currentPrice + (0.5m * atr);
                result.EntryZoneLow = currentPrice - (0.5m * atr);
            }

            // Calculate risk/reward from the actual entry point
            var effectiveEntry = isZoneTrade
                ? (direction == TradeDirection.Long ? nearestZone.High : nearestZone.Low)
                : currentPrice;
            decimal risk = Math.Abs(effectiveEntry - stop);
            decimal reward = Math.Abs(target - effectiveEntry);
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            if (result.RiskRewardRatio < _minRiskRewardRatio)
            {
                result.Reasoning = $"R:R too low ({result.RiskRewardRatio:F2} < {_minRiskRewardRatio}) entry:{effectiveEntry:F2} stop:{stop:F2} target:{target:F2}";
                return result;
            }

            // Populate result
            result.IsValid = true;
            result.StopLoss = stop;
            result.TakeProfit = target;
            result.Confidence = CalculateMacdConfidence(currHistogram, prevHistogram, isZoneTrade, result.RiskRewardRatio);

            result.RecommendedExitStrategy = result.RiskRewardRatio > 2.5m
                ? ExitStrategyType.ScaleOut
                : ExitStrategyType.TrailingStop;

            result.Reasoning = $"MACD Hist:{currHistogram:F4} Zone:{isZoneTrade} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        /// <summary>
        /// Bollinger Band mean reversion for ranging markets with high/normal volatility.
        ///
        /// Long: Price at lower band + RSI oversold (&lt;25) → buy toward middle band.
        /// Short: Price at upper band + RSI overbought (&gt;75) → sell toward middle band.
        /// Entry: StochRsiEntry (momentum confirmation) or LimitAtZoneEdge if near S/D zone.
        /// Target: Bollinger middle band.
        /// Stop: recent swing extreme ± ATR buffer.
        ///
        /// Trend filter: uses the 4H EMA gradient to avoid counter-trend BB trades.
        /// If 4H gradient is bearish, only SHORT BB setups are allowed (no buying dips in a downtrend).
        /// If 4H gradient is bullish, only LONG BB setups are allowed (no shorting rallies in an uptrend).
        /// </summary>
        private SetupResult EvaluateBbMeanReversion(TradeDirection direction, RegimeBasedMarketStructureResult regimeResult)
        {
            var result = new SetupResult
            {
                SetupType = direction == TradeDirection.Long ? SetupType.BbMeanRevLong : SetupType.BbMeanRevShort,
                Direction = direction
            };

            if (!HasSufficientData() || _bollingerBands == null || _rsi == null)
            {
                result.Reasoning = "insufficient data for BB";
                return result;
            }

            // Trend filter: use 4H EMA gradient to block counter-trend BB trades.
            // Negative gradient (bearish macro) → block LONG BB (no buying dips in a downtrend)
            // Positive gradient (bullish macro) → block SHORT BB (no shorting rallies in an uptrend)
            // Threshold of 0.02 allows trades when gradient is near-zero (truly ranging)
            if (regimeResult.EmaGradientNormalized < -0.02m && direction == TradeDirection.Long)
            {
                result.Reasoning = $"BB Long blocked by bearish 4H trend (grad:{regimeResult.EmaGradientNormalized:F3})";
                return result;
            }
            if (regimeResult.EmaGradientNormalized > 0.02m && direction == TradeDirection.Short)
            {
                result.Reasoning = $"BB Short blocked by bullish 4H trend (grad:{regimeResult.EmaGradientNormalized:F3})";
                return result;
            }

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var bb = _bollingerBands.LastOrDefault();
            var rsi = (decimal)(_rsi.LastOrDefault()?.Rsi ?? 50);
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);

            if (bb == null || atr == 0) return result;

            var bbLower = (decimal)(bb.LowerBand ?? 0);
            var bbUpper = (decimal)(bb.UpperBand ?? 0);
            var bbMiddle = (decimal)(bb.Sma ?? 0);

            bool bbSignal;
            decimal stop, target;

            // Volume confirmation for BB mean reversion
            var volumes = _quoteHub.Quotes.TakeLast(20).Select(q => (decimal)q.Volume).ToList();
            var avgVolume = volumes.Count > 0 ? volumes.Average() : 0;
            var currentVolume = volumes.Count > 0 ? volumes.Last() : 0;
            bool volumeConfirmed = avgVolume > 0 && currentVolume >= avgVolume * 0.8m;

            if (direction == TradeDirection.Long)
            {
                // Price at lower band (widened tolerance to 1%) + RSI oversold (now 25)
                bool atLowerBand = currentPrice <= bbLower * 1.01m;
                bool oversold = rsi < _rsiOversold;
                bbSignal = atLowerBand && oversold;

                var recentLow = (decimal)_quoteHub.Quotes.TakeLast(15).Min(q => q.Low); // Widened from 10 to 15
                stop = recentLow - (0.5m * atr); // Wider buffer: 0.3 → 0.5 ATR
                target = bbMiddle;
            }
            else
            {
                // Price at upper band (widened tolerance) + RSI overbought (now 75)
                bool atUpperBand = currentPrice >= bbUpper * 0.99m;
                bool overbought = rsi > _rsiOverbought;
                bbSignal = atUpperBand && overbought;

                var recentHigh = (decimal)_quoteHub.Quotes.TakeLast(15).Max(q => q.High); // Widened from 10 to 15
                stop = recentHigh + (0.5m * atr); // Wider buffer: 0.3 → 0.5 ATR
                target = bbMiddle;
            }

            if (!bbSignal)
            {
                result.Reasoning = $"no BB signal (price:{currentPrice:F2} bbLow:{bbLower:F2} bbUp:{bbUpper:F2} rsi:{rsi:F1})";
                return result;
            }

            if (!volumeConfirmed)
            {
                result.Reasoning = $"volume too low for BB ({currentVolume:F0} < {avgVolume * 0.8m:F0})";
                return result;
            }

            // Enforce minimum SL distance
            var minStopDistance = currentPrice * _minStopDistancePct;
            if (direction == TradeDirection.Long)
                stop = Math.Min(stop, currentPrice - minStopDistance);
            else
                stop = Math.Max(stop, currentPrice + minStopDistance);

            // Check zone proximity
            var zoneType = direction == TradeDirection.Long ? ZoneType.Demand : ZoneType.Supply;
            var nearestZone = FindNearestZone(currentPrice, regimeResult.ActiveZones, zoneType);
            bool isZoneTrade = nearestZone != null && nearestZone.DistanceTo(currentPrice) <= atr * _zoneProximityMultiple;

            if (isZoneTrade)
            {
                result.NearestZone = nearestZone;
                result.IsZoneTrade = true;
                result.RecommendedEntryStrategy = EntryStrategyType.LimitAtZoneEdge;
                result.EntryZoneHigh = nearestZone.High;
                result.EntryZoneLow = nearestZone.Low;

                // Recalculate SL/TP relative to the zone edge where entry will actually happen.
                var entryPrice = direction == TradeDirection.Long ? nearestZone.High : nearestZone.Low;
                stop = RecalculateStopForZone(direction, entryPrice, nearestZone, atr);
                target = RecalculateTargetForZone(direction, entryPrice, target, atr);
            }
            else
            {
                result.IsZoneTrade = false;
                result.RecommendedEntryStrategy = EntryStrategyType.StochRsiEntry;
                result.EntryZoneHigh = direction == TradeDirection.Long ? bbLower * 1.01m : bbUpper * 1.01m;
                result.EntryZoneLow = direction == TradeDirection.Long ? bbLower * 0.99m : bbUpper * 0.99m;
            }

            // Calculate risk/reward from the actual entry point
            var effectiveEntry = isZoneTrade
                ? (direction == TradeDirection.Long ? nearestZone.High : nearestZone.Low)
                : currentPrice;
            decimal risk = Math.Abs(effectiveEntry - stop);
            decimal reward = Math.Abs(target - effectiveEntry);
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            if (result.RiskRewardRatio < _minRiskRewardRatio)
            {
                result.Reasoning = $"R:R too low ({result.RiskRewardRatio:F2} < {_minRiskRewardRatio}) entry:{effectiveEntry:F2} stop:{stop:F2} target:{target:F2}";
                return result;
            }

            // Populate result
            result.IsValid = true;
            result.StopLoss = stop;
            result.TakeProfit = target;
            result.Confidence = CalculateBbConfidence(rsi, direction, isZoneTrade, result.RiskRewardRatio);

            result.RecommendedExitStrategy = ExitStrategyType.FixedTarget;
            result.Reasoning = $"BB RSI:{rsi:F1} Zone:{isZoneTrade} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        #endregion

        #region Helpers

        private bool HasSufficientData() =>
            _quoteHub?.Quotes != null && _quoteHub.Quotes.Count >= 50;

        /// <summary>
        /// Recalculate the stop loss relative to the zone entry price.
        /// For zone trades, the SL must be on the correct side of the zone edge
        /// where the limit order will actually fill.
        ///
        /// Long (entry at zone.High):  SL = zone.Low - 0.5×ATR (below the demand zone)
        /// Short (entry at zone.Low):  SL = zone.High + 0.5×ATR (above the supply zone)
        /// </summary>
        private decimal RecalculateStopForZone(TradeDirection direction, decimal entryPrice, SupplyDemandZone zone, decimal atr)
        {
            if (direction == TradeDirection.Long)
            {
                // SL below the demand zone
                var zoneSl = zone.Low - (0.5m * atr);
                // Ensure SL is always below entry
                return Math.Min(zoneSl, entryPrice - (0.3m * atr));
            }
            else
            {
                // SL above the supply zone
                var zoneSl = zone.High + (0.5m * atr);
                // Ensure SL is always above entry
                return Math.Max(zoneSl, entryPrice + (0.3m * atr));
            }
        }

        /// <summary>
        /// Ensure the target is on the correct side of the entry and represents
        /// at least a minimum move (0.5% from entry to cover fees + min profit).
        /// </summary>
        private decimal RecalculateTargetForZone(TradeDirection direction, decimal entryPrice, decimal originalTarget, decimal atr)
        {
            var minMove = entryPrice * 0.010m; // 1.0% minimum (fees + meaningful profit)
            if (direction == TradeDirection.Long)
            {
                var minTarget = entryPrice + minMove;
                // Use the larger of original target and minimum target
                return Math.Max(originalTarget, minTarget);
            }
            else
            {
                var minTarget = entryPrice - minMove;
                // Use the smaller of original target and minimum target
                return Math.Min(originalTarget, minTarget);
            }
        }

        /// <summary>
        /// Find the nearest zone of a given type from the 4H active zones.
        /// </summary>
        private SupplyDemandZone FindNearestZone(decimal price, List<SupplyDemandZone> zones, ZoneType type)
        {
            if (zones == null || !zones.Any()) return null;

            return zones
                .Where(z => z.Type == type)
                .OrderBy(z => z.DistanceTo(price))
                .FirstOrDefault();
        }

        private decimal CalculateMacdConfidence(decimal currHistogram, decimal prevHistogram, bool isZoneTrade, decimal rr)
        {
            decimal conf = 0.35m;

            // Stronger histogram momentum = more confidence
            if (Math.Abs(currHistogram) > Math.Abs(prevHistogram))
                conf += 0.15m;

            // Zone confluence adds confidence
            if (isZoneTrade)
                conf += 0.2m;

            // Good R:R adds confidence (check higher threshold first)
            if (rr > 2.5m)
                conf += 0.25m;
            else if (rr > 2m)
                conf += 0.15m;

            return Math.Clamp(conf, 0, 1);
        }

        private decimal CalculateBbConfidence(decimal rsi, TradeDirection direction, bool isZoneTrade, decimal rr)
        {
            decimal conf = 0.3m;

            // More extreme RSI = higher confidence
            if (direction == TradeDirection.Long && rsi < 25)
                conf += 0.2m;
            else if (direction == TradeDirection.Short && rsi > 75)
                conf += 0.2m;
            else
                conf += 0.1m;

            // Zone confluence
            if (isZoneTrade)
                conf += 0.2m;

            // R:R quality
            if (rr > 2m)
                conf += 0.15m;

            return Math.Clamp(conf, 0, 1);
        }

        #endregion
    }
}
