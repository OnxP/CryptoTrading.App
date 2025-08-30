using Binance;
using Binance.Client;
using Binance.Utility;
using Binance.WebSocket;
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

        ICandlestickClient _client;
        IBinanceWebSocketStream _webSocket;
        IBinanceApi _api;
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
                .AddBinance() // add default Binance services.
                .AddLogging(builder => builder // configure logging.
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddFile(configuration.GetSection("Logging:File")))
                .BuildServiceProvider();

            // Initialize client.
            _client = services.GetService<ICandlestickClient>();
            _api = services.GetService<IBinanceApi>();
            // Initialize the stream.
            _webSocket = services.GetService<IBinanceWebSocketStream>();
            _webSocket.Message += (s, e) => _client.HandleMessage(e.Subject, e.Json);

        }

        public void Configure(IBinanceApi api)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, false)
                .Build();

            // Configure services.
            var services = new ServiceCollection()
                .AddBinance() // add default Binance services.
                .AddLogging(builder => builder // configure logging.
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddFile(configuration.GetSection("Logging:File")))
                .BuildServiceProvider();

            // Initialize client.
            _client = services.GetService<ICandlestickClient>();
            _api = api;
            // Initialize the stream.
            _webSocket = services.GetService<IBinanceWebSocketStream>();
            _webSocket.Message += (s, e) => _client.HandleMessage(e.Subject, e.Json);
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
                //    tasks.Add(LoadHistoricData(_api, item.Key, From, item.Value));
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
                        StreamData(_api, item.Key, from, to);
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

        List<(Candlestick candlestick, CandlestickInterval interval)> candleSticksToStream = new List<(Candlestick, CandlestickInterval interval)>();

        private void StreamData(IBinanceApi api, (string symbol, CandlestickInterval interval) symbol, DateTime from, DateTime to)
        {
/* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var candleSticks = await api.GetCandlesticksAsync(symbol.symbol, symbol.interval, 1000, from.ToUniversalTime(), to.ToUniversalTime()).ConfigureAwait(false).ToList();
            var action = historicDataSubscribers.First().Value.First();
            Logger.LogInformation($"Loading Candlesticks for {symbol.symbol}-{symbol.interval} From:{from.ToString("dd MM yy hh:mm")} To: {to.ToString("dd MM yy hh:mm")} Number of candleSticks:{candleSticks.Count()}");

            action.Invoke(candleSticks);
        }

        void liveStream()
        {
            _webSocket.Uri = BinanceWebSocketStream.CreateUri(_client);

            using var controller = new RetryTaskController(_webSocket.StreamAsync);
            controller.Error += (s, e) => HandleError(e.Exception);
            controller.Begin();
        }

        private DateTime CalculateFrom(DateTime dateTime, CandlestickInterval interval)
        {
            int candleSticksToLoad = 200;
            return interval switch
            {
                CandlestickInterval.Minute => dateTime.AddMinutes(-1 * candleSticksToLoad),
                CandlestickInterval.Minutes_3 => dateTime.AddMinutes(-3 * candleSticksToLoad),
                CandlestickInterval.Minutes_5 => dateTime.AddMinutes(-5 * candleSticksToLoad),
                CandlestickInterval.Minutes_15 => dateTime.AddMinutes(-15 * candleSticksToLoad),
                CandlestickInterval.Minutes_30 => dateTime.AddMinutes(-30 * candleSticksToLoad),
                CandlestickInterval.Hour => dateTime.AddHours(-1 * candleSticksToLoad),
                CandlestickInterval.Hours_2 => dateTime.AddHours(-2 * candleSticksToLoad),
                CandlestickInterval.Hours_4 => dateTime.AddHours(-4 * candleSticksToLoad),
                CandlestickInterval.Hours_6 => dateTime.AddHours(-6 * candleSticksToLoad),
                CandlestickInterval.Hours_8 => dateTime.AddHours(-8 * candleSticksToLoad),
                CandlestickInterval.Hours_12 => dateTime.AddHours(-12 * candleSticksToLoad),
                CandlestickInterval.Day => dateTime.AddDays(-1 * candleSticksToLoad),
                CandlestickInterval.Days_3 => dateTime.AddDays(-3 * candleSticksToLoad),
                CandlestickInterval.Week => dateTime.AddDays(-7 * candleSticksToLoad),
                CandlestickInterval.Month => dateTime.AddMonths(-12 * candleSticksToLoad),
                _ => dateTime,
            };
        }
        private async System.Threading.Tasks.Task LoadHistoricData(IBinanceApi api, (string symbol, CandlestickInterval interval) symbol, DateTime from, IList<Action<IEnumerable<Candlestick>>> callback)
        {
            var calculatedFrom = CalculateFrom(from, symbol.interval).ToUniversalTime();
            var candleSticks = await api.GetCandlesticksAsync(symbol.symbol, symbol.interval, 0, calculatedFrom, from.ToUniversalTime()).ConfigureAwait(false);
            //need to drop first candle
            var sticks = candleSticks.Reverse().Skip(1);
            foreach (var action in callback)
            {
                action.Invoke(sticks);
            }

        }

        protected IEnumerable<DateTime> SplitDates(CandlestickInterval interval, DateTime from, DateTime to)
        {
            switch (interval)
            {
                case CandlestickInterval.Minute:
                    return GenerateTimeList(1, from, to);
                case CandlestickInterval.Minutes_3:
                    return GenerateTimeList(3, from, to);
                case CandlestickInterval.Minutes_5:
                    return GenerateTimeList(5, from, to);
                case CandlestickInterval.Minutes_15:
                    return GenerateTimeList(15, from, to);
                case CandlestickInterval.Minutes_30:
                    return GenerateTimeList(30, from, to);
                case CandlestickInterval.Hour:
                    return GenerateTimeList(1 * 60, from, to);
                case CandlestickInterval.Hours_2:
                    return GenerateTimeList(2 * 60, from, to);
                case CandlestickInterval.Hours_4:
                    return GenerateTimeList(4 * 60, from, to);
                case CandlestickInterval.Hours_6:
                    return GenerateTimeList(6 * 60, from, to);
                case CandlestickInterval.Hours_8:
                    return GenerateTimeList(8 * 60, from, to);
                case CandlestickInterval.Hours_12:
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

