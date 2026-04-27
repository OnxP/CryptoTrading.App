using Binance;
using CryptoTrading.App.Algorithm.RegimeBased;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
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

        // Streaming indicator hubs (Skender)
        private EmaHub<IQuote> _ema4H8;
        private EmaHub<IQuote> _ema4H21;
        private AtrHub<IQuote> _atr15M;

        // Partial 4H bar tracking (for provisional RSI)
        // Quote is init-only so we rebuild each tick; track running H/L in separate fields.
        private DateTime _provisional4HCloseTime;
        private decimal _provisional4HOpen;
        private decimal _provisional4HHigh;
        private decimal _provisional4HLow;
        private decimal _provisional4HVolume;
        private DateTime _last4HCloseTime;

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
        // Longs are historically weaker than shorts on BTC. Phase 1 at 65
        // cut the worst longs but RSI 65-70 still bled (PF 0.74, net -$7.6M
        // in-sample). Phase 2 raises the floor to 70 to keep only the
        // RSI 70-75 window that actually wins (WR 75%, PF 2.77).
        private const double HtfRsiLongThreshold = 70.0;
        // Sweet-spot short zone is RSI 35-40. RSI < 35 shorts underperform
        // (Phase 1 sub-bucket 30-35: WR 52%, PF 0.67, net -$19.2M).
        private const double HtfRsiShortThreshold = 40.0;
        private const double HtfRsiShortFloor = 35.0;
        private const int LtfAtrPeriod = 14;
        // Lower bound = entry qualifier. Upper bound filters out blow-off
        // bars. Phase 3 tightens from 1.75 to 1.5 because the 1.5-1.75 band
        // in-sample still ran PF 0.53 net -$29.7M — the same mean-reversion
        // pathology as the 1.75+ zone. Below 1.5 runs PF 4.27.
        private const decimal VolExpansionRatio = 1.2m;
        private const decimal VolExpansionMaxRatio = 1.5m;
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
            marketData.InitialDataLoadSubscribe(symbol, CandleInterval.Minute_15, ProcessHistoricData15M);
            marketData.InitialDataStreamSubscribe(symbol, CandleInterval.Minute_15, ProcessLiveCandle15M);

            // Subscribe to native 4H for RSI direction gate — the exchange's
            // 4H candles start earlier than 15M aggregation can produce and may
            // have slightly different close values, so using the real feed gives
            // more accurate RSI and earlier warmup.
            marketData.InitialDataLoadSubscribe(symbol, CandleInterval.Hour_4, ProcessHistoricData4H);
            marketData.InitialDataStreamSubscribe(symbol, CandleInterval.Hour_4, ProcessLiveCandle4H);

            _logger.LogInformation($"[HTF-RSI] Subscribed to {symbol} on 15M + 4H");
        }

        #region Historic Data Loading

        private void ProcessHistoricData15M(IEnumerable<ExchangeCandlestick> candlesticks)
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

            // Streaming Wilder ATR on 15M
            _atr15M = _quoteHub15M.ToAtr(LtfAtrPeriod);

            var initialAtr = _atr15M.Results.LastOrDefault()?.Atr;
            _logger.LogInformation(
                $"[HTF-RSI] Loaded {candlesticks.Count()} 15M candles | " +
                $"15M ATR(Wilder): {initialAtr?.ToString("F2") ?? "N/A"}");
        }

        private void ProcessHistoricData4H(IEnumerable<ExchangeCandlestick> candlesticks)
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

            // Streaming EMAs on 4H (Skender hubs); RSI computed on-demand with provisional bar
            _ema4H8  = _quoteHub4H.ToEma(8);
            _ema4H21 = _quoteHub4H.ToEma(21);

            _last4HCloseTime = _quoteHub4H.Quotes.Count > 0
                ? _quoteHub4H.Quotes[_quoteHub4H.Quotes.Count - 1].Timestamp
                : DateTime.MinValue;

            var rsi = _quoteHub4H.Quotes.ToRsi(HtfRsiPeriod).LastOrDefault()?.Rsi;
            _logger.LogInformation(
                $"[HTF-RSI] Loaded {candlesticks.Count()} 4H candles | " +
                $"4H RSI(Wilder): {rsi?.ToString("F1") ?? "N/A"}");
        }

        #endregion

        #region Live Data Processing

        private void ProcessLiveCandle15M(ExchangeCandlestickEvent args)
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

                // Push provisional 4H bar so _rsi4H reflects the partial close
                UpdateProvisional4HBar(args.Candlestick);

                // Check entry conditions
                EvaluateEntry(args.Candlestick, ts);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[HTF-RSI] Error processing 15M candle at {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}");
            }
        }

        private void ProcessLiveCandle4H(ExchangeCandlestickEvent args)
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
                _last4HCloseTime = quote.Timestamp;
                _provisional4HCloseTime = DateTime.MinValue; // reset; next 15M tick starts fresh

                var rsi = _quoteHub4H.Quotes.ToRsi(HtfRsiPeriod).LastOrDefault()?.Rsi;
                _logger.LogInformation(
                    $"[HTF-RSI 4H {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}] " +
                    $"New 4H candle | RSI(Wilder): {rsi?.ToString("F1") ?? "N/A"} | " +
                    $"Close: {args.Candlestick.Close:F2}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"[HTF-RSI] Error processing 4H candle at {args.Candlestick.CloseTime:yyyy-MM-dd HH:mm}");
            }
        }

        private void EvaluateEntry(ExchangeCandlestick candle, string ts)
        {
            // Pre-checks
            if (!_tradingState.CanTrade)
            {
                _logger.LogDebug($"[HTF-RSI {ts}] Cannot trade: {_tradingState.GetStatus()}");
                return;
            }

            // Need enough 15M quotes for ATR(14) + 20-bar lookback, and enough
            // 4H quotes for Wilder RSI(14) which needs `period + 1` bars.
            var quotesCount = _quoteHub15M.Quotes.Count;
            var htfCount = _quoteHub4H.Quotes.Count;
            if (htfCount < HtfRsiPeriod + 1 || quotesCount < VolExpansionLookback + LtfAtrPeriod + 2)
                return;

            // 1. Get 4H RSI (Wilder) including the in-progress partial bar.
            // Build a temporary list: completed bars + one provisional bar whose
            // close equals the current 15M close. Skender's ToRsi runs on this
            // snapshot without modifying the hub.
            var quotes4H = _quoteHub4H.Quotes;
            List<IQuote> rsiInput;
            if (_provisional4HCloseTime > _last4HCloseTime)
            {
                rsiInput = new List<IQuote>(quotes4H.Count + 1);
                rsiInput.AddRange(quotes4H);
                rsiInput.Add(new Quote
                {
                    Timestamp = _provisional4HCloseTime,
                    Open      = _provisional4HOpen,
                    High      = _provisional4HHigh,
                    Low       = _provisional4HLow,
                    Close     = candle.Close,
                    Volume    = _provisional4HVolume
                });
            }
            else
            {
                rsiInput = new List<IQuote>(quotes4H);
            }
            var currentRsi = rsiInput.ToRsi(HtfRsiPeriod).LastOrDefault()?.Rsi;
            if (!currentRsi.HasValue) return;

            // 2. Determine direction.
            // Long zone: RSI > 70 (strong HTF uptrend).
            // Short zone: 35 ≤ RSI < 40 (HTF weakness, but not already blown out).
            TradeDirection direction;
            if (currentRsi.Value > HtfRsiLongThreshold)
                direction = TradeDirection.Long;
            else if (currentRsi.Value < HtfRsiShortThreshold && currentRsi.Value >= HtfRsiShortFloor)
                direction = TradeDirection.Short;
            else
                return; // RSI outside productive zones

            // 3. Get 15M ATR (Wilder) and check vol expansion
            var atrResults = _atr15M?.Results;
            if (atrResults == null || atrResults.Count < VolExpansionLookback + 1) return;

            var currentAtrVal = atrResults[atrResults.Count - 1].Atr;
            var pastAtrVal = atrResults[atrResults.Count - 1 - VolExpansionLookback].Atr;
            if (!currentAtrVal.HasValue || !pastAtrVal.HasValue) return;

            var currentAtr = (decimal)currentAtrVal.Value;
            var pastAtr = (decimal)pastAtrVal.Value;
            if (currentAtr <= 0 || pastAtr <= 0) return;

            var expansionRatio = currentAtr / pastAtr;

            _logger.LogInformation(
                $"[HTF-RSI {ts}] BIAS {direction} | RSI:{currentRsi.Value:F1} | " +
                $"ATR:{currentAtr:F2} vs {pastAtr:F2} (20 ago) | VolExp:{expansionRatio:F2} | " +
                $"Band:[{VolExpansionRatio},{VolExpansionMaxRatio}) | {_tradingState.GetStatus()}");

            // Vol expansion must be in-band: low end filters chop, high end
            // rejects blow-off / climax bars which historically mean-revert.
            if (expansionRatio < VolExpansionRatio || expansionRatio >= VolExpansionMaxRatio)
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

            // Get 15M RSI (Wilder) for probability score
            double rsi15M = _quoteHub15M.Quotes.ToRsi(14).LastOrDefault()?.Rsi ?? 50;

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

            // Note: a surgical Direction × Score filter was tested (block Long 60-79
            // and/or Short 80+ per the Phase 1 loss postmortem). Results on Phase 3:
            //   - Short 80+ filter: fires ZERO times — Phase 3's RSI floor + vol cap
            //     already suppresses every setup that would have scored 80+ for a short.
            //   - Long 60-79 filter: halves trade count AND halves profit (DD halves too,
            //     so Calmar is unchanged — no edge added, just lower variance).
            // Conclusion: Phase 3's threshold tightening already captures this edge
            // implicitly. No additional filter here.

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
                OrderSide = direction == TradeDirection.Long ? ExchangeOrderSide.Buy : ExchangeOrderSide.Sell,
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
        /// Builds a provisional 4H quote for the in-progress bar and pushes it into
        /// <see cref="_quoteHub4H"/> using the same timestamp each tick. Skender
        /// replaces any existing quote with that timestamp in place, so <c>ToRsi</c>
        /// on the hub's quotes always reflects the partial close.
        ///
        /// Skipped if the real 4H close for this period has already arrived.
        /// </summary>
        private void UpdateProvisional4HBar(ExchangeCandlestick ltf)
        {
            var closeTime = Get4HBarCloseTime(ltf.CloseTime);
            if (closeTime <= _last4HCloseTime) return;

            if (_provisional4HCloseTime != closeTime)
            {
                // First 15M bar of a new 4H period — seed OHLV
                _provisional4HCloseTime = closeTime;
                _provisional4HOpen      = ltf.Open;
                _provisional4HHigh      = ltf.High;
                _provisional4HLow       = ltf.Low;
                _provisional4HVolume    = ltf.Volume;
            }
            else
            {
                // Subsequent 15M bars — extend running H/L/V
                if (ltf.High > _provisional4HHigh) _provisional4HHigh = ltf.High;
                if (ltf.Low  < _provisional4HLow)  _provisional4HLow  = ltf.Low;
                _provisional4HVolume += ltf.Volume;
            }

            // Do NOT push to _quoteHub4H — only real completed bars live there.
            // EvaluateEntry calls ToRsi on a temporary list that appends this bar.
        }

        /// <summary>
        /// Returns the close-time of the 4H bar that contains the given 15M close-time.
        /// Binance 4H bars start on 4-hour UTC boundaries (00, 04, 08, 12, 16, 20).
        /// </summary>
        private static DateTime Get4HBarCloseTime(DateTime ltfCloseTime)
        {
            // 15M close-time is the END of the bar; open = close - 15 min
            var openTime = ltfCloseTime.AddMinutes(-15);
            var nextBoundaryHour = ((openTime.Hour / 4) + 1) * 4;
            var date = openTime.Date;
            if (nextBoundaryHour >= 24)
            {
                date = date.AddDays(1);
                nextBoundaryHour -= 24;
            }
            return new DateTime(date.Year, date.Month, date.Day, nextBoundaryHour, 0, 0, DateTimeKind.Utc);
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
            _logger.LogInformation($"  HTF RSI Short:        {HtfRsiShortFloor} <= RSI < {HtfRsiShortThreshold}");
            _logger.LogInformation($"  HTF Multiplier:       16 (15M → 4H)");
            _logger.LogInformation("  -- Lower Timeframe (15M) --");
            _logger.LogInformation($"  LTF ATR Period:       {LtfAtrPeriod}");
            _logger.LogInformation($"  Vol Expansion Band:   [{VolExpansionRatio}, {VolExpansionMaxRatio})");
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
