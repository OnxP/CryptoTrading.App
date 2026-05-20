using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.MarketMonitorFactory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketData
{
    /// <summary>
    /// Monitors the open position and adjusts the stop loss against a
    /// live 1-minute candle stream.
    ///
    /// PR 5c: IMarketMonitor is now fully neutral
    /// (<see cref="ExchangeCandlestickEvent"/> / <see cref="ExchangeCandlestick"/>)
    /// so this file no longer translates at the boundary — neutral candles
    /// from the provider flow straight to the TradeMonitor callback.
    /// </summary>
    public class LiveMarketMonitor : AbstractMarketData, IMarketMonitor
    {
        protected IExchangeProvider _exchange;
        protected ILogger Logger;

        // Interval used for the 1m live feed this monitor subscribes to.
        private static readonly CandleInterval StreamInterval = CandleInterval.Minute_1;

        public LiveMarketMonitor(ILogger<LiveMarketMonitor> logger, IExchangeProvider exchange)
        {
            _exchange = exchange;
            Logger = logger;
        }

        private readonly List<string> symbols = new List<string>();

        protected LiveMarketMonitor()
        {
        }

        public virtual async Task<ExchangeOrder> CheckOrder(string orderId, string symbol)
        {
            if (string.IsNullOrEmpty(orderId))
            {
                Logger?.LogWarning("CheckOrder invoked with no order id for {Symbol}", symbol);
                return null;
            }

            return await _exchange.GetOrderAsync(symbol, orderId).ConfigureAwait(false);
        }

        public async Task Subscribe(string symbol, string keyValue, Action<ExchangeCandlestickEvent> processCandleStick)
        {
            if (!symbols.Contains(symbol))
            {
                symbols.Add(symbol);
            }

            // Binance.Net manages its own websocket connection lifecycle; one
            // SubscribeCandlestickAsync call per symbol is all we need and the
            // underlying socket stays connected until UnsubscribeAllAsync.
            await _exchange.SubscribeCandlestickAsync(
                symbol,
                StreamInterval,
                neutralCandle =>
                {
                    try
                    {
                        var eventTime = neutralCandle.CloseTime == default
                            ? DateTime.UtcNow
                            : neutralCandle.CloseTime;
                        var evt = new ExchangeCandlestickEvent(neutralCandle, eventTime);
                        processCandleStick.Invoke(evt);
                    }
                    catch (Exception e)
                    {
                        Logger?.LogError(e, "LiveMarketMonitor subscriber callback threw for {Symbol}", symbol);
                    }
                }).ConfigureAwait(false);
        }

        public bool IsSubscribed(string symbol, string keyValue)
        {
            return symbols.Contains(symbol);
        }

        public void UnSubscribe(string symbol, string keyValue)
        {
            symbols.Remove(symbol);

            // IExchangeProvider only exposes UnsubscribeAllAsync today; call
            // it when the last symbol drops so we don't leak sockets.
            if (!symbols.Any())
            {
                try
                {
                    _exchange.UnsubscribeAllAsync().GetAwaiter().GetResult();
                }
                catch (Exception e)
                {
                    Logger?.LogWarning(e, "LiveMarketMonitor failed to unsubscribe cleanly for {Symbol}", symbol);
                }
            }
        }

        public override void Configure(IConfig request)
        {
            throw new NotImplementedException();
        }

        public async Task<List<ExchangeCandlestick>> GetHistoricCandleSticks(string symbol)
        {
            // 200 bars of 1m data preserves the shape the legacy bundled-SDK
            // implementation returned; downstream indicators (TradeMonitor.QuoteHub
            // seed) don't see a behavioural change from this PR.
            var calculatedFrom = CandleStickIntervalHelper
                .CalculateCandleStickTimeFrom(DateTime.Now, StreamInterval, 200)
                .ToUniversalTime();

            var neutralCandles = await _exchange.GetCandlesticksAsync(
                symbol,
                StreamInterval,
                calculatedFrom,
                DateTime.Now.ToUniversalTime()).ConfigureAwait(false);

            var list = neutralCandles.Reverse().ToList();

            // Legacy code double-reversed here; preserve the same surface-level
            // ordering so TradeMonitor's seed is byte-for-byte identical.
            list.Reverse();
            return list;
        }
    }
}
