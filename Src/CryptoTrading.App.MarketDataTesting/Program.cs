using System;
using System.Collections.Generic;
using System.IO;
using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using IMarketDataEvents = CryptoTrading.App.Core.IMarketDataEvents;

namespace CryptoTrading.App.MarketDataTesting
{
    class Program
    {
        public static void Main(string[] args)
        {
            var Configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", true, false)
                    .AddUserSecrets<Program>() // for access to API key and secret.
                    .Build();

            // Configure services.
            var ServiceProvider = new ServiceCollection()
                // ReSharper disable once ArgumentsStyleLiteral
                .AddBinance(useSingleCombinedStream: true) // add default Binance services.

                // Use alternative, low-level, web socket client implementation.
                //.AddTransient<IWebSocketClient, WebSocket4NetClient>()
                //.AddTransient<IWebSocketClient, WebSocketSharpClient>()

                .AddOptions()
                .Configure<BinanceApiOptions>(Configuration.GetSection("ApiOptions"))


                .BuildServiceProvider();
            var api = ServiceProvider.GetService<IBinanceApi>();

            //// Check connectivity.
            //System.Threading.Tasks.Task task = PingAsync(api);
            //task.Wait();

            //LiveStream.StreamData();
            //LoadHistoricData(api);
            //var logger = ServiceProvider.
            writer = new StreamWriter(File.Open(@"C:\temp\MarketDataTest.csv",FileMode.OpenOrCreate));
            writer.WriteLine($"Historic,Symbol,Open,High,Low,Close,Open Time ,Close Time");
            IMarketData marketDate = ServiceProvider.GetService<IMarketData>();
            marketDate.Configure(null);
            marketDate.From = new DateTime(2020, 08, 24);
            //subscribe to several symbols
            AddEvents(marketDate as IMarketDataEvents);

            marketDate.StartStream();

            writer.Flush();
            writer.Close();
        }
        static StreamWriter writer;
        private static readonly object _sync = new object();

        private static void DisplayCandleStick(CandlestickEventArgs obj)
        {
            lock (_sync)
            {
                var candlestick = obj.Candlestick;
                Console.WriteLine($"Live,  {candlestick.Symbol} - O: {candlestick.Open:0.00000000} | C: {candlestick.Close:0.00000000} - [{candlestick.OpenTime.ToLongTimeString()}] - [{candlestick.CloseTime.ToLongTimeString()}]".PadRight(119));
                writer.WriteLine($"Live,{candlestick.Symbol},{candlestick.Open:0.00000000},{candlestick.High:0.00000000},{candlestick.Low:0.00000000},{candlestick.Close:0.00000000},{candlestick.OpenTime:dd/MM/yyyy HH:mm:ss},{candlestick.CloseTime:dd/MM/yyyy HH:mm:ss}");
            }
        }

        private static void DisplayHistoricCandleStick(IEnumerable<Candlestick> obj)
        {
            lock (_sync)
            {
                foreach (var candlestick in obj)
                {
                    Console.WriteLine($"Historic,  {candlestick.Symbol} - O: {candlestick.Open:0.00000000} | C: {candlestick.Close:0.00000000} - [{candlestick.OpenTime.ToLongTimeString()}] - [{candlestick.CloseTime.ToLongTimeString()}]".PadRight(119));
                    writer.WriteLine($"Historic,{candlestick.Symbol},{candlestick.Open:0.00000000},{candlestick.High:0.00000000},{candlestick.Low:0.00000000},{candlestick.Close:0.00000000},{candlestick.OpenTime:dd/MM/yyyy HH:mm:ss},{candlestick.CloseTime:dd/MM/yyyy HH:mm:ss}");
                }
            }
        }

        private static void LoadHistoricData(BinanceApi api)
        {
            var from = new DateTime(2020, 01, 29);
            var to = new DateTime(2020, 07, 29);
            var interval = CandlestickInterval.Hour;

            foreach (DateTime dt in SplitDates(interval, from, to))
            {
                var task = LoadHistoricData(api, "BNBBTC", from, dt);
                from = dt;
                task.Wait();
            }
        }

