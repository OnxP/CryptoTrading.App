using Binance;
using CryptoTrading.App.Algorithm.DualRegime.Persistence;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CryptoTrading.App.Algorithm.DualRegime
{
    /// <summary>
    /// Dual-Regime BTC Futures Swing Strategy.
    ///
    /// Two complementary strategies on shared capital, one position at a time:
    ///   1. Trend strategy — active when 4H regime is trending (pred_4h != 0)
    ///   2. Chop trend-follow — active when 4H regime is neutral (pred_4h == 0)
    ///
    /// Entry timeframe: 15-min bars aggregated to 1H decision points.
    /// ML: XGBoost-trained ONNX models predict 1H and 4H forward returns.
    /// Hold duration: up to 8 hours per trade.
    /// </summary>
    public sealed class DualRegimeAlgorithm : IAlgorithm
    {
        private readonly ILogger<DualRegimeAlgorithm> _logger;
        private readonly AlgoStatePersistence _persistence;

        // Model and feature pipeline
        private DualRegimeModelArtifact _artifact;
        private DualRegimeCandleAggregator _aggregator;
        private DualRegimeParams _params;
        private DualRegimeTradingState _tradingState;

        // Quote hub for execution strategy monitoring
        private readonly QuoteHub<IQuote> _quoteHub15M;

        // Config
        private Symbol _symbol;
        private IConfig _config;
        private decimal _startBtcAmount;
        private bool _equityInitialized;

        // Model artifact directory (configurable, default to app-relative path)
        private string _artifactDir;

        // State persistence / resume
        private bool _restoredFromSnapshot;
        private DateTime _restoredLastBarTime;
        private DualRegimeSetup _activeSetup;

        // Diagnostics: prediction-class distribution across entry-decision bars
        private int _evalCount;
        private readonly int[] _pred4HDist = new int[3]; // [-1, 0, +1]
        private readonly int[] _pred1HDist = new int[3]; // [-1, 0, +1]

        public string KeyValue { get; set; } = "1";
        public IConfig Config => _config;

        public DualRegimeAlgorithm(ILogger<DualRegimeAlgorithm> logger, AlgoStatePersistence persistence = null)
        {
            _logger = logger;
            _persistence = persistence;
            _quoteHub15M = new QuoteHub<IQuote>(500);
            _params = new DualRegimeParams();
        }

        public void Configure(IConfig config)
        {
            _config = config;
            _startBtcAmount = (decimal)(config.StartBtcAmount > 0 ? config.StartBtcAmount : 1);
            _tradingState = new DualRegimeTradingState(_startBtcAmount);

            _artifactDir = ResolveArtifactDir();

            try
            {
                _artifact = TryLoadModelFromDb() ?? DualRegimeModelArtifact.Load(_artifactDir);
                _aggregator = new DualRegimeCandleAggregator(_artifact);

                _logger.LogInformation(
                    $"[DUAL-REGIME] Model loaded: trained {_artifact.TrainedAt:yyyy-MM-dd}, " +
                    $"R2_1h={_artifact.ValR2_1H:F3}, R2_4h={_artifact.ValR2_4H:F3}, " +
                    $"sigma_1h={_artifact.Sigma1H:F6}, sigma_4h={_artifact.Sigma4H:F6}");

                if (!_artifact.Validate())
                    _logger.LogWarning("[DUAL-REGIME] Model validation FAILED — trading with degraded confidence");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[DUAL-REGIME] Failed to load model from {_artifactDir}");
                throw;
            }

            TryRestoreState();
            LogRunParameters();
        }

        private void TryRestoreState()
        {
            if (_persistence == null) return;

            try
            {
                var gap = _persistence.GetGapDurationAsync("DualRegime", KeyValue).Result;
                if (gap == null)
                {
                    _logger.LogInformation("[DUAL-REGIME] No saved state found — starting fresh");
                    return;
                }

                if (gap.Value.TotalHours >= 4)
                {
                    _logger.LogInformation(
                        $"[DUAL-REGIME] State gap is {gap.Value.TotalHours:F1}h (>= 4h) — full recomputation from historic data");
                    return;
                }

                var snapshot = _persistence.LoadAsync("DualRegime", KeyValue).Result;
                if (snapshot == null) return;

                RestoreFromSnapshot(snapshot);
                _restoredFromSnapshot = true;
                _restoredLastBarTime = snapshot.LastBarTime;

                _logger.LogInformation(
                    $"[DUAL-REGIME] Restored state — gap {gap.Value.TotalMinutes:F0}m | " +
                    $"1H bars: {_aggregator.Count1H} | InPosition: {_tradingState.IsInPosition} | " +
                    $"Equity: {_tradingState.CurrentEquity:F2}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DUAL-REGIME] Failed to restore state — starting fresh");
                _restoredFromSnapshot = false;
            }
        }

        public void Subscribe(Symbol symbol, IMarketDataEvents marketData)
        {
            _symbol = symbol;

            marketData.InitialDataLoadSubscribe(symbol, CandleInterval.Minute_15, ProcessHistoricData15M);
            marketData.InitialDataStreamSubscribe(symbol, CandleInterval.Minute_15, ProcessLiveCandle15M);

            _logger.LogInformation($"[DUAL-REGIME] Subscribed to {symbol} on 15M");
        }

        #region Data Processing

        private void ProcessHistoricData15M(IEnumerable<ExchangeCandlestick> candlesticks)
        {
            int count = 0;
            int skipped = 0;
            foreach (var candle in candlesticks)
            {
                if (_restoredFromSnapshot && candle.CloseTime <= _restoredLastBarTime)
                {
                    skipped++;
                    continue;
                }

                var quote = new Quote
                {
                    Timestamp = candle.CloseTime,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    Volume = candle.Volume
                };
                _quoteHub15M.Add(quote);
                _aggregator.AddBar15M(candle);
                count++;
            }

            if (skipped > 0)
                _logger.LogInformation(
                    $"[DUAL-REGIME] Skipped {skipped} historic bars (already in restored state)");

            _logger.LogInformation(
                $"[DUAL-REGIME] Loaded {count} historic 15M bars | " +
                $"1H bars: {_aggregator.Count1H} | Ready: {_aggregator.Ready}");

            _restoredFromSnapshot = false;
        }

        private void ProcessLiveCandle15M(ExchangeCandlestickEvent args)
        {
            try
            {
                if (!args.IsFinal) return;

                var candle = args.Candlestick;
                var quote = new Quote
                {
                    Timestamp = candle.CloseTime,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    Volume = candle.Volume
                };
                _quoteHub15M.Add(quote);

                // Convert BTC equity to USDT on first price
                if (!_equityInitialized && candle.Close > 0)
                {
                    decimal equityUsdt = _startBtcAmount * candle.Close;
                    _tradingState = new DualRegimeTradingState(equityUsdt);
                    _equityInitialized = true;
                    _logger.LogInformation(
                        $"[DUAL-REGIME EQUITY] {_startBtcAmount} BTC x {candle.Close:F2} = {equityUsdt:F2} USDT");
                }

                // Aggregate 15M -> 1H (returns non-null when a new 1H bar completes)
                var newBar1H = _aggregator.AddBar15M(candle);

                if (newBar1H != null)
                {
                    EvaluateSignal(newBar1H, candle);
                    SaveStateAsync(candle.CloseTime);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[DUAL-REGIME] Error processing 15M candle at {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}");
            }
        }

        #endregion

        #region Signal Generation

        private void EvaluateSignal(AggBar bar1H, ExchangeCandlestick rawCandle)
        {
            if (!_aggregator.Ready) return;
            if (_tradingState.IsInPosition) return;

            var features = _aggregator.GetLatestFeatures();
            if (features == null) return;

            string ts = bar1H.Timestamp.ToString("yyyy-MM-dd HH:mm");

            // Run ONNX inference
            double pred1H = _artifact.Predict1H(features);
            double pred4H = _artifact.Predict4H(features);

            int pred1HClass = _artifact.ClassifyPred(pred1H, _artifact.Thr1H);
            int pred4HClass = _artifact.ClassifyPred(pred4H, _artifact.Thr4H);

            // Diagnostics: tally prediction-class distribution at decision bars
            _evalCount++;
            _pred4HDist[pred4HClass + 1]++;
            _pred1HDist[pred1HClass + 1]++;
            if (_evalCount % 500 == 0)
                _logger.LogInformation(
                    $"[DUAL-REGIME PREDDIST] n={_evalCount} | " +
                    $"pred4h(-1/0/+1)={_pred4HDist[0]}/{_pred4HDist[1]}/{_pred4HDist[2]} | " +
                    $"pred1h(-1/0/+1)={_pred1HDist[0]}/{_pred1HDist[1]}/{_pred1HDist[2]} | " +
                    $"last4h={pred4H:F5} last1h={pred1H:F5} regime4h={bar1H.Regime4H3Class}");

            // Try trend strategy first (pred_4h != 0)
            if (pred4HClass != 0)
            {
                TryTrendEntry(bar1H, rawCandle, pred1HClass, pred4HClass, pred4H, ts);
                return;
            }

            // Try chop strategy (pred_4h == 0, Mode C)
            TryChopEntry(bar1H, rawCandle, pred1HClass, pred4HClass, ts);
        }

        private void TryTrendEntry(AggBar bar1H, ExchangeCandlestick rawCandle,
            int pred1HClass, int pred4HClass, double pred4HValue, string ts)
        {
            string signal = GetTrendSignalType(pred4HClass, pred1HClass);
            if (signal == null) return;

            if (Math.Abs(pred4HValue) < _params.EvThreshold) return;

            if (!_params.TrendStops.TryGetValue(signal, out double stopPct))
                return;

            bool isLong = signal.StartsWith("bull");

            // Vol-adjusted target
            double sigma1HNow = _aggregator.GetRollingSigma24();
            double sigma4HEst = sigma1HNow * 2.0;
            double targetPct = _params.TargetVolMultiplier * sigma4HEst;
            targetPct = Math.Max(_params.TargetMinPct, Math.Min(_params.TargetMaxPct, targetPct));

            decimal entryPrice = rawCandle.Close;
            decimal slippage = (decimal)_params.Slippage;
            decimal fillPrice = isLong ? entryPrice * (1 + slippage) : entryPrice * (1 - slippage);

            // Position sizing: risk / stop distance
            decimal notional = _tradingState.CurrentEquity * (decimal)_params.TrendCapitalRisk / (decimal)stopPct;
            decimal quantity = notional / fillPrice;

            decimal stopLoss, takeProfit;
            if (isLong)
            {
                stopLoss = fillPrice * (1 - (decimal)stopPct);
                takeProfit = fillPrice * (1 + (decimal)targetPct);
            }
            else
            {
                stopLoss = fillPrice * (1 + (decimal)stopPct);
                takeProfit = fillPrice * (1 - (decimal)targetPct);
            }

            FireSetup("trend", signal, isLong ? TradeDirection.Long : TradeDirection.Short,
                fillPrice, stopLoss, takeProfit, quantity, stopPct, targetPct, rawCandle, ts);
        }

        private void TryChopEntry(AggBar bar1H, ExchangeCandlestick rawCandle,
            int pred1HClass, int pred4HClass, string ts)
        {
            if (pred4HClass != 0) return;

            int observedClass = bar1H.ClassCombined;
            bool isLong;

            // Mode C: both observed AND predicted must agree
            if ((observedClass == 3 || observedClass == 4) && pred1HClass == 1)
                isLong = true;
            else if ((observedClass == 0 || observedClass == 1) && pred1HClass == -1)
                isLong = false;
            else
                return;

            // Vol-linked stop
            double sigmaNow = _aggregator.GetRollingSigma24();
            if (sigmaNow <= 0) sigmaNow = 0.004;
            double stopPct = _params.ChopStopMult * sigmaNow;
            stopPct = Math.Max(_params.ChopStopMin, Math.Min(_params.ChopStopMax, stopPct));

            decimal entryPrice = rawCandle.Close;
            decimal slippage = (decimal)_params.Slippage;
            decimal fillPrice = isLong ? entryPrice * (1 + slippage) : entryPrice * (1 - slippage);

            decimal notional = _tradingState.CurrentEquity * (decimal)_params.ChopCapitalRisk / (decimal)stopPct;
            decimal quantity = notional / fillPrice;

            decimal stopLoss = isLong
                ? fillPrice * (1 - (decimal)stopPct)
                : fillPrice * (1 + (decimal)stopPct);

            FireSetup("chop", "chop", isLong ? TradeDirection.Long : TradeDirection.Short,
                fillPrice, stopLoss, 0, quantity, stopPct, 0, rawCandle, ts);
        }

        private void FireSetup(string strategyType, string signalType, TradeDirection direction,
            decimal fillPrice, decimal stopLoss, decimal takeProfit, decimal quantity,
            double stopPct, double targetPct,
            ExchangeCandlestick rawCandle, string ts)
        {
            // Minimum quantity guard
            decimal minQty = _symbol?.Quantity?.Minimum ?? 0m;
            decimal qtyIncrement = _symbol?.Quantity?.Increment ?? 0m;
            if (minQty > 0 && quantity < minQty) return;
            if (qtyIncrement > 0 && qtyIncrement < quantity)
                quantity = Math.Floor(quantity / qtyIncrement) * qtyIncrement;

            var setup = new DualRegimeSetup
            {
                StrategyType = strategyType,
                SignalType = signalType,
                Direction = direction,
                EntryPrice = fillPrice,
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                StopPct = stopPct,
                TargetPct = targetPct,
                Quantity = quantity,
                Leverage = _params.Leverage,
                PeakPrice = fillPrice,
                EntryTime = rawCandle.CloseTime,
            };

            var executionStrategy = new DualRegimeExecutionStrategy(setup, _params, _tradingState);
            executionStrategy.SetLogger(_logger);

            var strategyResult = new DualRegimeStrategyResult
            {
                PostTrade = true,
                Amount = quantity * fillPrice,
                Leverage = _params.Leverage,
                OrderSide = direction == TradeDirection.Long ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell,
                Setup = setup,
            };

            _tradingState.IsInPosition = true;
            _activeSetup = setup;

            _logger.LogInformation(
                $"[DUAL-REGIME {ts}] SETUP: {strategyType}/{signalType} {direction} | " +
                $"Price:{fillPrice:F2} | SL:{stopLoss:F2} | TP:{takeProfit:F2} | " +
                $"Qty:{quantity:F6} | Risk:{stopPct * 100:F2}% | " +
                $"{_tradingState.GetStatus()}");

            SaveStateAsync(rawCandle.CloseTime);

            RequestTracker.Instance.Add(
                rawCandle.Symbol,
                new TradeRequest(strategyResult, executionStrategy, _symbol, rawCandle.CloseTime),
                KeyValue);
        }

        private static string GetTrendSignalType(int pred4HClass, int pred1HClass)
        {
            if (pred4HClass == 1 && pred1HClass == 1) return "bull_bull";
            if (pred4HClass == 1 && pred1HClass == 0) return "bull_neutral";
            if (pred4HClass == -1 && pred1HClass == -1) return "bear_bear";
            if (pred4HClass == -1 && pred1HClass == 0) return "bear_neutral";
            return null; // 4h and 1h disagree — skip
        }

        #endregion

        #region State Persistence

        private DualRegimeStateSnapshot ToSnapshot(DateTime lastBarTime)
        {
            var snap = new DualRegimeStateSnapshot
            {
                TradingState = new TradingStateSnapshot
                {
                    CurrentEquity = _tradingState.CurrentEquity,
                    IsInPosition = _tradingState.IsInPosition,
                    TotalTrades = _tradingState.TotalTrades,
                    Wins = _tradingState.Wins,
                    Losses = _tradingState.Losses,
                },
                Aggregator = _aggregator.ToSnapshot(),
                EvalCount = _evalCount,
                Pred4HDist = (int[])_pred4HDist.Clone(),
                Pred1HDist = (int[])_pred1HDist.Clone(),
                ModelArtifactVersion = _artifact.SchemaVersion,
                LastBarTime = lastBarTime,
            };

            if (_activeSetup != null && _tradingState.IsInPosition)
            {
                snap.ActiveSetup = new SetupSnapshot
                {
                    StrategyType = _activeSetup.StrategyType,
                    SignalType = _activeSetup.SignalType,
                    Direction = _activeSetup.Direction.ToString(),
                    EntryPrice = _activeSetup.EntryPrice,
                    StopLoss = _activeSetup.StopLoss,
                    TakeProfit = _activeSetup.TakeProfit,
                    Quantity = _activeSetup.Quantity,
                    Leverage = _activeSetup.Leverage,
                    StopPct = _activeSetup.StopPct,
                    TargetPct = _activeSetup.TargetPct,
                    EntryTime = _activeSetup.EntryTime,
                    PeakPrice = _activeSetup.PeakPrice,
                    Mfe = _activeSetup.Mfe,
                    Mae = _activeSetup.Mae,
                    BarsHeld = _activeSetup.BarsHeld,
                    BeEngaged = _activeSetup.BeEngaged,
                    TrailEngaged = _activeSetup.TrailEngaged,
                };
            }

            return snap;
        }

        private void RestoreFromSnapshot(DualRegimeStateSnapshot snap)
        {
            if (snap.TradingState != null)
            {
                _tradingState.CurrentEquity = snap.TradingState.CurrentEquity;
                _tradingState.IsInPosition = snap.TradingState.IsInPosition;
                _tradingState.TotalTrades = snap.TradingState.TotalTrades;
                _tradingState.Wins = snap.TradingState.Wins;
                _tradingState.Losses = snap.TradingState.Losses;
                _equityInitialized = true;
            }

            if (snap.Aggregator != null)
                _aggregator.RestoreFromSnapshot(snap.Aggregator);

            _evalCount = snap.EvalCount;

            if (snap.Pred4HDist != null)
                for (int i = 0; i < Math.Min(3, snap.Pred4HDist.Length); i++)
                    _pred4HDist[i] = snap.Pred4HDist[i];

            if (snap.Pred1HDist != null)
                for (int i = 0; i < Math.Min(3, snap.Pred1HDist.Length); i++)
                    _pred1HDist[i] = snap.Pred1HDist[i];
        }

        private async void SaveStateAsync(DateTime lastBarTime)
        {
            if (_persistence == null) return;

            try
            {
                var snapshot = ToSnapshot(lastBarTime);
                await _persistence.SaveAsync("DualRegime", KeyValue, snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DUAL-REGIME] Failed to persist state");
            }
        }

        #endregion

        #region Model Loading

        private DualRegimeModelArtifact TryLoadModelFromDb()
        {
            if (_persistence == null) return null;

            try
            {
                var entity = _persistence.LoadActiveModelAsync("DualRegime").Result;
                if (entity == null) return null;

                var artifact = DualRegimeModelArtifact.LoadFromDb(
                    entity.ManifestJson, entity.Model1hOnnx, entity.Model4hOnnx);

                _logger.LogInformation(
                    $"[DUAL-REGIME] Loaded model from DB: v{entity.Version}, trained {artifact.TrainedAt:yyyy-MM-dd}");

                return artifact;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DUAL-REGIME] Failed to load model from DB — falling back to filesystem");
                return null;
            }
        }

        #endregion

        #region Helpers

        private string ResolveArtifactDir()
        {
            // Check environment variable first
            var envDir = Environment.GetEnvironmentVariable("DUAL_REGIME_MODEL_DIR");
            if (!string.IsNullOrEmpty(envDir) && Directory.Exists(envDir))
                return envDir;

            // Default to Models/DualRegime relative to assembly
            var assemblyDir = AppDomain.CurrentDomain.BaseDirectory;
            var defaultDir = Path.Combine(assemblyDir, "Models", "DualRegime");
            if (Directory.Exists(defaultDir))
                return defaultDir;

            // Fallback to working directory
            return Path.Combine(Directory.GetCurrentDirectory(), "Models", "DualRegime");
        }

        private void LogRunParameters()
        {
            _logger.LogInformation("================================================================");
            _logger.LogInformation("  DUAL-REGIME BTC SWING STRATEGY");
            _logger.LogInformation("================================================================");
            _logger.LogInformation($"  RunType:              {_config.RunType}");
            _logger.LogInformation($"  StartBtcAmount:       {_config.StartBtcAmount}");
            _logger.LogInformation($"  Model trained:        {_artifact.TrainedAt:yyyy-MM-dd HH:mm}");
            _logger.LogInformation($"  R2 (1h/4h):           {_artifact.ValR2_1H:F3} / {_artifact.ValR2_4H:F3}");
            _logger.LogInformation($"  Sigma 1h/4h:          {_artifact.Sigma1H:F6} / {_artifact.Sigma4H:F6}");
            _logger.LogInformation("  -- Trend Strategy --");
            _logger.LogInformation($"  BE trigger/offset:    {_params.TrendBeTrigger * 100}% / {_params.TrendBeOffset * 100}%");
            _logger.LogInformation($"  Trail trigger/lock:   {_params.TrendTrailTrigger * 100}% / {_params.TrendTrailLockFraction * 100}%");
            _logger.LogInformation($"  Target vol mult:      {_params.TargetVolMultiplier}x sigma_4h_est");
            _logger.LogInformation($"  Capital risk:         {_params.TrendCapitalRisk * 100}% per trade");
            _logger.LogInformation("  -- Chop Strategy --");
            _logger.LogInformation($"  Stop mult:            {_params.ChopStopMult}x sigma_24");
            _logger.LogInformation($"  BE trigger/offset:    {_params.ChopBeTrigger * 100}% / {_params.ChopBeOffset * 100}%");
            _logger.LogInformation($"  Lock fraction:        {_params.ChopLockFraction * 100}%");
            _logger.LogInformation($"  Capital risk:         {_params.ChopCapitalRisk * 100}% per trade");
            _logger.LogInformation("  -- Shared --");
            _logger.LogInformation($"  Max hold:             {_params.MaxHoldBars} bars");
            _logger.LogInformation($"  Costs:                {_params.CostPerSide * 100}% + {_params.Slippage * 100}% per side");
            _logger.LogInformation($"  EV threshold:         {_params.EvThreshold * 100}%");
            _logger.LogInformation($"  Leverage:             {_params.Leverage}x");
            _logger.LogInformation("================================================================");
        }

        #endregion
    }
}
