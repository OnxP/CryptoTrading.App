using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using Binance.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using Binance;
using Binance.Client;
using Binance.Application;
using Binance.Utility;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using System.Security.Cryptography.X509Certificates;

namespace CryptoTrading.App.MarketData
{
    public class HistoricalMarketData : IMarketData
    {
        public DateTime From { get; set; }
        DateTime To { get; set; }

        ICandlestickClient _client;
        IBinanceWebSocketStream _webSocket;

        private IDictionary<(string symbol, CandlestickInterval interval), Action<IEnumerable<Candlestick>>> _historicDataSubscribers = new Dictionary<(string symbol, CandlestickInterval interval), Action<IEnumerable<Candlestick>>>();
        private IDictionary<(string symbol, CandlestickInterval interval), IList<Action<CandlestickEventArgs>>> _subscribers = new Dictionary<(string symbol, CandlestickInterval interval), IList<Action<CandlestickEventArgs>>>();
        //public events 

        public void InitialDataLoadSubscribe(string symbol, CandlestickInterval interval,Action<IEnumerable<Candlestick>> callback)
        {
            _historicDataSubscribers.Add((symbol, interval), callback);
        }
        public void InitialDataLoadUnSubscribe(string symbol, CandlestickInterval interval)
        {
            _historicDataSubscribers.Remove((symbol, interval));
        }

        public void InitialDataStreamSubscribe(string symbol, CandlestickInterval interval, Action<CandlestickEventArgs> callback)
        {
            if(!_subscribers.ContainsKey((symbol,interval)))
            {
                _subscribers.Add((symbol, interval), new List<Action<CandlestickEventArgs>>());
            }
            _subscribers[(symbol, interval)].Add(callback);
        }

        public void InitialDataStreamUnSubscribe(string symbol, CandlestickInterval interval, Action<CandlestickEventArgs> callback)
        {
            _subscribers.Remove((symbol, interval));
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

            // Initialize the stream.
            _webSocket = services.GetService<IBinanceWebSocketStream>();
            _webSocket.Message += (s, e) => _client.HandleMessage(e.Subject, e.Json);

        }

        public void StartStream()
        {
            try
            {
                var api = new BinanceApi();
                var tasks = new List<Task>();
                foreach (var item in _historicDataSubscribers)
                {
                    tasks.Add(LoadHistoricData(api, item.Key, From, item.Value));
                }
                Task.WaitAll(tasks.ToArray());

                tasks = new List<Task>();
                //LoadData


                //StreamHistoricData
                foreach (var item in _subscribers)
                {
                    var from = From;
                    foreach (var to in SplitDates(item.Key.interval, from, DateTime.Now.ToUniversalTime()))
                    {
                        tasks.Add(StreamData(api, item.Key, From, to));
                        from = to;
                        if(tasks.Count % 50 == 0)
                        {
                            Thread.Sleep(60000);
                        }
                    }
                }
                Task.WaitAll(tasks.ToArray());

                //sort the list
                var orderedList = candleSticksToStream.OrderBy(x => x.candlestick.CloseTime);

                foreach (var item in orderedList.GroupBy(x=>x.candlestick.CloseTime))
                {
                    foreach (var candleStick in item)
                    {
                        foreach(var action in _subscribers[(candleStick.candlestick.Symbol,candleStick.interval)])
                        {
                            action.Invoke(new CandlestickEventArgs(item.Key, candleStick.candlestick,0,0,true));
                        }
                    }
                }

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
        private static readonly object _sync = new object();

        private async System.Threading.Tasks.Task StreamData(BinanceApi api, (string symbol, CandlestickInterval interval) symbol, DateTime from, DateTime to)
        {
            var candleSticks = await api.GetCandlesticksAsync(symbol.symbol, symbol.interval, 500, from.ToUniversalTime(), to.ToUniversalTime());
            foreach (var candleStick in candleSticks)
            {
                lock (_sync)
                {
                    candleSticksToStream.Add((candleStick, symbol.interval));
                }
            }
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
            int candleSticksToLoad = 100;
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

        private static void HandleError(Exception e)
        {
            //lock (_sync)
            //{
                Console.WriteLine(e.Message);
            //}
        }

        private async System.Threading.Tasks.Task LoadHistoricData(BinanceApi api, (string symbol,CandlestickInterval interval) symbol, DateTime from, Action<IEnumerable<Candlestick>> callback)
        {
            var calculatedFrom = CalculateFrom(from, symbol.interval).ToUniversalTime();
            var candleSticks = await api.GetCandlesticksAsync(symbol.symbol, symbol.interval, 0, calculatedFrom, from.ToUniversalTime());
            //need to drop first candle
            var sticks = candleSticks.Reverse().Skip(1);
            callback.Invoke(sticks);
        }

        

        protected IEnumerable<DateTime> SplitDates(CandlestickInterval interval, DateTime from, DateTime to)
        {
            switch (interval)
            {
                case CandlestickInterval.Minutes_3:
                case CandlestickInterval.Minutes_5:
                case CandlestickInterval.Minutes_15:
                case CandlestickInterval.Minutes_30:
                    return SplitByDay(from, to);
                case CandlestickInterval.Hour:
                case CandlestickInterval.Hours_2:
                case CandlestickInterval.Hours_4:
                case CandlestickInterval.Hours_6:
                case CandlestickInterval.Hours_8:
                case CandlestickInterval.Hours_12:
                    return SplitByWeek(from, to);
                default:
                    return new List<DateTime> { to };
            }
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
            for (int i = 0; i < daysdiff; i+=7)
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

    