        private static async System.Threading.Tasks.Task LoadHistoricData(BinanceApi api, string symbol, DateTime from, DateTime to)
        {
            var test = await api.GetCandlesticksAsync(symbol, CandlestickInterval.Hour, 500, from.ToUniversalTime(), to.ToUniversalTime());

            foreach (var item in test)
            {
                Console.WriteLine(item.OpenTime.ToString() + " - " + item.CloseTime.ToString());
            }
        }

        protected static IEnumerable<DateTime> SplitDates(CandlestickInterval interval, DateTime from, DateTime to)
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

        protected static IEnumerable<DateTime> SplitByWeek(DateTime from, DateTime to)
        {
            var daysdiff = (to - from).TotalDays;
            for (int i = 0; i < daysdiff; i++)
            {
                yield return from.AddDays(i);
            }
        }

        protected static IEnumerable<DateTime> SplitByDay(DateTime from, DateTime to)
        {
            var daysdiff = (to - from).TotalDays;
            for (int i = 1; i <= daysdiff; i++)
            {
                yield return from.AddDays(i);
            }
        }

        public static async System.Threading.Tasks.Task PingAsync(BinanceApi api)
        {
            if (await api.PingAsync())
            {
                Console.WriteLine("Successful!");
            }
        }

        private static void AddEvents(IMarketDataEvents marketDate)
        {
            marketDate.InitialDataLoadSubscribe(Symbol.ETH_BTC, CandlestickInterval.Minutes_15, DisplayHistoricCandleStick);
            //marketDate.InitialDataLoadSubscribe(Symbol.XRP_BTC, CandlestickInterval.Hours_2, DisplayHistoricCandleStick);
            //marketDate.InitialDataLoadSubscribe(Symbol.SYS_BTC, CandlestickInterval.Hour, DisplayHistoricCandleStick);
            //marketDate.InitialDataLoadSubscribe(Symbol.SYS_BTC, CandlestickInterval.Hours_2, DisplayHistoricCandleStick);
            //marketDate.InitialDataLoadSubscribe(Symbol.NEO_BTC, CandlestickInterval.Hour, DisplayHistoricCandleStick);
            //marketDate.InitialDataLoadSubscribe(Symbol.NEO_BTC, CandlestickInterval.Hours_2, DisplayHistoricCandleStick);
            //marketDate.InitialDataLoadSubscribe(Symbol.NEO_BTC, CandlestickInterval.Hours_6, DisplayHistoricCandleStick);
            //marketDate.InitialDataLoadSubscribe(Symbol.LTC_BTC, CandlestickInterval.Hour, DisplayHistoricCandleStick);

            marketDate.InitialDataStreamSubscribe(Symbol.ETH_BTC, CandlestickInterval.Minutes_15, DisplayCandleStick);
            //marketDate.InitialDataStreamSubscribe(Symbol.XRP_BTC, CandlestickInterval.Hours_2, DisplayCandleStick);
            //marketDate.InitialDataStreamSubscribe(Symbol.SYS_BTC, CandlestickInterval.Hour, DisplayCandleStick);
            //marketDate.InitialDataStreamSubscribe(Symbol.SYS_BTC, CandlestickInterval.Hours_2, DisplayCandleStick);
            //marketDate.InitialDataStreamSubscribe(Symbol.NEO_BTC, CandlestickInterval.Hour, DisplayCandleStick);
            //marketDate.InitialDataStreamSubscribe(Symbol.NEO_BTC, CandlestickInterval.Hours_2, DisplayCandleStick);
            //marketDate.InitialDataStreamSubscribe(Symbol.NEO_BTC, CandlestickInterval.Hours_6, DisplayCandleStick);
            //marketDate.InitialDataStreamSubscribe(Symbol.LTC_BTC, CandlestickInterval.Hour, DisplayCandleStick);
        }

    }
}