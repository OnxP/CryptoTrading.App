using Binance.Utility;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketData
{
    /// <summary>
    /// Historic bar loader for backtests / calibration. Fetches closed bars
    /// via <see cref="IExchangeProvider"/> (Binance.Net under the hood) and
    /// fans them out to the <c>historicDataSubscribers</c> map populated by
    /// <see cref="AbstractMarketData.InitialDataLoadSubscribe"/>.
    ///
    /// PR 5c: IMarketDataEvents is now neutral so this file no longer
    /// translates to bundled <c>Candlestick</c> at the subscriber seam —
    /// neutral candles from the provider flow straight through. The interval
    /// splitter <see cref="SplitDates"/> stays keyed on the neutral
    /// <see cref="CandleInterval"/>.
    /// </summary>
    public class HistoricalMarketData : AbstractMarketData, IMarketData
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        private readonly IExchangeProvider _exchange;
        public ILogger<HistoricalMarketData> Logger { get; set; }

        public HistoricalMarketData(ILogger<HistoricalMarketData> logger, IExchangeProvider exchange)
        {
            Logger = logger;
            _exchange = exchange;
        }

        public HistoricalMarketData(ILogger<HistoricalMarketData> logger, IExchangeProvider exchange,
            DateTime from, DateTime to) : this(logger, exchange)
        {
            From = from;
            To = to;
        }

        public override void Configure(IConfig request)
        {
            // No-op: the old Configure() constructed its own DI container to
            // resolve bundled-SDK services. With IExchangeProvider injected,
            // there is nothing to wire up here — Configure is only on the
            // interface for back-compat with the Live path.
        }

        public Task StartStream(CancellationToken ct)
        {
            return StartStream();
        }

        public ITaskController GetTaskController()
        {
            return new TaskController(StartStream);
        }

        public async Task StartStream()
        {
            try
            {
                if (!historicDataSubscribers.Any() && !subscribers.Any())
                {
                    Logger.LogWarning("StartStream called with no subscribers; nothing to load.");
                    return;
                }

                // All callbacks currently share a single action — preserve the
                // legacy behaviour of fanning to historicDataSubscribers.First().
                var action = historicDataSubscribers.Any()
                    ? historicDataSubscribers.First().Value.First()
                    : null;

                if (action == null)
                {
                    Logger.LogWarning("StartStream: no historic-data subscribers registered; skipping load.");
                    return;
                }

                var semaphore = new SemaphoreSlim(10);
                var tasks = new List<Task>();

                Logger.LogInformation("Loading Candlesticks");

                foreach (var item in subscribers)
                {
                    var from = From;
                    var list = SplitDates(item.Key.interval, from, To);
                    var localItem = item;
                    foreach (var to in list)
                    {
                        var localFrom = from;
                        var localTo = to;
                        tasks.Add(Task.Run(async () =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                var candlesticks = await StreamData(localItem.Key, localFrom, localTo);
                                action.Invoke(candlesticks.ToList());
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }));

                        from = to;
                    }
                }

                await Task.WhenAll(tasks);
                Logger.LogInformation("Finished Loading Candlesticks");
            }
            catch (Exception e)
            {
                Logger.LogError(e, "HistoricalMarketData.StartStream failed");
                throw;
            }
        }

        private async Task<IEnumerable<ExchangeCandlestick>> StreamData(
            (string symbol, CandleInterval interval) symbol, DateTime from, DateTime to)
        {
            var neutralCandles = await _exchange.GetCandlesticksAsync(
                symbol.symbol,
                symbol.interval,
                from.ToUniversalTime(),
                to.ToUniversalTime()).ConfigureAwait(false);

            var list = neutralCandles.ToList();

            Logger.LogInformation(
                $"Loading Candlesticks for {symbol.symbol}-{symbol.interval} " +
                $"From:{from:dd MM yy hh:mm} To: {to:dd MM yy hh:mm} " +
                $"Number of candleSticks:{list.Count}");

            return list;
        }

        protected IEnumerable<DateTime> SplitDates(CandleInterval interval, DateTime from, DateTime to)
        {
            switch (interval)
            {
                case CandleInterval.Minute_1:
                    return GenerateTimeList(1, from, to);
                case CandleInterval.Minute_3:
                    return GenerateTimeList(3, from, to);
                case CandleInterval.Minute_5:
                    return GenerateTimeList(5, from, to);
                case CandleInterval.Minute_15:
                    return GenerateTimeList(15, from, to);
                case CandleInterval.Minute_30:
                    return GenerateTimeList(30, from, to);
                case CandleInterval.Hour_1:
                    return GenerateTimeList(1 * 60, from, to);
                case CandleInterval.Hour_2:
                    return GenerateTimeList(2 * 60, from, to);
                case CandleInterval.Hour_4:
                    return GenerateTimeList(4 * 60, from, to);
                case CandleInterval.Hour_6:
                    return GenerateTimeList(6 * 60, from, to);
                case CandleInterval.Hour_8:
                    return GenerateTimeList(8 * 60, from, to);
                case CandleInterval.Hour_12:
                    return GenerateTimeList(12 * 60, from, to);
                default:
                    return new List<DateTime> { to };
            }
        }

        private IEnumerable<DateTime> GenerateTimeList(int v, DateTime from, DateTime to)
        {
            var list = new List<DateTime>();
            var dt = from;
            list.Add(from);
            while (dt < to)
            {
                dt = dt.AddMinutes(v * 1000);
                if (dt < to)
                {
                    list.Add(dt);
                }
                else
                {
                    list.Add(to);
                    break;
                }
            }
            return list;
        }

        protected IEnumerable<DateTime> SplitByWeek(DateTime from, DateTime to)
        {
            var daysdiff = (to - from).TotalDays;
            if (daysdiff <= 7)
            {
                yield return to;
                yield break;
            }
            var j = 0;
            for (int i = 0; i < daysdiff; i += 7)
            {
                yield return from.AddDays(i * 7);
                j += 7;
            }
            if (daysdiff - j > 0)
            {
                yield return from.AddDays(daysdiff - j);
            }
        }

        protected IEnumerable<DateTime> SplitByDay(DateTime from, DateTime to)
        {
            var daysdiff = (to - from).TotalDays;
            if (daysdiff < 1)
            {
                yield return to;
                yield break;
            }
            var j = 0;
            for (int i = 1; i <= daysdiff; i++)
            {
                yield return from.AddDays(i);
                j += 7;
            }
            if (daysdiff - j > 0)
            {
                yield return from.AddDays(daysdiff - j);
            }
        }
    }
}
