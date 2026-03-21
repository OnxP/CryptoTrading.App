using CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using IAlgorithm = CryptoTrading.App.Core.Strategy.IAlgorithm;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Multi-timeframe algorithm using regime-based trading strategy.
    /// Implements IAlgorithm from Core.
    /// 
    /// Architecture:
    /// - 4H Timeframe: Regime detection (trend direction, volatility, allowed setups)
    /// - 15M Timeframe: Setup evaluation (entry zone, stop loss, take profit, R:R)
    /// - 1M Timeframe: Execution (precise entry timing, position management)
    /// 
    /// Flow:
    /// 1. On 4H candle close: Update market regime
    /// 2. On 15M candle close: Check for valid setups
    /// 3. On 1M candle close: Execute entry/exit
    /// </summary>
    public class RegimeBasedMultiTimeFrameAlgorithm : IAlgorithm
    {
        private readonly ILogger<RegimeBasedMultiTimeFrameAlgorithm> _logger;
        private readonly int _candlesToKeep = 200;

        // Quote hubs for each timeframe
        private readonly QuoteHub<IQuote> _quoteHub4H;
        private readonly QuoteHub<IQuote> _quoteHub15M;
        private readonly QuoteHub<IQuote> _quoteHub1M;

        private ExchangeSymbol _symbol;

        // Strategy components
        private readonly IMarketStructureStrategy _regimeStrategy;
        private readonly IStrategy _setupStrategy;

        // State
        private IMarketStructureResult _currentRegime;
        private IExecutionStrategy _activeExecutionStrategy;
        private SetupResult _activeSetup;
        private bool _isInPosition;

        public string KeyValue { get; set; } = "1";
        public IConfig Config { get; private set; }

        public RegimeBasedMultiTimeFrameAlgorithm(
            IMarketStructureStrategy regimeStrategy,
            IStrategy setupStrategy,
            ILogger<RegimeBasedMultiTimeFrameAlgorithm> logger)
        {
            _regimeStrategy = regimeStrategy ?? new RegimeBasedMarketStructureStrategy();
            _setupStrategy = setupStrategy ?? new RegimeBasedSetupStrategy();
            _logger = logger;

            _quoteHub4H = new QuoteHub<IQuote>(_candlesToKeep);
            _quoteHub15M = new QuoteHub<IQuote>(_candlesToKeep);
            _quoteHub1M = new QuoteHub<IQuote>(_candlesToKeep);
        }

        public RegimeBasedMultiTimeFrameAlgorithm(
            IMarketStructureStrategy regimeStrategy,
            IStrategy setupStrategy,
            ILogger<RegimeBasedMultiTimeFrameAlgorithm> logger,
            IKey key) : this(regimeStrategy, setupStrategy, logger)
        {
            KeyValue = key.KeyValue;
        }

        public void Configure(IConfig config)
        {
            Config = config;
        }

        public void Subscribe(ExchangeSymbol symbol, IMarketDataEvents marketData)
        {
            _symbol = symbol;

            // 4H for regime detection
            marketData.InitialDataLoadSubscribe(symbol.Ticker, CandleInterval.Hour_4, ProcessHistoricData4H);
            marketData.InitialDataStreamSubscribe(symbol.Ticker, CandleInterval.Hour_4, ProcessLiveCandle4H);

            // 15M for setup detection
            marketData.InitialDataLoadSubscribe(symbol.Ticker, CandleInterval.Minute_15, ProcessHistoricData15M);
            marketData.InitialDataStreamSubscribe(symbol.Ticker, CandleInterval.Minute_15, ProcessLiveCandle15M);


            _logger.LogInformation($"Subscribed to {symbol} on 4H, 15M, and 1M timeframes");
        }

        #region Historic Data Loading

        private void ProcessHistoricData4H(IEnumerable<ExchangeCandlestick> candlesticks)
        {
            foreach (var candle in candlesticks)
            {
                _quoteHub4H.Add(new Quote
                {
                    Timestamp = candle.CloseTime,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    Volume = candle.Volume
                });
            }

            _regimeStrategy.SetQuotes(_quoteHub4H);
            _currentRegime = _regimeStrategy.Calculate();

            _logger.LogInformation(
                $"Loaded {candlesticks.Count()} 4H candles for {candlesticks.FirstOrDefault()?.Symbol}. " +
                $"Initial regime: {_currentRegime?.MarketRegime}");
        }

        private void ProcessHistoricData15M(IEnumerable<ExchangeCandlestick> candlesticks)
        {
            foreach (var candle in candlesticks)
            {
                _quoteHub15M.Add(new Quote
                {
                    Timestamp = candle.CloseTime,
                    Open = candle.Open,
                    High = candle.High,
                    Low = candle.Low,
                    Close = candle.Close,
                    Volume = candle.Volume
                });
            }

            _setupStrategy.SetQuotes(_quoteHub15M);
            _logger.LogInformation($"Loaded {candlesticks.Count()} 15M candles");
        }

        #endregion

        #region Live Data Processing

        /// <summary>
        /// Process 4H candle - Update market regime
        /// </summary>
        private void ProcessLiveCandle4H(ExchangeCandlestickEvent args)
        {
            try
            {
                if (!args.IsFinal) return;

                _quoteHub4H.Add(new Quote
                {
                    Timestamp = args.Candlestick.CloseTime,
                    Open = args.Candlestick.Open,
                    High = args.Candlestick.High,
                    Low = args.Candlestick.Low,
                    Close = args.Candlestick.Close,
                    Volume = args.Candlestick.Volume
                });

                var previousRegime = _currentRegime?.MarketRegime;
                _currentRegime = _regimeStrategy.Calculate();

                var regimeResult = _currentRegime as RegimeBasedMarketStructureResult;

                var ts4H = args.Candlestick.CloseTime.ToString("yyyy-MM-dd HH:mm");
                _logger.LogInformation(
                    $"[4H {ts4H}] Regime: {_currentRegime.MarketRegime} | " +
                    $"Vol: {regimeResult?.VolatilityRegime} | " +
                    $"Dir: {regimeResult?.AllowedDirection} | " +
                    $"Conf: {regimeResult?.Confidence:P0}");

                if (regimeResult != null)
                {
                    _logger.LogInformation(
                        $"[4H {ts4H}] EMA Grad: {regimeResult.EmaGradientNormalized:F3} | " +
                        $"Zones: {regimeResult.ActiveZones?.Count ?? 0} | " +
                        $"Setups: {string.Join(", ", regimeResult.AllowedSetups)}");
                    _logger.LogDebug($"[4H {ts4H}] {regimeResult.Reasoning}");
                }

                // Invalidate setup if regime changed
                if (previousRegime != _currentRegime.MarketRegime && _activeSetup != null)
                {
                    if (regimeResult != null && !regimeResult.AllowedSetups.Contains(_activeSetup.SetupType))
                    {
                        _logger.LogInformation($"[4H {ts4H}] Setup invalidated by regime change");
                        _activeSetup = null;
                        _activeExecutionStrategy = null;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error processing 4H candle at {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}");
            }
        }

        /// <summary>
        /// Process 15M candle - Evaluate setups
        /// </summary>
        private void ProcessLiveCandle15M(ExchangeCandlestickEvent args)
        {
            try
            {
                if (!args.IsFinal) return;

                _quoteHub15M.Add(new Quote
                {
                    Timestamp = args.Candlestick.CloseTime,
                    Open = args.Candlestick.Open,
                    High = args.Candlestick.High,
                    Low = args.Candlestick.Low,
                    Close = args.Candlestick.Close,
                    Volume = args.Candlestick.Volume
                });

                var ts15M = args.Candlestick.CloseTime.ToString("yyyy-MM-dd HH:mm");

                if (_currentRegime == null)
                {
                    _logger.LogWarning($"[15M {ts15M}] No regime, skipping");
                    return;
                }

                // Don't look for new setups if in position
                if (_isInPosition)
                {
                    _logger.LogDebug($"[15M {ts15M}] In position, skipping setup eval");
                    return;
                }

                var (strategyResult, executionStrategy) = _setupStrategy.Calculate(_currentRegime);

                if (strategyResult.PostTrade && executionStrategy != null)
                {
                    var regimeResult = strategyResult as RegimeBasedStrategyResult;
                    _activeSetup = regimeResult?.Setup;
                    _activeExecutionStrategy = executionStrategy;

                    // Setup detected — 1M execution layer will handle precise entry and trade submission
                    if (_activeSetup != null)
                    {
                        _logger.LogInformation(
                            $"[15M {ts15M}] SETUP: {_activeSetup.SetupType} {_activeSetup.Direction}");
                        _logger.LogInformation(
                            $"[15M {ts15M}] Entry Zone: [{_activeSetup.EntryZoneLow:F2} - {_activeSetup.EntryZoneHigh:F2}] | " +
                            $"ZoneTrade: {_activeSetup.IsZoneTrade}");
                        _logger.LogInformation(
                            $"[15M {ts15M}] Stop: {_activeSetup.StopLoss:F2} | TP: {_activeSetup.TakeProfit:F2} | R:R: {_activeSetup.RiskRewardRatio:F2}");
                        _logger.LogInformation(
                            $"[15M {ts15M}] Entry: {_activeSetup.RecommendedEntryStrategy} | Exit: {_activeSetup.RecommendedExitStrategy} | " +
                            $"Conf: {_activeSetup.Confidence:P0}");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error processing 15M candle at {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}");
            }
        }

        #endregion

        #region Manual Controls

        public void ForceClosePosition(string reason)
        {
            if (!_isInPosition) return;
            _logger.LogWarning($"[MANUAL] Force close: {reason}");
            _isInPosition = false;
            _activeSetup = null;
            _activeExecutionStrategy = null;
        }

        public void ClearSetup()
        {
            _logger.LogWarning("[MANUAL] Clearing setup");
            _activeSetup = null;
            _activeExecutionStrategy = null;
        }

        public string GetState()
        {
            var regime = _currentRegime as RegimeBasedMarketStructureResult;
            return $"Regime: {_currentRegime?.MarketRegime} | " +
                   $"Vol: {regime?.VolatilityRegime} | " +
                   $"Setup: {_activeSetup?.SetupType.ToString() ?? "None"} | " +
                   $"InPos: {_isInPosition}";
        }

        #endregion
    }
}