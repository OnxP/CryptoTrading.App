using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Enhanced market regime detection using multiple confirming indicators.
    /// Implements IMarketStructureStrategy from Core.
    /// 
    /// Timeframe: 4H
    /// Purpose: Determine market regime and allowed trade direction
    /// </summary>
    public class RegimeBasedMarketStructureStrategy : IMarketStructureStrategy
    {
        private QuoteHub<IQuote> _quoteHub;

        // Streaming indicator hubs
        private EmaHub<IQuote> _ema9;
        private EmaHub<IQuote> _ema21;
        private EmaHub<IQuote> _ema50;
        private EmaHub<IQuote> _ema100;
        private AtrHub<IQuote> _atr;

        // Configuration
        private readonly int _adxTrendThreshold = 25;
        private readonly decimal _volatilityHighPercentile = 75;
        private readonly decimal _volatilityLowPercentile = 25;

        // Batch indicators (recalculated on each Calculate)
        private IReadOnlyList<RsiResult> _rsi;
        private IReadOnlyList<AdxResult> _adx;
        private IReadOnlyList<AroonResult> _aroon;
        private IReadOnlyList<BollingerBandsResult> _bollingerBands;
        private IReadOnlyList<SuperTrendResult> _superTrend;

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;

            // Initialize streaming hubs
            _ema9 = _quoteHub.ToEma(9);
            _ema21 = _quoteHub.ToEma(21);
            _ema50 = _quoteHub.ToEma(50);
            _ema100 = _quoteHub.ToEma(100);
            _atr = _quoteHub.ToAtr(14);

            RefreshBatchIndicators();
        }

        private void RefreshBatchIndicators()
        {
            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 50) return;

            _rsi = _quoteHub.Quotes.ToRsi(14);
            _adx = _quoteHub.Quotes.ToAdx(14);
            _aroon = _quoteHub.Quotes.ToAroon(14);
            _bollingerBands = _quoteHub.Quotes.ToBollingerBands(20, 2);
            _superTrend = _quoteHub.Quotes.ToSuperTrend(10, 3);
        }

        public IMarketStructureResult Calculate()
        {
            RefreshBatchIndicators();

            var result = new RegimeBasedMarketStructureResult();

            if (_quoteHub == null || _quoteHub.Quotes.Count < 100)
            {
                result.MarketRegime = MarketRegime.RangingMarket;
                result.Confidence = 0;
                result.Reasoning = "Insufficient data";
                return result;
            }

            // Analyze market components
            var trend = AnalyzeTrend();
            var volatility = AnalyzeVolatility();
            var momentum = AnalyzeMomentum();

            // Determine regime using Core's MarketRegime enum
            result.MarketRegime = DetermineRegime(trend, momentum);
            result.VolatilityRegime = volatility.Regime;
            result.Confidence = CalculateConfidence(trend, volatility, momentum);
            result.AllowedDirection = GetAllowedDirection(result.MarketRegime, result.VolatilityRegime);
            result.AllowedSetups = GetAllowedSetups(result.MarketRegime, result.VolatilityRegime);
            result.Reasoning = BuildReasoning(trend, volatility, momentum);

            // Pass data downstream
            result.CurrentAtr = (decimal)(_atr?.Results?.LastOrDefault()?.Atr ?? 0);
            result.CurrentRsi = (decimal)(_rsi?.LastOrDefault()?.Rsi ?? 50);
            result.TrendStrength = trend.Strength;

            return result;
        }

        #region Analysis

        private (bool IsUptrend, bool IsDowntrend, decimal Strength, decimal Slope) AnalyzeTrend()
        {
            var ema9 = _ema9?.Results?.LastOrDefault()?.Ema ?? 0;
            var ema21 = _ema21?.Results?.LastOrDefault()?.Ema ?? 0;
            var ema50 = _ema50?.Results?.LastOrDefault()?.Ema ?? 0;
            var ema100 = _ema100?.Results?.LastOrDefault()?.Ema ?? 0;

            bool emasBullish = ema9 > ema21 && ema21 > ema50 && ema50 > ema100;
            bool emasBearish = ema9 < ema21 && ema21 < ema50 && ema50 < ema100;

            var superTrendLast = _superTrend?.LastOrDefault();
            bool superTrendBullish = superTrendLast?.UpperBand == null;

            var adxLast = _adx?.LastOrDefault();
            decimal strength = Math.Min((decimal)(adxLast?.Adx ?? 0) / 50m, 1m);

            // Calculate slope
            decimal slope = 0;
            if (_ema9?.Results?.Count >= 5)
            {
                var recentEmas = _ema9.Results.TakeLast(5).ToList();
                var first = recentEmas.FirstOrDefault()?.Ema ?? 0;
                var last = recentEmas.LastOrDefault()?.Ema ?? 0;
                slope = first != 0 ? (decimal)((last - first) / first * 100) : 0;
            }

            bool isUptrend = emasBullish && superTrendBullish && slope > 0;
            bool isDowntrend = emasBearish && !superTrendBullish && slope < 0;

            return (isUptrend, isDowntrend, strength, slope);
        }

        private (VolatilityRegime Regime, decimal AtrPercentile, decimal BbWidth) AnalyzeVolatility()
        {
            var atrResults = _atr?.Results;
            if (atrResults == null || atrResults.Count < 100)
                return (VolatilityRegime.Normal, 50, 0);

            var atrValues = atrResults.Where(a => a.Atr.HasValue).Select(a => a.Atr.Value).ToList();
            var currentAtr = atrValues.LastOrDefault();
            int rank = atrValues.Count(x => x <= currentAtr);
            decimal atrPercentile = (decimal)rank / atrValues.Count * 100;

            var bbLast = _bollingerBands?.LastOrDefault();
            decimal bbWidth = bbLast?.Sma > 0
                ? (decimal)((bbLast.UpperBand - bbLast.LowerBand) / bbLast.Sma * 100d)
                : 0m;

            VolatilityRegime regime;
            if (atrPercentile >= _volatilityHighPercentile)
                regime = VolatilityRegime.High;
            else if (atrPercentile <= _volatilityLowPercentile)
                regime = VolatilityRegime.Low;
            else
                regime = VolatilityRegime.Normal;

            return (regime, atrPercentile, bbWidth);
        }

        private (decimal Rsi, bool BullishDivergence, bool BearishDivergence) AnalyzeMomentum()
        {
            decimal rsi = (decimal)(_rsi?.LastOrDefault()?.Rsi ?? 50);
            bool bullDiv = false, bearDiv = false;

            if (_rsi?.Count >= 20 && _quoteHub.Quotes.Count >= 20)
            {
                var recentRsi = _rsi.TakeLast(10).Average(r => r.Rsi ?? 50);
                var olderRsi = _rsi.Skip(_rsi.Count - 20).Take(10).Average(r => r.Rsi ?? 50);

                var recentPrices = _quoteHub.Quotes.TakeLast(10).ToList();
                var olderPrices = _quoteHub.Quotes.Skip(_quoteHub.Quotes.Count - 20).Take(10).ToList();

                var recentLow = recentPrices.Min(p => p.Low);
                var olderLow = olderPrices.Min(p => p.Low);
                var recentHigh = recentPrices.Max(p => p.High);
                var olderHigh = olderPrices.Max(p => p.High);

                bullDiv = recentLow < olderLow && recentRsi > olderRsi && rsi < 40;
                bearDiv = recentHigh > olderHigh && recentRsi < olderRsi && rsi > 60;
            }

            return (rsi, bullDiv, bearDiv);
        }

        #endregion

        #region Regime Determination

        private MarketRegime DetermineRegime(
            (bool IsUptrend, bool IsDowntrend, decimal Strength, decimal Slope) trend,
            (decimal Rsi, bool BullishDivergence, bool BearishDivergence) momentum)
        {
            var aroonLast = _aroon?.LastOrDefault();

            if (trend.IsUptrend && trend.Strength > 0.5m)
                return MarketRegime.BullMarket;

            if (trend.IsUptrend || (momentum.BullishDivergence && momentum.Rsi < 40))
                return MarketRegime.BullMarket;

            if (trend.IsDowntrend && trend.Strength > 0.5m)
                return MarketRegime.BearMarket;

            if (trend.IsDowntrend || (momentum.BearishDivergence && momentum.Rsi > 60))
                return MarketRegime.BearMarket;

            if (aroonLast != null)
            {
                if (aroonLast.AroonUp > 70 && aroonLast.AroonDown < 30)
                    return MarketRegime.BullMarket;
                if (aroonLast.AroonDown > 70 && aroonLast.AroonUp < 30)
                    return MarketRegime.BearMarket;
            }

            return MarketRegime.RangingMarket;
        }

        private AllowedDirection GetAllowedDirection(MarketRegime regime, VolatilityRegime volatility)
        {
            if (volatility == VolatilityRegime.High && regime == MarketRegime.RangingMarket)
                return AllowedDirection.None;

            return regime switch
            {
                MarketRegime.BullMarket => AllowedDirection.Long,
                MarketRegime.BearMarket => AllowedDirection.Short,
                MarketRegime.RangingMarket when volatility == VolatilityRegime.Low => AllowedDirection.None,
                _ => AllowedDirection.None
            };
        }

        private List<SetupType> GetAllowedSetups(MarketRegime regime, VolatilityRegime volatility)
        {
            var setups = new List<SetupType>();

            switch (regime)
            {
                case MarketRegime.BullMarket:
                    setups.Add(SetupType.TrendContinuationLong);
                    setups.Add(SetupType.BreakoutLong);
                    if (volatility == VolatilityRegime.High)
                        setups.Add(SetupType.FadeExtremeLong);
                    break;

                case MarketRegime.BearMarket:
                    setups.Add(SetupType.TrendContinuationShort);
                    setups.Add(SetupType.BreakoutShort);
                    if (volatility == VolatilityRegime.High)
                        setups.Add(SetupType.FadeExtremeShort);
                    break;

                case MarketRegime.RangingMarket:
                    if (volatility == VolatilityRegime.Low)
                    {
                        setups.Add(SetupType.BreakoutLong);
                        setups.Add(SetupType.BreakoutShort);
                    }
                    else
                    {
                        setups.Add(SetupType.MeanReversionLong);
                        setups.Add(SetupType.MeanReversionShort);
                    }
                    break;
            }

            return setups;
        }

        private decimal CalculateConfidence(
            (bool IsUptrend, bool IsDowntrend, decimal Strength, decimal Slope) trend,
            (VolatilityRegime Regime, decimal AtrPercentile, decimal BbWidth) volatility,
            (decimal Rsi, bool BullishDivergence, bool BearishDivergence) momentum)
        {
            decimal confidence = 0.5m;

            confidence += trend.Strength * 0.2m;

            if (trend.IsUptrend || trend.IsDowntrend)
                confidence += 0.15m;

            if ((momentum.Rsi > 75 && trend.IsUptrend) || (momentum.Rsi < 25 && trend.IsDowntrend))
                confidence -= 0.1m;

            if (momentum.BullishDivergence || momentum.BearishDivergence)
                confidence -= 0.1m;

            if (volatility.AtrPercentile > 90)
                confidence -= 0.15m;

            return Math.Clamp(confidence, 0, 1);
        }

        private string BuildReasoning(
            (bool IsUptrend, bool IsDowntrend, decimal Strength, decimal Slope) trend,
            (VolatilityRegime Regime, decimal AtrPercentile, decimal BbWidth) volatility,
            (decimal Rsi, bool BullishDivergence, bool BearishDivergence) momentum)
        {
            var reasons = new List<string>();

            if (trend.IsUptrend)
                reasons.Add($"Uptrend (slope:{trend.Slope:F2}% str:{trend.Strength:F2})");
            else if (trend.IsDowntrend)
                reasons.Add($"Downtrend (slope:{trend.Slope:F2}% str:{trend.Strength:F2})");
            else
                reasons.Add("Ranging");

            reasons.Add($"Vol:{volatility.Regime} ATR%:{volatility.AtrPercentile:F0}");
            reasons.Add($"RSI:{momentum.Rsi:F1}");

            if (momentum.BullishDivergence) reasons.Add("BullDiv");
            if (momentum.BearishDivergence) reasons.Add("BearDiv");

            return string.Join(" | ", reasons);
        }

        #endregion
    }
}