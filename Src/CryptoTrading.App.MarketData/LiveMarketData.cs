using Binance.Utility;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.RequestTracker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketData
{
    /// <summary>
    /// Live market data feed. Loads historic candles then subscribes to
    /// live kline streams via <see cref="IExchangeProvider"/>.
    ///
    /// PR 5c: IMarketDataEvents is now fully neutral so the bundled-SDK
    /// bridge calls at the candle/event seams are gone — neutral candles
    /// from the provider flow straight to subscribers.
    /// </summary>
    public class LiveMarketData : AbstractMarketData, IMarketData
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        private readonly IExchangeProvider _exchange;
        public ILogger<LiveMarketData> Logger { get; set; }
        public bool loadHistoric = true;

        public LiveMarketData(ILogger<LiveMarketData> logger, IExchangeProvider exchange)
        {
            Logger = logger;
            _exchange = exchange;
        }

        public override void Configure(IConfig request)
        {
            if (loadHistoric)
            {
                LoadHistoricCandleSticks(request);
                loadHistoric = false;
            }
            // Live subscriptions are established inside the TaskController's
            // action (GetTaskController) so that Begin()/CancelAsync() own the
            // stream lifecycle.
        }

        public ITaskController GetTaskController()
        {
            return new TaskController(RunStreamAsync);
        }

        /// <summary>
        /// Subscribe to every live kline stream the subscribers dictionary
        /// asks for, then park on the cancellation token. Cancellation
        /// triggers UnsubscribeAllAsync so the underlying Binance.Net socket
        /// connections are cleanly released.
        /// </summary>
        private async Task RunStreamAsync(CancellationToken ct)
        {
            try
            {
                foreach (var entry in subscribers)
                {
                    var key = entry.Key;
                    var callbacks = entry.Value.ToList();

                    await _exchange.SubscribeCandlestickAsync(
                        key.symbol,
                        key.interval,
                        neutralCandle =>
                        {
                            try
                            {
                                var eventTime = neutralCandle.CloseTime == default
                                    ? DateTime.UtcNow
                                    : neutralCandle.CloseTime;
                                var evt = new ExchangeCandlestickEvent(neutralCandle, eventTime);
                                foreach (var cb in callbacks)
                                {
                                    cb.Invoke(evt);
                                }
                                // Flush any pending request-tracker submits,
                                // preserving the legacy behaviour the bundled
                                // IBinanceWebSocketStream.PostMessage handler had.
                                RequestTracker.Instance.SubmitRequests();
                            }
                            catch (Exception e)
                            {
                                Logger?.LogError(e, "LiveMarketData subscriber callback threw");
                            }
                        }).ConfigureAwait(false);
                }

                // Park until cancellation; cleanup on the way out.
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* expected */ }
            }
            finally
            {
                try { await _exchange.UnsubscribeAllAsync().ConfigureAwait(false); }
                catch (Exception e)
                {
                    Logger?.LogWarning(e, "LiveMarketData failed to unsubscribe cleanly");
                }
            }
        }

        public void LoadHistoricCandleSticks(IConfig config)
        {
            Logger.LogInformation("Loading Historic Candlesticks");
            var tasks = new List<Task>();
            foreach (var item in historicDataSubscribers)
            {
                tasks.Add(LoadHistoricData(item.Key, config.NumberOfCandleSticksToLoad, item.Value));
            }
            Task.WaitAll(tasks.ToArray());
            Logger.LogInformation("Finished loading Historic Candlesticks");
        }

        private async Task LoadHistoricData(
            (string symbol, CandleInterval interval) symbol,
            int numberOfCandleSticks,
            IList<Action<IEnumerable<ExchangeCandlestick>>> callback)
        {
            var calculatedFrom = CandleStickIntervalHelper
                .CalculateCandleStickTimeFrom(DateTime.Now, symbol.interval, numberOfCandleSticks)
                .ToUniversalTime();

            var neutralCandles = await _exchange.GetCandlesticksAsync(
                symbol.symbol,
                symbol.interval,
                calculatedFrom,
                DateTime.Now.ToUniversalTime()).ConfigureAwait(false);

            // Match the old implementation's reversal so downstream consumers
            // that assumed "newest first → oldest last" see the same ordering.
            var sticks = neutralCandles.Reverse().ToList();

            foreach (var action in callback)
            {
                action.Invoke(sticks);
            }
        }
    }
}
