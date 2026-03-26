using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// 4H timeframe regime detection using EMA gradient analysis.
    /// Implements IMarketStructureStrategy from Core.
    ///
    /// Uses a fast EMA (configurable, default 5-period) with gradient calculation
    /// to classify the market regime. A horizontal gradient indicates a ranging market,
    /// a strong positive/negative gradient indicates trending markets.
    ///
    /// Also detects supply/demand zones for downstream 15M/1M use and
    /// classifies volatility using ATR percentile.
    ///
    /// The EMA gradient approach is deliberately reactive to catch regime changes
    /// within 1-2 candles, preventing losses during trend reversals.
    /// </summary>
    public class RegimeBasedMarketStructureStrategy : IMarketStructureStrategy
    {
        private QuoteHub<IQuote> _quoteHub;

        // Streaming indicator hubs
        private EmaHub<IQuote> _emaGradient;
        private AtrHub<IQuote> _atr;

        // Supply/demand zone detector
        private SupplyDemandZoneDetector _zoneDetector;

        // Regime confirmation: require consecutive bars in the same direction to prevent whipsaw
        private MarketRegime _previousRegime = MarketRegime.RangingMarket;
        private MarketRegime _pendingRegime = MarketRegime.RangingMarket;
        private int _pendingRegimeCount = 0;
        private readonly int _regimeConfirmationBars = 2; // Require 2 consecutive bars of new regime

        // Configuration
        private readonly int _emaGradientPeriod;
        private readonly int _gradientLookback;
        private readonly decimal _trendThreshold;
        private readonly decimal _volatilityHighPercentile = 75;
        private readonly decimal _volatilityLowPercentile = 25;

        public RegimeBasedMarketStructureStrategy(
            int emaGradientPeriod = 5,
            int gradientLookback = 3,
            decimal trendThreshold = 0.05m)
        {
            _emaGradientPeriod = emaGradientPeriod;
            _gradientLookback = gradientLookback;
            _trendThreshold = trendThreshold;
            _zoneDetector = new SupplyDemandZoneDetector();
        }

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
            _emaGradient = _quoteHub.ToEma(_emaGradientPeriod);
            _atr = _quoteHub.ToAtr(14);
        }

        public IMarketStructureResult Calculate()
        {
            var result = new RegimeBasedMarketStructureResult();

            if (_quoteHub == null || _quoteHub.Quotes.Count < 50)
            {
                result.MarketRegime = MarketRegime.RangingMarket;
                result.Confidence = 0;
                result.Reasoning = "Insufficient data";
                return result;
            }

            // 1. Calculate EMA gradient
            var gradient = CalculateEmaGradient();

            // 2. Classify regime from gradient
            result.MarketRegime = ClassifyRegime(gradient.Normalized);

            // 3. Analyze volatility (ATR percentile)
            var volatility = AnalyzeVolatility();
            result.VolatilityRegime = volatility.Regime;

            // 4. Detect supply/demand zones
            result.ActiveZones = _zoneDetector.Detect(_quoteHub.Quotes);

            // 5. Set allowed direction and setups based on regime + volatility
            result.AllowedDirection = GetAllowedDirection(result.MarketRegime, result.VolatilityRegime);
            result.AllowedSetups = GetAllowedSetups(result.MarketRegime, result.VolatilityRegime);

            // 6. EMA gradient data for downstream strategies
            result.EmaGradient = gradient.RawGradient;
            result.EmaGradientNormalized = gradient.Normalized;
            result.PredictedNextDirection = gradient.Normalized > 0.01m ? 1 : gradient.Normalized < -0.01m ? -1 : 0;

            // 7. Confidence and metadata
            result.Confidence = CalculateConfidence(gradient, volatility);
            result.CurrentAtr = (decimal)(_atr?.Results?.LastOrDefault()?.Atr ?? 0);
            result.AtrPercentile = volatility.AtrPercentile;
            result.TrendStrength = Math.Abs(gradient.Normalized);
            result.Reasoning = BuildReasoning(gradient, volatility, result.ActiveZones.Count);

            return result;
        }

        #region EMA Gradient Analysis

        private (decimal RawGradient, decimal Normalized) CalculateEmaGradient()
        {
            var emaResults = _emaGradient?.Results;
            if (emaResults == null || emaResults.Count < _gradientLookback + 1)
                return (0, 0);

            // Get the last N+1 EMA values to calculate N gradients
            var recentEmas = emaResults.TakeLast(_gradientLookback + 1).ToList();
            var gradients = new decimal[_gradientLookback];

            for (int i = 1; i < recentEmas.Count; i++)
            {
                var prev = (decimal)(recentEmas[i - 1]?.Ema ?? 0);
                var curr = (decimal)(recentEmas[i]?.Ema ?? 0);
                gradients[i - 1] = prev != 0 ? (curr - prev) / prev * 100m : 0;
            }

            decimal avgGradient = gradients.Average();

            // Normalize by ATR percentage to make thresholds asset-agnostic
            decimal currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            decimal currentAtr = (decimal)(_atr?.Results?.LastOrDefault()?.Atr ?? 1);
            decimal atrPercent = currentPrice > 0 ? currentAtr / currentPrice * 100m : 1m;
            decimal normalized = atrPercent > 0 ? avgGradient / atrPercent : 0;

            return (avgGradient, Math.Clamp(normalized, -1m, 1m));
        }

        private MarketRegime ClassifyRegime(decimal normalizedGradient)
        {
            MarketRegime rawRegime;
            if (normalizedGradient > _trendThreshold)
                rawRegime = MarketRegime.BullMarket;
            else if (normalizedGradient < -_trendThreshold)
                rawRegime = MarketRegime.BearMarket;
            else
                rawRegime = MarketRegime.RangingMarket;

            // Require N consecutive bars of a new regime before switching (prevents whipsaw).
            // Transition to Ranging is instant (safer to stop trading than keep stale trend).
            if (rawRegime == _previousRegime)
            {
                _pendingRegimeCount = 0;
                _pendingRegime = rawRegime;
                return rawRegime;
            }

            if (rawRegime == MarketRegime.RangingMarket)
            {
                // Instant transition to Ranging (risk-off)
                _previousRegime = rawRegime;
                _pendingRegimeCount = 0;
                return rawRegime;
            }

            // Trending regime change: require confirmation
            if (rawRegime == _pendingRegime)
            {
                _pendingRegimeCount++;
            }
            else
            {
                _pendingRegime = rawRegime;
                _pendingRegimeCount = 1;
            }

            if (_pendingRegimeCount >= _regimeConfirmationBars)
            {
                _previousRegime = rawRegime;
                _pendingRegimeCount = 0;
                return rawRegime;
            }

            // Not yet confirmed — keep previous regime
            return _previousRegime;
        }

        #endregion

        #region Volatility Analysis

        private (VolatilityRegime Regime, decimal AtrPercentile) AnalyzeVolatility()
        {
            var atrResults = _atr?.Results;
            if (atrResults == null || atrResults.Count < 50)
                return (VolatilityRegime.Normal, 50);

            var atrValues = atrResults.Where(a => a.Atr.HasValue).Select(a => a.Atr.Value).ToList();
            if (!atrValues.Any())
                return (VolatilityRegime.Normal, 50);

            var currentAtr = atrValues.Last();
            int rank = atrValues.Count(x => x <= currentAtr);
            decimal atrPercentile = (decimal)rank / atrValues.Count * 100;

            VolatilityRegime regime;
            if (atrPercentile >= _volatilityHighPercentile)
                regime = VolatilityRegime.High;
            else if (atrPercentile <= _volatilityLowPercentile)
                regime = VolatilityRegime.Low;
            else
                regime = VolatilityRegime.Normal;

            return (regime, atrPercentile);
        }

        #endregion

        #region Regime Rules

        private AllowedDirection GetAllowedDirection(MarketRegime regime, VolatilityRegime volatility)
        {
            return regime switch
            {
                MarketRegime.BullMarket => AllowedDirection.Long,
                MarketRegime.BearMarket => AllowedDirection.Short,
                MarketRegime.RangingMarket when volatility == VolatilityRegime.Low => AllowedDirection.None,
                MarketRegime.RangingMarket => AllowedDirection.Both,
                _ => AllowedDirection.None
            };
        }

        private List<SetupType> GetAllowedSetups(MarketRegime regime, VolatilityRegime volatility)
        {
            var setups = new List<SetupType>();

            switch (regime)
            {
                case MarketRegime.BullMarket:
                    setups.Add(SetupType.MacdTrendLong);
                    break;

                case MarketRegime.BearMarket:
                    setups.Add(SetupType.MacdTrendShort);
                    break;

                case MarketRegime.RangingMarket:
                    if (volatility == VolatilityRegime.High || volatility == VolatilityRegime.Normal)
                    {
                        // Ranging + High/Normal vol = Bollinger mean reversion
                        setups.Add(SetupType.BbMeanRevLong);
                        setups.Add(SetupType.BbMeanRevShort);
                    }
                    // Ranging + Low vol = NO TRADING (empty list)
                    break;
            }

            return setups;
        }

        #endregion

        #region Helpers

        private decimal CalculateConfidence(
            (decimal RawGradient, decimal Normalized) gradient,
            (VolatilityRegime Regime, decimal AtrPercentile) volatility)
        {
            decimal confidence = 0.5m;

            // Stronger gradient = higher confidence in regime classification
            decimal gradientStrength = Math.Abs(gradient.Normalized);
            confidence += gradientStrength * 0.3m;

            // Extreme volatility reduces confidence
            if (volatility.AtrPercentile > 90)
                confidence -= 0.15m;

            // Very low volatility with ranging = low confidence trades
            if (volatility.Regime == VolatilityRegime.Low && Math.Abs(gradient.Normalized) < _trendThreshold)
                confidence -= 0.2m;

            return Math.Clamp(confidence, 0, 1);
        }

        private string BuildReasoning(
            (decimal RawGradient, decimal Normalized) gradient,
            (VolatilityRegime Regime, decimal AtrPercentile) volatility,
            int zoneCount)
        {
            var reasons = new List<string>();

            if (gradient.Normalized > _trendThreshold)
                reasons.Add($"Bull (grad:{gradient.Normalized:F3})");
            else if (gradient.Normalized < -_trendThreshold)
                reasons.Add($"Bear (grad:{gradient.Normalized:F3})");
            else
                reasons.Add($"Ranging (grad:{gradient.Normalized:F3})");

            reasons.Add($"Vol:{volatility.Regime} ATR%:{volatility.AtrPercentile:F0}");
            reasons.Add($"Zones:{zoneCount}");

            return string.Join(" | ", reasons);
        }

        #endregion
    }
}
