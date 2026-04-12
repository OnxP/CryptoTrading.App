using Binance;
using Binance.Client;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// HTF RSI Direction + LTF Volatility Expansion algorithm.
    ///
    /// Uses two timeframes working together:
    ///   - 4H RSI determines trade direction (above 65 = long, below 35 = short)
    ///   - 15M ATR expansion determines entry timing (20%+ increase vs 20 candles ago)
    ///
    /// All data comes from a single 15M subscription. 4H candles are aggregated
    /// internally from 15M data using the FourHourCandleAggregator.
    ///
    /// Position management: 1.5 × ATR SL/TP (1:1 R:R), trailing stop at 1.5R,
    /// time stop at 16 candles, 8-candle min gap, 3-loss cooldown.
    /// </summary>
    public class HtfRsiVolExpansionAlgorithm : IAlgorithm
    {
        private readonly ILogger<HtfRsiVolExpansionAlgorithm> _logger;

        // Quote hubs
        private readonly QuoteHub<IQuote> _quoteHub15M;
        private readonly QuoteHub<IQuote> _quoteHub4H;

        // Streaming indicator hubs
        private AtrHub<IQuote> _atrHub15M;
        private EmaHub<IQuote> _ema4H8;
        private EmaHub<IQuote> _ema4H21;

        // Aggregation
        private readonly FourHourCandleAggregator _aggregator;

        // Shared state
        private HtfRsiTradingState _tradingState;

        // Symbol / config
        private Symbol _symbol;
        private IConfig _config;
        private decimal _startBtcAmount;
        private bool _equityInitialized;

        public string KeyValue { get; set; } = "1";
        public IConfig Config => _config;

        // Strategy parameters
        private const int HtfRsiPeriod = 14;
        private const double HtfRsiLongThreshold = 65.0;
        private const double HtfRsiShortThreshold = 35.0;
        private const int LtfAtrPeriod = 14;
        private const decimal VolExpansionRatio = 1.2m;
        private const int VolExpansionLookback = 20;
        private const decimal SlTpAtrMultiplier = 1.5m;
        private const int Leverage = 5;

        public HtfRsiVolExpansionAlgorithm(
            ILogger<HtfRsiVolExpansionAlgorithm> logger)
        {
            _logger = logger;
            _quoteHub15M = new QuoteHub<IQuote>(500);
            _quoteHub4H = new QuoteHub<IQuote>(200);
            _aggregator = new FourHourCandleAggregator();
        }

        public void Configure(IConfig config)
        {
            _config = config;
            _startBtcAmount = (decimal)(config.StartBtcAmount > 0 ? config.StartBtcAmount : 1);
            _tradingState = new HtfRsiTradingState(_startBtcAmount);

            LogRunParameters();
        }

        public void Subscribe(Symbol symbol, IMarketDataEvents marketData)
        {
            _symbol = symbol;

            // Subscribe to 15M only - we aggregate 4H internally
            marketData.InitialDataLoadSubscribe(symbol, CandlestickInterval.Minutes_15, ProcessHistoricData15M);
            marketData.InitialDataStreamSubscribe(symbol, CandlestickInterval.Minutes_15, ProcessLiveCandle15M);

            _logger.LogInformation($"[HTF-RSI] Subscribed to {symbol} on 15M (4H aggregated internally)");
        }

        #region Historic Data Loading

        private void ProcessHistoricData15M(IEnumerable<Candlestick> candlesticks)
        {
            var quotes15M = new List<Quote>();
            foreach (var candle in candlesticks)
            {
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
                quotes15M.Add(quote);
            }

            // Initialize streaming ATR on 15M
            _atrHub15M = _quoteHub15M.ToAtr(LtfAtrPeriod);

            // Aggregate 15M → 4H
            var candles4H = _aggregator.AggregateHistoric(quotes15M);
            foreach (var c in candles4H)
                _quoteHub4H.Add(c);

            // Initialize 4H EMAs for probability scorer
            _ema4H8 = _quoteHub4H.ToEma(8);
            _ema4H21 = _quoteHub4H.ToEma(21);

            // Log initial state
            var rsiResults = _quoteHub4H.Quotes.Count >= HtfRsiPeriod + 1
                ? _quoteHub4H.Quotes.ToRsi(HtfRsiPeriod)
                : null;
            var lastRsi = rsiResults?.LastOrDefault()?.Rsi;

            _logger.LogInformation(
                $"[HTF-RSI] Loaded {candlesticks.Count()} 15M candles → {candles4H.Count} 4H candles | " +
                $"4H RSI: {lastRsi?.ToString("F1") ?? "N/A"} | " +
                $"15M ATR: {_atrHub15M.Results.LastOrDefault()?.Atr?.ToString("F2") ?? "N/A"}");
        }

        #endregion

        #region Live Data Processing

        private void ProcessLiveCandle15M(CandlestickEventArgs args)
        {
            try
            {
                if (!args.IsFinal) return;

                var quote = new Quote
                {
                    Timestamp = args.Candlestick.CloseTime,
                    Open = args.Candlestick.Open,
                    High = args.Candlestick.High,
                    Low = args.Candlestick.Low,
                    Close = args.Candlestick.Close,
                    Volume = args.Candlestick.Volume
                };
                _quoteHub15M.Add(quote);

                var ts = args.Candlestick.CloseTime.ToString("yyyy-MM-dd HH:mm");

                // Convert BTC equity to USDT on first price
                if (!_equityInitialized && args.Candlestick.Close > 0)
                {
                    decimal equityUsdt = _startBtcAmount * args.Candlestick.Close;
                    _tradingState = new HtfRsiTradingState(equityUsdt);
                    _equityInitialized = true;
                    _logger.LogInformation(
                        $"[HTF-RSI EQUITY] {_startBtcAmount} BTC × {args.Candlestick.Close:F2} = {equityUsdt:F2} USDT");
                }

                // Advance trading state counters
                _tradingState.OnNewCandle();

                // Aggregate 15M → 4H
                var completed4H = _aggregator.TryAggregate(quote);
                if (completed4H != null)
                {
                    _quoteHub4H.Add(completed4H);
                    var rsiResults = _quoteHub4H.Quotes.ToRsi(HtfRsiPeriod);
                    var lastRsi = rsiResults.LastOrDefault()?.Rsi;
                    _logger.LogInformation(
                        $"[HTF-RSI 4H {ts}] New 4H candle | RSI: {lastRsi?.ToString("F1") ?? "N/A"} | " +
                        $"Close: {completed4H.Close:F2}");
                }

                // Check entry conditions
                EvaluateEntry(args.Candlestick, ts);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[HTF-RSI] Error processing 15M candle at {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}");
            }
        }

        private void EvaluateEntry(Candlestick candle, string ts)
        {
            // Pre-checks
            if (!_tradingState.CanTrade)
            {
                _logger.LogDebug($"[HTF-RSI {ts}] Cannot trade: {_tradingState.GetStatus()}");
                return;
            }

            // Need enough data for indicators
            var atrCount = _atrHub15M?.Results?.Count ?? 0;
            var htfCount = _quoteHub4H.Quotes.Count;
            if (htfCount < HtfRsiPeriod + 2 || atrCount < VolExpansionLookback + LtfAtrPeriod + 1)
            {
                _logger.LogInformation($"[HTF-RSI {ts}] Insufficient data: 4H={htfCount} (need {HtfRsiPeriod + 2}), ATR={atrCount} (need {VolExpansionLookback + LtfAtrPeriod + 1})");
                return;
            }

            // 1. Get 4H RSI
            var rsiResults = _quoteHub4H.Quotes.ToRsi(HtfRsiPeriod);
            var currentRsi = rsiResults.Last()?.Rsi;
            if (!currentRsi.HasValue) return;

            // 2. Determine direction
            TradeDirection direction;
            if (currentRsi.Value > HtfRsiLongThreshold)
                direction = TradeDirection.Long;
            else if (currentRsi.Value < HtfRsiShortThreshold)
                direction = TradeDirection.Short;
            else
                return; // RSI in no-trade zone (35-65)

            // 3. Get 15M ATR and check vol expansion
            var atrResults = _atrHub15M.Results;
            var currentAtrResult = atrResults.Last();
            if (!currentAtrResult.Atr.HasValue) return;

            var currentAtr = (decimal)currentAtrResult.Atr.Value;

            // ATR from 20 candles ago
            int lookbackIndex = atrResults.Count - 1 - VolExpansionLookback;
            if (lookbackIndex < 0) return;
            var pastAtrResult = atrResults[lookbackIndex];
            if (!pastAtrResult.Atr.HasValue || pastAtrResult.Atr.Value <= 0) return;

            var pastAtr = (decimal)pastAtrResult.Atr.Value;
            var expansionRatio = currentAtr / pastAtr;

            _logger.LogInformation(
                $"[HTF-RSI {ts}] BIAS {direction} | RSI:{currentRsi.Value:F1} | " +
                $"ATR:{currentAtr:F2} vs {pastAtr:F2} (20 ago) | VolExp:{expansionRatio:F2} | " +
                $"Need:{VolExpansionRatio} | {_tradingState.GetStatus()}");

            if (expansionRatio < VolExpansionRatio)
                return;

            // All conditions met - create setup
            var entryPrice = candle.Close;
            var initialRisk = currentAtr * SlTpAtrMultiplier;

            decimal stopLoss, takeProfit;
            if (direction == TradeDirection.Long)
            {
                stopLoss = entryPrice - initialRisk;
                takeProfit = entryPrice + initialRisk;
            }
            else
            {
                stopLoss = entryPrice + initialRisk;
                takeProfit = entryPrice - initialRisk;
            }

            // Position sizing: full equity × leverage / price
            var equity = _tradingState.CurrentEquity;
            var notional = equity * Leverage;
            var quantity = notional / entryPrice;

            // Respect symbol constraints
            if (_symbol?.Quantity?.Minimum > 0 && quantity < _symbol.Quantity.Minimum)
            {
                _logger.LogWarning($"[HTF-RSI {ts}] Quantity {quantity:F6} below minimum {_symbol.Quantity.Minimum}. Skipping.");
                return;
            }
            if (_symbol?.Quantity?.Increment > 0)
            {
                quantity = Math.Floor(quantity / _symbol.Quantity.Increment) * _symbol.Quantity.Increment;
            }

            // Get 15M RSI for probability score
            double rsi15M = 50;
            try
            {
                var rsi15MResults = _quoteHub15M.Quotes.ToRsi(14);
                rsi15M = rsi15MResults.Last()?.Rsi ?? 50;
            }
            catch { }

            // Compute 20-period volume average
            decimal avgVolume20 = 0;
            var quotes15M = _quoteHub15M.Quotes;
            if (quotes15M.Count >= 20)
            {
                avgVolume20 = quotes15M.Skip(quotes15M.Count - 20).Average(q => (decimal)q.Volume);
            }

            // Probability score
            int score = ProbabilityScorer.Score(
                htfRsi: currentRsi.Value,
                volExpansionRatio: (double)expansionRatio,
                rsi15M: rsi15M,
                direction: direction,
                candleOpen: candle.Open,
                candleClose: candle.Close,
                candleVolume: candle.Volume,
                avgVolume20: avgVolume20,
                ema4H8: _ema4H8?.Results.LastOrDefault()?.Value,
                ema4H21: _ema4H21?.Results.LastOrDefault()?.Value,
                recentWinRate: _tradingState.RecentWinRate);

            // Create setup
            var setup = new HtfRsiVolExpansionSetup
            {
                Direction = direction,
                EntryPrice = entryPrice,
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                AtrAtEntry = currentAtr,
                InitialRisk = initialRisk,
                HtfRsi = currentRsi.Value,
                VolExpansionRatio = (double)expansionRatio,
                Rsi15M = rsi15M,
                ProbabilityScore = score,
                EntryTime = candle.CloseTime,
                Quantity = quantity,
                Leverage = Leverage
            };

            // Create execution strategy
            var executionStrategy = new HtfRsiVolExpansionExecutionStrategy(setup, _tradingState);
            executionStrategy.SetLogger(_logger);

            // Create strategy result
            var strategyResult = new HtfRsiVolExpansionStrategyResult
            {
                PostTrade = true,
                Amount = quantity * entryPrice, // USDT notional
                Leverage = Leverage,
                OrderSide = direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell,
                Setup = setup
            };

            // Mark as in position
            _tradingState.IsInPosition = true;

            // Fire setup
            _logger.LogInformation(
                $"[HTF-RSI {ts}] SETUP FIRED: {direction} | Price:{entryPrice:F2} | " +
                $"SL:{stopLoss:F2} | TP:{takeProfit:F2} | ATR:{currentAtr:F2} | " +
                $"VolExp:{expansionRatio:F2} | 4H RSI:{currentRsi.Value:F1} | " +
                $"15M RSI:{rsi15M:F1} | Score:{score} | Qty:{quantity:F6} | " +
                $"Equity:{equity:F2} | {_tradingState.GetStatus()}");

            RequestTracker.Instance.Add(
                candle.Symbol,
                new TradeRequest(strategyResult, executionStrategy, _symbol, candle.CloseTime),
                KeyValue);
        }

        #endregion

        #region Logging

        private void LogRunParameters()
        {
            _logger.LogInformation("================================================================");
            _logger.LogInformation("  HTF RSI + LTF VOL EXPANSION STRATEGY");
            _logger.LogInformation("================================================================");
            _logger.LogInformation($"  RunType:              {_config.RunType}");
            _logger.LogInformation($"  Interval:             {_config.Interval}");
            _logger.LogInformation($"  StartBtcAmount:       {_config.StartBtcAmount}");
            _logger.LogInformation("  -- Higher Timeframe (4H from 15M aggregation) --");
            _logger.LogInformation($"  HTF RSI Period:       {HtfRsiPeriod}");
            _logger.LogInformation($"  HTF RSI Long:         > {HtfRsiLongThreshold}");
            _logger.LogInformation($"  HTF RSI Short:        < {HtfRsiShortThreshold}");
            _logger.LogInformation($"  HTF Multiplier:       16 (15M → 4H)");
            _logger.LogInformation("  -- Lower Timeframe (15M) --");
            _logger.LogInformation($"  LTF ATR Period:       {LtfAtrPeriod}");
            _logger.LogInformation($"  Vol Expansion Ratio:  {VolExpansionRatio}");
            _logger.LogInformation($"  Vol Expansion Lookback: {VolExpansionLookback} candles");
            _logger.LogInformation("  -- Risk Management --");
            _logger.LogInformation($"  SL/TP:                {SlTpAtrMultiplier} × ATR (1:1 R:R)");
            _logger.LogInformation($"  Trailing Activation:  1.5R");
            _logger.LogInformation($"  Trailing Distance:    1.0 × ATR");
            _logger.LogInformation($"  Max Hold:             16 candles (4 hours)");
            _logger.LogInformation($"  Min Gap:              8 candles (2 hours)");
            _logger.LogInformation($"  Cooldown:             3 consecutive losses → 16 candle pause");
            _logger.LogInformation($"  Leverage:             {Leverage}x (fixed)");
            _logger.LogInformation("  -- Probability Score --");
            _logger.LogInformation("  7 factors, 0-100, logged only (no sizing impact)");
            _logger.LogInformation("================================================================");
        }

        #endregion
    }
}
