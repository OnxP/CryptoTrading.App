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
    /// Evaluates setups allowed by the regime and calculates:
    /// - Entry zone
    /// - Stop loss and take profit levels
    /// - Risk/reward ratio
    /// - Recommended entry and exit strategies
    /// </summary>
    public class RegimeBasedSetupStrategy : IStrategy
    {
        private QuoteHub<IQuote> _quoteHub;

        // Streaming indicators
        private EmaHub<IQuote> _ema9;
        private EmaHub<IQuote> _ema21;
        private AtrHub<IQuote> _atr;

        // Batch indicators
        private IReadOnlyList<RsiResult> _rsi;
        private IReadOnlyList<BollingerBandsResult> _bollingerBands;

        // Configuration
        private readonly decimal _minRiskRewardRatio = 1.5m;
        private readonly decimal _volumeThreshold = 1.2m;

        public void SetQuotes(QuoteHub<IQuote> quoteHub)
        {
            _quoteHub = quoteHub;
            _ema9 = _quoteHub.ToEma(9);
            _ema21 = _quoteHub.ToEma(21);
            _atr = _quoteHub.ToAtr(14);
            RefreshIndicators();
        }

        private void RefreshIndicators()
        {
            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 50) return;
            _rsi = _quoteHub.Quotes.ToRsi(14);
            _bollingerBands = _quoteHub.Quotes.ToBollingerBands(20, 2);
        }

        public (IStrategyResult, IExecutionStrategy) Calculate(IMarketStructureResult marketStructure)
        {
            RefreshIndicators();

            // Cast to our extended result
            var regimeResult = marketStructure as RegimeBasedMarketStructureResult;
            if (regimeResult == null || !regimeResult.AllowedSetups.Any() ||
                regimeResult.AllowedDirection == AllowedDirection.None)
            {
                return (new StrategyResult { PostTrade = false }, null);
            }

            // Find the best valid setup
            SetupResult bestSetup = null;
            foreach (var setupType in regimeResult.AllowedSetups)
            {
                var setup = EvaluateSetup(setupType);
                if (setup != null && setup.IsValid)
                {
                    if (bestSetup == null || setup.RiskRewardRatio > bestSetup.RiskRewardRatio)
                        bestSetup = setup;
                }
            }

            if (bestSetup == null || !bestSetup.IsValid)
                return (new StrategyResult { PostTrade = false }, null);

            // Create result and execution strategy
            var strategyResult = new RegimeBasedStrategyResult
            {
                PostTrade = true,
                Amount = 0.1m,
                Leverage = 3,
                OrderSide = bestSetup.Direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell,
                Setup = bestSetup
            };

            var executionStrategy = new RegimeBasedExecutionStrategy(_quoteHub, bestSetup);

            return (strategyResult, executionStrategy);
        }

        #region Setup Evaluation

        private SetupResult EvaluateSetup(SetupType setupType)
        {
            return setupType switch
            {
                SetupType.TrendContinuationLong => EvaluateTrendContinuationLong(),
                SetupType.TrendContinuationShort => EvaluateTrendContinuationShort(),
                SetupType.MeanReversionLong => EvaluateMeanReversionLong(),
                SetupType.MeanReversionShort => EvaluateMeanReversionShort(),
                SetupType.BreakoutLong => EvaluateBreakoutLong(),
                SetupType.BreakoutShort => EvaluateBreakoutShort(),
                SetupType.FadeExtremeLong => EvaluateFadeExtremeLong(),
                SetupType.FadeExtremeShort => EvaluateFadeExtremeShort(),
                _ => null
            };
        }

        private SetupResult EvaluateTrendContinuationLong()
        {
            var result = new SetupResult
            {
                SetupType = SetupType.TrendContinuationLong,
                Direction = TradeDirection.Long
            };

            if (!HasSufficientData()) return result;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var ema9 = (decimal)(_ema9.Results.LastOrDefault()?.Ema ?? 0);
            var ema21 = (decimal)(_ema21.Results.LastOrDefault()?.Ema ?? 0);
            var rsi = (decimal)(_rsi.LastOrDefault()?.Rsi ?? 50);
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);

            bool emaAligned = ema9 > ema21;
            bool pullbackToEma = currentPrice <= ema9 && currentPrice >= ema21;
            bool rsiHealthy = rsi > 40 && rsi < 70;
            bool volumeOk = GetVolumeRatio() >= 0.8m;

            var swingLow = (decimal)_quoteHub.Quotes.TakeLast(20).Min(q => q.Low);
            var resistance = (decimal)_quoteHub.Quotes.TakeLast(50).Max(q => q.High);
            if (resistance <= currentPrice) resistance = currentPrice + (3 * atr);

            decimal risk = currentPrice - swingLow;
            decimal reward = resistance - currentPrice;
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            result.IsValid = emaAligned && pullbackToEma && rsiHealthy && volumeOk &&
                            result.RiskRewardRatio >= _minRiskRewardRatio;
            result.EntryZoneHigh = ema9;
            result.EntryZoneLow = ema21;
            result.StopLoss = swingLow - (0.1m * atr);
            result.TakeProfit = resistance;
            result.Confidence = CalculateConfidence(emaAligned, pullbackToEma, rsiHealthy, GetVolumeRatio(), result.RiskRewardRatio);
            result.RecommendedEntryStrategy = EntryStrategyType.LimitAtSupport;
            result.RecommendedExitStrategy = result.RiskRewardRatio > 2.5m ? ExitStrategyType.ScaleOut : ExitStrategyType.TrailingStop;
            result.Reasoning = $"EMA:{emaAligned} Pull:{pullbackToEma} RSI:{rsi:F1} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        private SetupResult EvaluateTrendContinuationShort()
        {
            var result = new SetupResult
            {
                SetupType = SetupType.TrendContinuationShort,
                Direction = TradeDirection.Short
            };

            if (!HasSufficientData()) return result;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var ema9 = (decimal)(_ema9.Results.LastOrDefault()?.Ema ?? 0);
            var ema21 = (decimal)(_ema21.Results.LastOrDefault()?.Ema ?? 0);
            var rsi = (decimal)(_rsi.LastOrDefault()?.Rsi ?? 50);
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);

            bool emaAligned = ema9 < ema21;
            bool pullbackToEma = currentPrice >= ema9 && currentPrice <= ema21;
            bool rsiHealthy = rsi < 60 && rsi > 30;
            bool volumeOk = GetVolumeRatio() >= 0.8m;

            var swingHigh = (decimal)_quoteHub.Quotes.TakeLast(20).Max(q => q.High);
            var support = (decimal)_quoteHub.Quotes.TakeLast(50).Min(q => q.Low);
            if (support >= currentPrice) support = currentPrice - (3 * atr);

            decimal risk = swingHigh - currentPrice;
            decimal reward = currentPrice - support;
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            result.IsValid = emaAligned && pullbackToEma && rsiHealthy && volumeOk &&
                            result.RiskRewardRatio >= _minRiskRewardRatio;
            result.EntryZoneHigh = ema21;
            result.EntryZoneLow = ema9;
            result.StopLoss = swingHigh + (0.1m * atr);
            result.TakeProfit = support;
            result.Confidence = CalculateConfidence(emaAligned, pullbackToEma, rsiHealthy, GetVolumeRatio(), result.RiskRewardRatio);
            result.RecommendedEntryStrategy = EntryStrategyType.LimitAtResistance;
            result.RecommendedExitStrategy = result.RiskRewardRatio > 2.5m ? ExitStrategyType.ScaleOut : ExitStrategyType.TrailingStop;
            result.Reasoning = $"EMA:{emaAligned} Pull:{pullbackToEma} RSI:{rsi:F1} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        private SetupResult EvaluateMeanReversionLong()
        {
            var result = new SetupResult
            {
                SetupType = SetupType.MeanReversionLong,
                Direction = TradeDirection.Long
            };

            if (!HasSufficientData()) return result;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var bb = _bollingerBands.LastOrDefault();
            var rsi = (decimal)(_rsi.LastOrDefault()?.Rsi ?? 50);
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);

            if (bb == null) return result;

            var bbLower = (decimal)(bb.LowerBand ?? 0);
            var bbMiddle = (decimal)(bb.Sma ?? 0);

            bool atLowerBand = currentPrice <= bbLower * 1.005m;
            bool oversold = rsi < 35;

            var recentLow = (decimal)_quoteHub.Quotes.TakeLast(5).Min(q => q.Low);

            decimal risk = currentPrice - (recentLow - 0.2m * atr);
            decimal reward = bbMiddle - currentPrice;
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            result.IsValid = atLowerBand && oversold && result.RiskRewardRatio >= _minRiskRewardRatio;
            result.EntryZoneHigh = bbLower * 1.01m;
            result.EntryZoneLow = bbLower * 0.99m;
            result.StopLoss = recentLow - (0.3m * atr);
            result.TakeProfit = bbMiddle;
            result.Confidence = CalculateMRConfidence(atLowerBand, oversold, GetVolumeRatio() > 1.5m, result.RiskRewardRatio);
            result.RecommendedEntryStrategy = GetVolumeRatio() > 1.5m ? EntryStrategyType.ScaleIn : EntryStrategyType.LimitAtSupport;
            result.RecommendedExitStrategy = ExitStrategyType.FixedTarget;
            result.Reasoning = $"AtBB:{atLowerBand} RSI:{rsi:F1} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        private SetupResult EvaluateMeanReversionShort()
        {
            var result = new SetupResult
            {
                SetupType = SetupType.MeanReversionShort,
                Direction = TradeDirection.Short
            };

            if (!HasSufficientData()) return result;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var bb = _bollingerBands.LastOrDefault();
            var rsi = (decimal)(_rsi.LastOrDefault()?.Rsi ?? 50);
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);

            if (bb == null) return result;

            var bbUpper = (decimal)(bb.UpperBand ?? 0);
            var bbMiddle = (decimal)(bb.Sma ?? 0);

            bool atUpperBand = currentPrice >= bbUpper * 0.995m;
            bool overbought = rsi > 65;

            var recentHigh = (decimal)_quoteHub.Quotes.TakeLast(5).Max(q => q.High);

            decimal risk = (recentHigh + 0.2m * atr) - currentPrice;
            decimal reward = currentPrice - bbMiddle;
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            result.IsValid = atUpperBand && overbought && result.RiskRewardRatio >= _minRiskRewardRatio;
            result.EntryZoneHigh = bbUpper * 1.01m;
            result.EntryZoneLow = bbUpper * 0.99m;
            result.StopLoss = recentHigh + (0.3m * atr);
            result.TakeProfit = bbMiddle;
            result.Confidence = CalculateMRConfidence(atUpperBand, overbought, GetVolumeRatio() > 1.5m, result.RiskRewardRatio);
            result.RecommendedEntryStrategy = GetVolumeRatio() > 1.5m ? EntryStrategyType.ScaleIn : EntryStrategyType.LimitAtResistance;
            result.RecommendedExitStrategy = ExitStrategyType.FixedTarget;
            result.Reasoning = $"AtBB:{atUpperBand} RSI:{rsi:F1} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        private SetupResult EvaluateBreakoutLong()
        {
            var result = new SetupResult
            {
                SetupType = SetupType.BreakoutLong,
                Direction = TradeDirection.Long
            };

            if (!HasSufficientData()) return result;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var bb = _bollingerBands.LastOrDefault();
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);

            if (bb == null) return result;

            var recentCandles = _quoteHub.Quotes.TakeLast(20).ToList();
            var rangeHigh = (decimal)recentCandles.Max(c => c.High);
            var rangeLow = (decimal)recentCandles.Min(c => c.Low);
            var rangeSize = rangeHigh - rangeLow;

            var bbWidth = bb.Sma > 0 ? (decimal)((bb.UpperBand - bb.LowerBand) / bb.Sma * 100) : 10;

            bool volatilityCompressed = bbWidth < 3;
            bool nearResistance = currentPrice >= rangeHigh * 0.99m;
            bool volumeConfirm = GetVolumeRatio() > _volumeThreshold;

            decimal target = rangeHigh + rangeSize;
            decimal stop = rangeLow - (0.1m * atr);

            decimal risk = currentPrice - stop;
            decimal reward = target - currentPrice;
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            result.IsValid = volatilityCompressed && nearResistance && result.RiskRewardRatio >= _minRiskRewardRatio;
            result.EntryZoneHigh = rangeHigh * 1.01m;
            result.EntryZoneLow = rangeHigh * 0.995m;
            result.StopLoss = stop;
            result.TakeProfit = target;
            result.Confidence = CalculateBreakoutConfidence(volatilityCompressed, volumeConfirm, result.RiskRewardRatio);
            result.RecommendedEntryStrategy = EntryStrategyType.BreakoutEntry;
            result.RecommendedExitStrategy = ExitStrategyType.TrailingStop;
            result.Reasoning = $"Compressed:{volatilityCompressed} NearRes:{nearResistance} BBW:{bbWidth:F2}% R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        private SetupResult EvaluateBreakoutShort()
        {
            var result = new SetupResult
            {
                SetupType = SetupType.BreakoutShort,
                Direction = TradeDirection.Short
            };

            if (!HasSufficientData()) return result;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var bb = _bollingerBands.LastOrDefault();
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);

            if (bb == null) return result;

            var recentCandles = _quoteHub.Quotes.TakeLast(20).ToList();
            var rangeHigh = (decimal)recentCandles.Max(c => c.High);
            var rangeLow = (decimal)recentCandles.Min(c => c.Low);
            var rangeSize = rangeHigh - rangeLow;

            var bbWidth = bb.Sma > 0 ? (decimal)((bb.UpperBand - bb.LowerBand) / bb.Sma * 100) : 10;

            bool volatilityCompressed = bbWidth < 3;
            bool nearSupport = currentPrice <= rangeLow * 1.01m;
            bool volumeConfirm = GetVolumeRatio() > _volumeThreshold;

            decimal target = rangeLow - rangeSize;
            decimal stop = rangeHigh + (0.1m * atr);

            decimal risk = stop - currentPrice;
            decimal reward = currentPrice - target;
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            result.IsValid = volatilityCompressed && nearSupport && result.RiskRewardRatio >= _minRiskRewardRatio;
            result.EntryZoneHigh = rangeLow * 1.005m;
            result.EntryZoneLow = rangeLow * 0.99m;
            result.StopLoss = stop;
            result.TakeProfit = target;
            result.Confidence = CalculateBreakoutConfidence(volatilityCompressed, volumeConfirm, result.RiskRewardRatio);
            result.RecommendedEntryStrategy = EntryStrategyType.BreakoutEntry;
            result.RecommendedExitStrategy = ExitStrategyType.TrailingStop;
            result.Reasoning = $"Compressed:{volatilityCompressed} NearSup:{nearSupport} BBW:{bbWidth:F2}% R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        private SetupResult EvaluateFadeExtremeLong()
        {
            var result = new SetupResult
            {
                SetupType = SetupType.FadeExtremeLong,
                Direction = TradeDirection.Long
            };

            if (!HasSufficientData()) return result;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var bb = _bollingerBands.LastOrDefault();
            var rsi = (decimal)(_rsi.LastOrDefault()?.Rsi ?? 50);
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);

            if (bb == null) return result;

            var bbLower = (decimal)(bb.LowerBand ?? 0);
            var bbMiddle = (decimal)(bb.Sma ?? 0);

            bool extremeOversold = rsi < 25;
            bool belowLowerBand = currentPrice < bbLower;

            var recentLow = (decimal)_quoteHub.Quotes.TakeLast(3).Min(q => q.Low);

            decimal risk = currentPrice - (recentLow - 0.2m * atr);
            decimal reward = bbMiddle - currentPrice;
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            result.IsValid = extremeOversold && belowLowerBand && result.RiskRewardRatio >= _minRiskRewardRatio;
            result.EntryZoneHigh = bbLower;
            result.EntryZoneLow = bbLower * 0.98m;
            result.StopLoss = recentLow - (0.2m * atr);
            result.TakeProfit = bbMiddle;
            result.Confidence = CalculateFadeConfidence(extremeOversold, GetVolumeRatio() > 2m, result.RiskRewardRatio);
            result.RecommendedEntryStrategy = EntryStrategyType.ScaleIn;
            result.RecommendedExitStrategy = ExitStrategyType.ScaleOut;
            result.Reasoning = $"ExtremeOS:{extremeOversold} RSI:{rsi:F1} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        private SetupResult EvaluateFadeExtremeShort()
        {
            var result = new SetupResult
            {
                SetupType = SetupType.FadeExtremeShort,
                Direction = TradeDirection.Short
            };

            if (!HasSufficientData()) return result;

            var currentPrice = (decimal)_quoteHub.Quotes.Last().Close;
            var bb = _bollingerBands.LastOrDefault();
            var rsi = (decimal)(_rsi.LastOrDefault()?.Rsi ?? 50);
            var atr = (decimal)(_atr.Results.LastOrDefault()?.Atr ?? 0);

            if (bb == null) return result;

            var bbUpper = (decimal)(bb.UpperBand ?? 0);
            var bbMiddle = (decimal)(bb.Sma ?? 0);

            bool extremeOverbought = rsi > 75;
            bool aboveUpperBand = currentPrice > bbUpper;

            var recentHigh = (decimal)_quoteHub.Quotes.TakeLast(3).Max(q => q.High);

            decimal risk = (recentHigh + 0.2m * atr) - currentPrice;
            decimal reward = currentPrice - bbMiddle;
            result.RiskRewardRatio = risk > 0 ? reward / risk : 0;

            result.IsValid = extremeOverbought && aboveUpperBand && result.RiskRewardRatio >= _minRiskRewardRatio;
            result.EntryZoneHigh = bbUpper * 1.02m;
            result.EntryZoneLow = bbUpper;
            result.StopLoss = recentHigh + (0.2m * atr);
            result.TakeProfit = bbMiddle;
            result.Confidence = CalculateFadeConfidence(extremeOverbought, GetVolumeRatio() > 2m, result.RiskRewardRatio);
            result.RecommendedEntryStrategy = EntryStrategyType.ScaleIn;
            result.RecommendedExitStrategy = ExitStrategyType.ScaleOut;
            result.Reasoning = $"ExtremeOB:{extremeOverbought} RSI:{rsi:F1} R:R:{result.RiskRewardRatio:F2}";

            return result;
        }

        #endregion

        #region Helpers

        private bool HasSufficientData() =>
            _quoteHub?.Quotes != null && _quoteHub.Quotes.Count >= 50;

        private decimal GetVolumeRatio()
        {
            if (_quoteHub?.Quotes == null || _quoteHub.Quotes.Count < 21) return 1m;

            var volumes = _quoteHub.Quotes.TakeLast(21).Select(q => q.Volume).ToList();
            var currentVolume = volumes.Last();
            var avgVolume = volumes.Take(20).Average();

            return avgVolume > 0 ? (decimal)(currentVolume / avgVolume) : 1m;
        }

        private decimal CalculateConfidence(bool c1, bool c2, bool c3, decimal volRatio, decimal rr)
        {
            decimal conf = 0.3m;
            if (c1) conf += 0.2m;
            if (c2) conf += 0.15m;
            if (c3) conf += 0.1m;
            if (volRatio > 1.2m) conf += 0.1m;
            if (rr > 2m) conf += 0.15m;
            return Math.Clamp(conf, 0, 1);
        }

        private decimal CalculateMRConfidence(bool atBand, bool extreme, bool volSpike, decimal rr)
        {
            decimal conf = 0.25m;
            if (atBand) conf += 0.2m;
            if (extreme) conf += 0.2m;
            if (volSpike) conf += 0.15m;
            if (rr > 2m) conf += 0.2m;
            return Math.Clamp(conf, 0, 1);
        }

        private decimal CalculateBreakoutConfidence(bool compressed, bool volConfirm, decimal rr)
        {
            decimal conf = 0.3m;
            if (compressed) conf += 0.25m;
            if (volConfirm) conf += 0.2m;
            if (rr > 2m) conf += 0.25m;
            return Math.Clamp(conf, 0, 1);
        }

        private decimal CalculateFadeConfidence(bool extreme, bool volClimax, decimal rr)
        {
            decimal conf = 0.2m;
            if (extreme) conf += 0.25m;
            if (volClimax) conf += 0.15m;
            if (rr > 2m) conf += 0.2m;
            return Math.Clamp(conf, 0, 1);
        }

        #endregion
    }
}