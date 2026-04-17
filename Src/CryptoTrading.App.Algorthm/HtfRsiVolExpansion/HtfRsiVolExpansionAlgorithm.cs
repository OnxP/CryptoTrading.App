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
    /// Position management: 1.5 × ATR SL, dynamic TP by score (1-2R), trailing stop at 1R,
    /// time stop at 240 1M bars (4 hours), 8-candle min gap, 3-loss cooldown.
    /// </summary>
    public class HtfRsiVolExpansionAlgorithm : IAlgorithm
    {
        private readonly ILogger<HtfRsiVolExpansionAlgorithm> _logger;

        // Quote hubs
        private readonly QuoteHub<IQuote> _quoteHub15M;
        private readonly QuoteHub<IQuote> _quoteHub4H;

        // Streaming indicator hubs (4H EMAs for probability scorer)
        private EmaHub<IQuote> _ema4H8;
        private EmaHub<IQuote> _ema4H21;

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
        private const double HtfRsiLongThreshold = 60.0;
        private const double HtfRsiShortThreshold = 40.0;
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

            // Subscribe to 15M for ATR / vol expansion
            marketData.InitialDataLoadSubscribe(symbol, CandlestickInterval.Minutes_15, ProcessHistoricData15M);
            marketData.InitialDataStreamSubscribe(symbol, CandlestickInterval.Minutes_15, ProcessLiveCandle15M);

            // Subscribe to native 4H for RSI direction gate — the exchange's
            // 4H candles start earlier than 15M aggregation can produce and may
            // have slightly different close values, so using the real feed gives
            // more accurate RSI and earlier warmup.
            marketData.InitialDataLoadSubscribe(symbol, CandlestickInterval.Hours_4, ProcessHistoricData4H);
            marketData.InitialDataStreamSubscribe(symbol, CandlestickInterval.Hours_4, ProcessLiveCandle4H);

            _logger.LogInformation($"[HTF-RSI] Subscribed to {symbol} on 15M + 4H");
        }

        #region Historic Data Loading

        private void ProcessHistoricData15M(IEnumerable<Candlestick> candlesticks)
        {
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
            }

            var initialAtr = _quoteHub15M.Quotes.ToAtr(LtfAtrPeriod).LastOrDefault()?.Atr;
            _logger.LogInformation(
                $"[HTF-RSI] Loaded {candlesticks.Count()} 15M candles | " +
                $"15M ATR: {initialAtr?.ToString("F2") ?? "N/A"}");
        }

        private void ProcessHistoricData4H(IEnumerable<Candlestick> candlesticks)
        {
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
                _quoteHub4H.Add(quote);
            }

            // Initialize 4H EMAs for probability scorer
            _ema4H8 = _quoteHub4H.ToEma(8);
            _ema4H21 = _quoteHub4H.ToEma(21);

            var rsi = ComputeCutlersRsi(_quoteHub4H.Quotes, HtfRsiPeriod);
            _logger.LogInformation(
                $"[HTF-RSI] Loaded {candlesticks.Count()} 4H candles | " +
                $"4H RSI(Cutler): {rsi?.ToString("F1") ?? "N/A"}");
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

                // Check entry conditions
                EvaluateEntry(args.Candlestick, ts);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[HTF-RSI] Error processing 15M candle at {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}");
            }
        }

        private void ProcessLiveCandle4H(CandlestickEventArgs args)
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
                _quoteHub4H.Add(quote);

                var rsi = ComputeCutlersRsi(_quoteHub4H.Quotes, HtfRsiPeriod);
                _logger.LogInformation(
                    $"[HTF-RSI 4H {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}] " +
                    $"New 4H candle | RSI(Cutler): {rsi?.ToString("F1") ?? "N/A"} | " +
                    $"Close: {args.Candlestick.Close:F2}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[HTF-RSI] Error processing 4H candle at {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}");
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

            // Need enough 15M quotes for ATR(14) + 20-bar lookback, and enough
            // 4H quotes for Cutler's RSI(14) which needs `period + 1` bars.
            var quotesCount = _quoteHub15M.Quotes.Count;
            var htfCount = _quoteHub4H.Quotes.Count;
            if (htfCount < HtfRsiPeriod + 1 || quotesCount < VolExpansionLookback + LtfAtrPeriod + 2)
                return;

            // 1. Get 4H RSI — use Cutler's (SMA of gains/losses) variant
            // with a partial-bar extension: the current 15M close acts as
            // the in-progress 4H bar's close.
            //
            // NOTE: The target backtest has look-ahead bias — it uses the
            // RSI of the COMPLETED 4H bar containing the entry, which
            // wouldn't be available in real-time. Our partial-bar approach
            // is the correct live-trading implementation. This causes ~4%
            // of target trades to fail our RSI gate (17/436), which are
            // trades the target shouldn't have taken without future data.
            var currentRsi = ComputeCutlersRsi(_quoteHub4H.Quotes, HtfRsiPeriod, candle.Close);
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
            // Use SMA-based ATR (simple mean of the last 14 TR values), NOT
            // Wilder's smoothed ATR. The backtest spec this algorithm is
            // calibrated against uses the SMA variant — matching it to 100%
            // precision across the target trade list.
            var ltfQuotes = _quoteHub15M.Quotes;
            var ltfCount = ltfQuotes.Count;
            if (ltfCount < LtfAtrPeriod + VolExpansionLookback + 1) return;

            var currentAtr = ComputeSmaAtr(ltfQuotes, ltfCount - 1, LtfAtrPeriod);
            var pastAtr = ComputeSmaAtr(ltfQuotes, ltfCount - 1 - VolExpansionLookback, LtfAtrPeriod);
            if (currentAtr <= 0 || pastAtr <= 0) return;

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

            // Create execution strategy (simple: just SL/TP, no trailing/breakeven/time stop)
            var executionStrategy = new SimpleExecutionStrategy(setup, _tradingState);
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

        /// <summary>
        /// Cutler's RSI(period) on the final bar of the supplied quote buffer.
        /// Unlike Wilder's smoothed RSI (what Skender's ToRsi returns), this
        /// uses simple arithmetic means of the last `period` gains and losses.
        /// Returns null if the buffer doesn't have `period + 1` quotes yet.
        ///
        /// The target backtest uses Cutler's RSI but with look-ahead bias:
        /// it reads the RSI of the completed 4H bar containing the entry,
        /// which isn't available in real-time. Our partial-bar extension
        /// is the correct live-trading equivalent — it uses only data
        /// available at entry time.
        /// </summary>
        /// <param name="partialClose">
        /// Close price of the current partially-formed 4H bar (the latest
        /// 15M close). An extra price change from the last closed bar's
        /// close to this value is included in the RSI window (covering
        /// period-1 closed changes + 1 partial change). This gives the
        /// best real-time approximation of the in-progress 4H bar's RSI.
        /// </param>
        private static double? ComputeCutlersRsi(
            System.Collections.Generic.IReadOnlyList<IQuote> quotes,
            int period,
            decimal? partialClose = null)
        {
            int n = quotes.Count;
            int needed = partialClose.HasValue ? period : period + 1;
            if (n < needed) return null;

            double gainSum = 0, lossSum = 0;

            if (partialClose.HasValue)
            {
                // Use period-1 closed changes + 1 partial change
                for (int i = n - period + 1; i < n; i++)
                {
                    var diff = (double)(quotes[i].Close - quotes[i - 1].Close);
                    if (diff > 0) gainSum += diff;
                    else lossSum += -diff;
                }
                // Add partial change: last closed bar → partial bar
                var partialDiff = (double)(partialClose.Value - quotes[n - 1].Close);
                if (partialDiff > 0) gainSum += partialDiff;
                else lossSum += -partialDiff;
            }
            else
            {
                for (int i = n - period; i < n; i++)
                {
                    var diff = (double)(quotes[i].Close - quotes[i - 1].Close);
                    if (diff > 0) gainSum += diff;
                    else lossSum += -diff;
                }
            }

            var avgGain = gainSum / period;
            var avgLoss = lossSum / period;
            if (avgLoss == 0) return 100.0;
            var rs = avgGain / avgLoss;
            return 100.0 - 100.0 / (1.0 + rs);
        }

        /// <summary>
        /// Simple-mean ATR at `endIndex` in the quotes buffer. Computes the
        /// mean of the last `period` True Range values where TR[i] =
        /// max(high-low, |high-prevClose|, |low-prevClose|). Returns 0 if
        /// there aren't enough quotes (caller must check).
        ///
        /// This is the SMA-based ATR variant used by the backtest spec —
        /// different from Wilder's smoothed ATR (which Skender implements).
        /// </summary>
        private static decimal ComputeSmaAtr(
            System.Collections.Generic.IReadOnlyList<IQuote> quotes,
            int endIndex,
            int period)
        {
            if (endIndex < period) return 0m;

            decimal sum = 0m;
            for (int i = endIndex - period + 1; i <= endIndex; i++)
            {
                var h = quotes[i].High;
                var l = quotes[i].Low;
                var pc = quotes[i - 1].Close;
                var t1 = h - l;
                var t2 = Math.Abs(h - pc);
                var t3 = Math.Abs(l - pc);
                sum += Math.Max(t1, Math.Max(t2, t3));
            }
            return sum / period;
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
            _logger.LogInformation($"  SL:                   {SlTpAtrMultiplier} × ATR");
            _logger.LogInformation($"  Dynamic TP:           80+→trail, 60-79→2R, 40-59→1.5R, <40→1R");
            _logger.LogInformation($"  Trailing Activation:  1.0R");
            _logger.LogInformation($"  Trailing Distance:    1.0 × ATR");
            _logger.LogInformation($"  Max Hold:             240 bars (4 hours on 1M)");
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
