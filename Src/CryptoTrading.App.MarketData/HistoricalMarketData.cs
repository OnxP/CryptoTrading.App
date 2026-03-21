using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Extensions;
using CryptoTrading.App.Core.TradeRequest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.MarketData
{
    public class HistoricalMarketData : AbstractMarketData, IMarketData
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        IExchangeProvider _provider;
        public ILogger<HistoricalMarketData> Logger { get; set; }

        public HistoricalMarketData(ILogger<HistoricalMarketData> logger)
        {
            Logger = logger;
        }

        public HistoricalMarketData(ILogger<HistoricalMarketData> logger,DateTime from,DateTime to):this(logger)
        {
            From = from;
            To = to;
        }

        public void Configure(IRequest request)
        {
            var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", true, false)
                    .Build();

            // Configure services.
            var services = new ServiceCollection()
                .AddLogging(builder => builder // configure logging.
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddFile(configuration.GetSection("Logging:File")))
                .BuildServiceProvider();

            _provider = services.GetService<IExchangeProvider>();
        }

        public void Configure(IExchangeProvider provider)
        {
            _provider = provider;
        }

        public override void Configure(IConfig request)
        {
            Configure(new CancelRequest(0,"TEST"));
        }

        public Task StartStream(CancellationToken ct)
        {
            return new Task(StartStream);
        }

        public ITaskController GetTaskController()
        {
            var controller = new TaskController(StartStream);
            return controller;
        }

        public void StartStream()
        {
            try
            {
                //Logger.LogInformation("Loading Historic Candlesticks");
                var tasks = new List<Task>();
                //foreach (var item in historicDataSubscribers)
                //{
                //    tasks.Add(LoadHistoricData(_provider, item.Key, From, item.Value));
                //}
                //Task.WaitAll(tasks.ToArray());
                //Logger.LogInformation("Finished loading Historic Candlesticks");
                tasks = new List<Task>();
                //LoadData

                Logger.LogInformation("Loading Candlesticks");

                //StreamHistoricData
                foreach (var item in subscribers)
                {
                    var from = From;
                    var list = SplitDates(item.Key.interval, from, To);
                    foreach (var to in list)
                    {
                        Stopwatch time = new Stopwatch();
                        time.Start();
                        StreamData(_provider, item.Key, from, to);
                        from = to;
                        //while (time.ElapsedMilliseconds <= 1000)
                        //{ }
                    }
                }
                //Task.WaitAll(tasks.ToArray());
                Logger.LogInformation("Finished Loading Candlesticks");

                //sort the list



            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine();
                Console.WriteLine("  ...press any key to close window.");
                Console.ReadKey(true);
            }
        }

        List<(ExchangeCandlestick candlestick, CandleInterval interval)> candleSticksToStream = new List<(ExchangeCandlestick, CandleInterval interval)>();

        private async void StreamData(IExchangeProvider provider, (string symbol, CandleInterval interval) symbol, DateTime from, DateTime to)
        {
            /* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var candleSticks = await provider.GetCandlesticksAsync(symbol.symbol, symbol.interval, from.ToUniversalTime(), to.ToUniversalTime()).ConfigureAwait(false);
            var action = historicDataSubscribers.First().Value.First();
            Logger.LogInformation($"Loading Candlesticks for {symbol.symbol}-{symbol.interval} From:{from.ToString("dd MM yy hh:mm")} To: {to.ToString("dd MM yy hh:mm")} Number of candleSticks:{candleSticks.Count()}");

            action.Invoke(candleSticks);
        }

        private DateTime CalculateFrom(DateTime dateTime, CandleInterval interval)
        {
            int candleSticksToLoad = 200;
            return interval switch
            {
                CandleInterval.Minute_1 => dateTime.AddMinutes(-1 * candleSticksToLoad),
                CandleInterval.Minute_3 => dateTime.AddMinutes(-3 * candleSticksToLoad),
                CandleInterval.Minute_5 => dateTime.AddMinutes(-5 * candleSticksToLoad),
                CandleInterval.Minute_15 => dateTime.AddMinutes(-15 * candleSticksToLoad),
                CandleInterval.Minute_30 => dateTime.AddMinutes(-30 * candleSticksToLoad),
                CandleInterval.Hour_1 => dateTime.AddHours(-1 * candleSticksToLoad),
                CandleInterval.Hour_2 => dateTime.AddHours(-2 * candleSticksToLoad),
                CandleInterval.Hour_4 => dateTime.AddHours(-4 * candleSticksToLoad),
                CandleInterval.Hour_6 => dateTime.AddHours(-6 * candleSticksToLoad),
                CandleInterval.Hour_8 => dateTime.AddHours(-8 * candleSticksToLoad),
                CandleInterval.Hour_12 => dateTime.AddHours(-12 * candleSticksToLoad),
                CandleInterval.Day_1 => dateTime.AddDays(-1 * candleSticksToLoad),
                CandleInterval.Day_3 => dateTime.AddDays(-3 * candleSticksToLoad),
                CandleInterval.Week_1 => dateTime.AddDays(-7 * candleSticksToLoad),
                CandleInterval.Month_1 => dateTime.AddMonths(-12 * candleSticksToLoad),
                _ => dateTime,
            };
        }
        private async System.Threading.Tasks.Task LoadHistoricData(IExchangeProvider provider, (string symbol, CandleInterval interval) symbol, DateTime from, IList<Action<IEnumerable<ExchangeCandlestick>>> callback)
        {
            var calculatedFrom = CalculateFrom(from, symbol.interval).ToUniversalTime();
            var candleSticks = await provider.GetCandlesticksAsync(symbol.symbol, symbol.interval, calculatedFrom, from.ToUniversalTime()).ConfigureAwait(false);
            //need to drop first candle
            var sticks = candleSticks.Reverse().Skip(1);
            foreach (var action in callback)
            {
                action.Invoke(sticks);
            }

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
                    return new List<DateTime>() { to } ;
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
