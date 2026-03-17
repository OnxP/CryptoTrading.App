using Binance;
using CryptoTrading.App.Core.Strategy;
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

        // Streaming indicators
        private AtrHub<IQuote> _atr;

        // Batch indicators (refreshed on each Calculate call)
        private IReadOnlyList<MacdResult> _macd;
        private IReadOnlyList<BollingerBandsResult> _bollingerBands;
        private IReadOnlyList<RsiResult> _rsi;

        // Leverage probability calculator
        private readonly LeverageProbabilityCalculator _leverageCalculator = new LeverageProbabilityCalculator();

        // Configuration
        private readonly decimal _minRiskRewardRatio = 1.5m;
        private readonly int _macdFast = 12;
        private readonly int _macdSlow = 26;
        private readonly int _macdSignal = 9;
        private readonly int _bbPeriod = 20;
        private readonly int _bbStdDev = 2;
        private readonly int _rsiPeriod = 14;
        private readonly decimal _rsiOversold = 35m;
        private readonly decimal _rsiOverbought = 65m;
        private readonly decimal _zoneProximityMultiple = 2m; // 2x ATR

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
                return (new StrategyResult { PostTrade = false }, null);
            }

            // Find the best valid setup from the allowed list
            SetupResult bestSetup = null;
            foreach (var setupType in regimeResult.AllowedSetups)
            {
                var setup = EvaluateSetup(setupType, regimeResult);
                if (setup != null && setup.IsValid)
                {
                    if (bestSetup == null || setup.RiskRewardRatio > bestSetup.RiskRewardRatio)
                        bestSetup = setup;
                }
            }

            if (bestSetup == null || !bestSetup.IsValid)
                return (new StrategyResult { PostTrade = false }, null);

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

            // Create result and execution strategy
            var strategyResult = new RegimeBasedStrategyResult
            {
                PostTrade = true,
                Amount = 0.1m,
                Leverage = leverageRec.ActualLeverage,
                OrderSide = bestSetup.Direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell,
                Setup = bestSetup,
                LeverageRecommendation = leverageRec
            };

            var executionStrategy = new RegimeBasedExecutionStrategy(_quoteHub, bestSetup);

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

            if (!HasSufficientData() || _macd == null || _macd.Count < 2) return result;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);
            if (atr == 0) return result;

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

            if (direction == TradeDirection.Long)
            {
                // Bullish signal: histogram crosses above zero OR MACD crosses above signal
                bool histogramCross = prevHistogram <= 0 && currHistogram > 0;
                bool macdCross = prevMacdLine <= prevSignalLine && currMacdLine > currSignalLine;
                macdSignal = histogramCross || macdCross;

                var swingLow = (decimal)_quoteHub.Quotes.TakeLast(20).Min(q => q.Low);
                var structureHigh = (decimal)_quoteHub.Quotes.TakeLast(50).Max(q => q.High);

                stop = swingLow - (0.1m * atr);
                target = structureHigh > currentPrice ? structureHigh : currentPrice + (3m * atr);
            }
            else
            {
                // Bearish signal: histogram crosses below zero OR MACD crosses below signal
                bool histogramCross = prevHistogram >= 0 && currHistogram < 0;
                bool macdCross = prevMacdLine >= prevSignalLine && currMacdLine < currSignalLine;
                macdSignal = histogramCross || macdCross;

                var swingHigh = (decimal)_quoteHub.Quotes.TakeLast(20).Max(q => q.High);
                var structureLow = (decimal)_quoteHub.Quotes.TakeLast(50).Min(q => q.Low);

                stop = swingHigh + (0.1m * atr);
                target = structureLow < currentPrice ? structureLow : currentPrice - (3m * atr);
            }

            if (!macdSignal) return result;

            // Calculate risk/reward
            decimal risk = Math.Abs(currentPrice - stop);
            decimal reward = Math.Abs(target - currentPrice);
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            if (result.RiskRewardRatio < _minRiskRewardRatio) return result;

            // Check zone proximity: within 2×ATR of a relevant zone?
            var zoneType = direction == TradeDirection.Long ? ZoneType.Demand : ZoneType.Supply;
            var nearestZone = FindNearestZone(currentPrice, regimeResult.ActiveZones, zoneType);
            bool isZoneTrade = nearestZone != null && nearestZone.DistanceTo(currentPrice) <= atr * _zoneProximityMultiple;

            // Populate result
            result.IsValid = true;
            result.StopLoss = stop;
            result.TakeProfit = target;
            result.Confidence = CalculateMacdConfidence(currHistogram, prevHistogram, isZoneTrade, result.RiskRewardRatio);

            if (isZoneTrade)
            {
                result.NearestZone = nearestZone;
                result.IsZoneTrade = true;
                result.RecommendedEntryStrategy = EntryStrategyType.LimitAtZoneEdge;
                result.EntryZoneHigh = nearestZone.High;
                result.EntryZoneLow = nearestZone.Low;
            }
            else
            {
                result.IsZoneTrade = false;
                result.RecommendedEntryStrategy = EntryStrategyType.StochRsiEntry;
                result.EntryZoneHigh = currentPrice + (0.5m * atr);
                result.EntryZoneLow = currentPrice - (0.5m * atr);
            }

            result.RecommendedExitStrategy = result.RiskRewardRatio > 2.5m
                ? ExitStrategyType.ScaleOut
                : ExitStrategyType.TrailingStop;

            result.Reasoning = $"MACD Hist:{currHistogram:F4} Zone:{isZoneTrade} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        /// <summary>
        /// Bollinger Band mean reversion for ranging markets with high/normal volatility.
        ///
        /// Long: Price at lower band + RSI oversold (&lt;35) → buy toward middle band.
        /// Short: Price at upper band + RSI overbought (&gt;65) → sell toward middle band.
        /// Entry: StochRsiEntry (momentum confirmation) or LimitAtZoneEdge if near S/D zone.
        /// Target: Bollinger middle band.
        /// Stop: recent swing extreme ± ATR buffer.
        /// </summary>
        private SetupResult EvaluateBbMeanReversion(TradeDirection direction, RegimeBasedMarketStructureResult regimeResult)
        {
            var result = new SetupResult
            {
                SetupType = direction == TradeDirection.Long ? SetupType.BbMeanRevLong : SetupType.BbMeanRevShort,
                Direction = direction
            };

            if (!HasSufficientData() || _bollingerBands == null || _rsi == null) return result;

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

            if (direction == TradeDirection.Long)
            {
                // Price at lower band + RSI oversold
                bool atLowerBand = currentPrice <= bbLower * 1.005m;
                bool oversold = rsi < _rsiOversold;
                bbSignal = atLowerBand && oversold;

                var recentLow = (decimal)_quoteHub.Quotes.TakeLast(10).Min(q => q.Low);
                stop = recentLow - (0.3m * atr);
                target = bbMiddle;
            }
            else
            {
                // Price at upper band + RSI overbought
                bool atUpperBand = currentPrice >= bbUpper * 0.995m;
                bool overbought = rsi > _rsiOverbought;
                bbSignal = atUpperBand && overbought;

                var recentHigh = (decimal)_quoteHub.Quotes.TakeLast(10).Max(q => q.High);
                stop = recentHigh + (0.3m * atr);
                target = bbMiddle;
            }

            if (!bbSignal) return result;

            // Calculate risk/reward
            decimal risk = Math.Abs(currentPrice - stop);
            decimal reward = Math.Abs(target - currentPrice);
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            if (result.RiskRewardRatio < _minRiskRewardRatio) return result;

            // Check zone proximity
            var zoneType = direction == TradeDirection.Long ? ZoneType.Demand : ZoneType.Supply;
            var nearestZone = FindNearestZone(currentPrice, regimeResult.ActiveZones, zoneType);
            bool isZoneTrade = nearestZone != null && nearestZone.DistanceTo(currentPrice) <= atr * _zoneProximityMultiple;

            // Populate result
            result.IsValid = true;
            result.StopLoss = stop;
            result.TakeProfit = target;
            result.Confidence = CalculateBbConfidence(rsi, direction, isZoneTrade, result.RiskRewardRatio);

            if (isZoneTrade)
            {
                result.NearestZone = nearestZone;
                result.IsZoneTrade = true;
                result.RecommendedEntryStrategy = EntryStrategyType.LimitAtZoneEdge;
                result.EntryZoneHigh = nearestZone.High;
                result.EntryZoneLow = nearestZone.Low;
            }
            else
            {
                result.IsZoneTrade = false;
                result.RecommendedEntryStrategy = EntryStrategyType.StochRsiEntry;
                result.EntryZoneHigh = direction == TradeDirection.Long ? bbLower * 1.01m : bbUpper * 1.01m;
                result.EntryZoneLow = direction == TradeDirection.Long ? bbLower * 0.99m : bbUpper * 0.99m;
            }

            result.RecommendedExitStrategy = ExitStrategyType.FixedTarget;
            result.Reasoning = $"BB RSI:{rsi:F1} Zone:{isZoneTrade} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        #endregion

        #region Helpers

        private bool HasSufficientData() =>
            _quoteHub?.Quotes != null && _quoteHub.Quotes.Count >= 50;

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

            // Good R:R adds confidence
            if (rr > 2m)
                conf += 0.15m;
            else if (rr > 2.5m)
                conf += 0.25m;

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
