using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketData
{
    // Class monitors the position in the open trade and adjusts the stop loss
    // against live streaming 1m candles.
    //
    // PR 5b: was previously wired directly to the bundled-SDK
    // IBinanceApi / ICandlestickClient / IBinanceWebSocketStream. Now it
    // goes through IExchangeProvider (Binance.Net) with the bundled-SDK
    // types only surfacing at the IMarketMonitor boundary so the 15+
    // callers don't have to change in this PR. A follow-up PR migrates
    // IMarketMonitor itself to neutral types.
    public class LiveMarketMonitor : AbstractMarketData, IMarketMonitor
    {
        protected IExchangeProvider _exchange;
        protected ILogger Logger;

        // Interval used for the 1m live feed this monitor subscribes to.
        // Kept as a field so the fixed choice is visible in one place; any
        // future per-symbol override lands here.
        private static readonly CandleInterval StreamInterval = CandleInterval.Minute_1;
        private static readonly CandlestickInterval StreamIntervalBundled = CandlestickInterval.Minute;

        public LiveMarketMonitor(ILogger<LiveMarketMonitor> logger, IExchangeProvider exchange)
        {
            _exchange = exchange;
            Logger = logger;
        }

        private readonly List<string> symbols = new List<string>();

        protected LiveMarketMonitor()
        {
        }

        public virtual async Task<bool> CheckOrder(ITransaction transaction)
        {
            // Prefer the exchange-assigned order id (populated on place); fall
            // back to the client id only because legacy paper-trade paths can
            // leave OrderId blank — the neutral provider tolerates both via
            // the upstream get-order call.
            var id = !string.IsNullOrEmpty(transaction.Order?.OrderId)
                ? transaction.Order.OrderId
                : transaction.Order?.ClientOrderId;

            if (string.IsNullOrEmpty(id))
            {
                Logger?.LogWarning("CheckOrder invoked with no order id on transaction for {Pair}", transaction.Pair);
                return false;
            }

            var newOrder = await _exchange.GetOrderAsync(transaction.Pair, id).ConfigureAwait(false);
            transaction.UpdateOrder(newOrder);
            return newOrder.Status == ExchangeOrderStatus.Filled;
        }

        public async Task Subscribe(string symbol, string keyValue, Action<CandlestickEventArgs> processCandleStick)
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
                        var args = BundledSdkBridge.ToBundledEventArgs(neutralCandle, StreamIntervalBundled);
                        processCandleStick.Invoke(args);
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
            // Single-symbol unsubscribe is a follow-up on the provider
            // interface — for the 1-symbol BTCUSDT runtime this is a no-op
            // while other symbols are active.
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

        public async Task<List<Candlestick>> GetHistoricCandleSticks(string symbol)
        {
            // 200 bars of 1m data is the shape the legacy bundled-SDK
            // implementation returned; preserved byte-for-byte so downstream
            // indicators (TradeMonitor.QuoteHub seed) don't see a behavioural
            // change from this PR.
            var calculatedFrom = CandleStickIntervalHelper
                .CalculateCandleStickTimeFrom(DateTime.Now, StreamIntervalBundled, 200)
                .ToUniversalTime();

            var neutralCandles = await _exchange.GetCandlesticksAsync(
                symbol,
                StreamInterval,
                calculatedFrom,
                DateTime.Now.ToUniversalTime()).ConfigureAwait(false);

            var list = neutralCandles
                .Reverse()
                .Select(c => BundledSdkBridge.ToBundledCandlestick(c, StreamIntervalBundled))
                .ToList();

            // Legacy code called .Reverse() again on the materialized list.
            // Keeping the same double-reverse behaviour so the surface-level
            // ordering is identical to the bundled-SDK path.
            list.Reverse();
            return list;
        }
    }
}
