using Binance;
using Binance.Client;
using CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

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

        private Symbol _symbol;

        // Strategy components
        private readonly IMarketStructureStrategy _regimeStrategy;
        private readonly IStrategy _setupStrategy;

        // Performance tracking for position sizing and circuit breaker
        public TradePerformanceTracker PerformanceTracker { get; private set; }

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

            // Pass logger to strategies that support it
            (_setupStrategy as RegimeBasedSetupStrategy)?.SetLogger(_logger);

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

            // Initialize performance tracker with starting equity
            decimal startEquity = (decimal)(Config.StartBtcAmount > 0 ? Config.StartBtcAmount : 10000);
            PerformanceTracker = new TradePerformanceTracker(startEquity);

            // Wire performance tracker and equity into setup strategy for position sizing
            if (_setupStrategy is RegimeBasedSetupStrategy setupStrategy)
            {
                setupStrategy.PerformanceTracker = PerformanceTracker;
                setupStrategy.Equity = startEquity;
            }

            LogRunParameters();
        }

        private void LogRunParameters()
        {
            _logger.LogInformation("================================================================");
            _logger.LogInformation("  RUN PARAMETERS");
            _logger.LogInformation("================================================================");

            // Database / run config
            _logger.LogInformation($"  RunType:            {Config.RunType}");
            _logger.LogInformation($"  Interval:           {Config.Interval}");
            _logger.LogInformation($"  From:               {Config.From:yyyy-MM-dd}");
            _logger.LogInformation($"  To:                 {Config.To:yyyy-MM-dd}");
            _logger.LogInformation($"  NoOfTrades:         {Config.NoOfTrades}");
            _logger.LogInformation($"  Risk:               {Config.Risk}");
            _logger.LogInformation($"  UseFixedAmount:     {Config.UseFixedAmount}");
            _logger.LogInformation($"  FixedAmount:        {Config.FixedAmount}");
            _logger.LogInformation($"  PercentDailyVolume: {Config.PercentDailyVolume}");
            _logger.LogInformation($"  StartBtcAmount:     {Config.StartBtcAmount}");
            _logger.LogInformation($"  CandlesToLoad:      {Config.NumberOfCandleSticksToLoad}");
            _logger.LogInformation($"  Increment:          {Config.Increment}");

            // 4H regime strategy parameters
            var regimeStrategy = _regimeStrategy as RegimeBasedMarketStructureStrategy;
            _logger.LogInformation("  -- 4H Regime Strategy --");
            _logger.LogInformation($"  EmaGradientPeriod:     5 (default)");
            _logger.LogInformation($"  GradientLookback:      3 (default)");
            _logger.LogInformation($"  TrendThreshold:        0.05 (default)");
            _logger.LogInformation($"  VolHighPercentile:     75");
            _logger.LogInformation($"  VolLowPercentile:      25");

            // 15M setup strategy parameters
            _logger.LogInformation("  -- 15M Setup Strategy --");
            _logger.LogInformation($"  MinRiskRewardRatio:    1.5");
            _logger.LogInformation($"  MACD:                  12/26/9");
            _logger.LogInformation($"  BollingerBands:        20 period, 2 stddev");
            _logger.LogInformation($"  RSI:                   14 period, oversold=35, overbought=65");
            _logger.LogInformation($"  ZoneProximity:         2x ATR");

            // 1M entry strategy parameters
            _logger.LogInformation("  -- 1M Entry Strategy --");
            _logger.LogInformation($"  Confluence:            min score 2.0 (7 signals: StochRsi/Pattern/Volume/Zone/MACD/EMA/Momentum)");
            _logger.LogInformation($"  StochRsi:              14/14/3/3, oversold=20, overbought=80");
            _logger.LogInformation($"  MomentumLookback:      15 bars (extended from 5)");

            // 1M exit strategy parameters
            _logger.LogInformation("  -- 1M Exit Strategy --");
            _logger.LogInformation($"  TrailingStop:          adaptive: 0.3R=1.5xATR, 1R=2x, 1.5R=2.5x, 2R=3x, 3R=3.5x");
            _logger.LogInformation($"  ScaleOut:              1R=33%+BE_SL, 2R=33%, 3R/exhausted=rest (MARKET orders)");
            _logger.LogInformation($"  FixedTarget:           trail after 50% progress (1.5-2x ATR), full exit at 80%+exhausted");
            _logger.LogInformation($"  TimeBased:             60 bars + <0.1% move + contracting vol (was 20 bars/0.5%)");
            _logger.LogInformation($"  StructureBreak:        2 confirming bars + 1.3x volume (was 1 bar, no volume)");

            // Risk management
            _logger.LogInformation("  -- Risk Management --");
            _logger.LogInformation($"  PositionSizing:        1% equity risk per trade, volatility-adjusted");
            _logger.LogInformation($"  AntiMartingale:        0.75x after 1L, 0.50x after 2L, 0.25x after 3L");
            _logger.LogInformation($"  CircuitBreaker:        3% daily loss, 10% peak drawdown");
            _logger.LogInformation($"  RegimeScaling:         1.3x strong trend, 0.7x range, 0.7x high vol");
            _logger.LogInformation($"  QualityFilter:         min composite score 0.55 (raised from 0.50)");

            _logger.LogInformation("================================================================");
        }

        public void Subscribe(Symbol symbol, IMarketDataEvents marketData)
        {
            _symbol = symbol;

            // Pass symbol-specific order constraints to the setup strategy for position sizing
            if (_setupStrategy is RegimeBasedSetupStrategy setupStrategy)
            {
                setupStrategy.SymbolConstraints = new SymbolConstraints
                {
                    MinQuantity = symbol.Quantity.Minimum,
                    MaxQuantity = symbol.Quantity.Maximum,
                    StepSize = symbol.Quantity.Increment,
                    MinNotional = symbol.NotionalMinimumValue,
                    TickSize = symbol.Price.Increment
                };
                _logger.LogInformation(
                    $"Symbol constraints: MinQty={symbol.Quantity.Minimum} MaxQty={symbol.Quantity.Maximum} " +
                    $"Step={symbol.Quantity.Increment} MinNotional={symbol.NotionalMinimumValue} Tick={symbol.Price.Increment}");
            }

            // 4H for regime detection
            marketData.InitialDataLoadSubscribe(symbol, CandlestickInterval.Hours_4, ProcessHistoricData4H);
            marketData.InitialDataStreamSubscribe(symbol, CandlestickInterval.Hours_4, ProcessLiveCandle4H);

            // 15M for setup detection
            marketData.InitialDataLoadSubscribe(symbol, CandlestickInterval.Minutes_15, ProcessHistoricData15M);
            marketData.InitialDataStreamSubscribe(symbol, CandlestickInterval.Minutes_15, ProcessLiveCandle15M);


            _logger.LogInformation($"Subscribed to {symbol} on 4H, 15M, and 1M timeframes");
        }

        #region Historic Data Loading

        private void ProcessHistoricData4H(IEnumerable<Candlestick> candlesticks)
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

        private void ProcessHistoricData15M(IEnumerable<Candlestick> candlesticks)
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
        private void ProcessLiveCandle4H(CandlestickEventArgs args)
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
        private void ProcessLiveCandle15M(CandlestickEventArgs args)
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

                    // Pass logger to execution strategy and its entry/exit strategies
                    (executionStrategy as RegimeBasedExecutionStrategy)?.SetLogger(_logger);

                    // Submit trade request to RequestTracker for execution by TradeMonitor
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

                        RequestTracker.Instance.Add(
                            args.Candlestick.Symbol,
                            new CryptoTrading.App.Algorithm.TradeRequest(strategyResult, executionStrategy, _symbol, args.Candlestick.CloseTime),
                            KeyValue);
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