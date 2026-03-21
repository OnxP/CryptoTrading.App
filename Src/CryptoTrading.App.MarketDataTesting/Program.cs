using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CryptoTrading.App.Core.Exchange;
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
                .AddOptions()
                .BuildServiceProvider();

            // TODO: Register IExchangeProvider via DI instead of direct instantiation
            var provider = ServiceProvider.GetService<IExchangeProvider>();

            writer = new StreamWriter(File.Open(@"C:\temp\MarketDataTest.csv",FileMode.OpenOrCreate));
            writer.WriteLine($"Historic,Symbol,Open,High,Low,Close,Open Time ,Close Time");
            IMarketData marketDate = ServiceProvider.GetService<IMarketData>();
            marketDate.Configure(null);
            marketDate.From = new DateTime(2020, 08, 24);
            //subscribe to several symbols
            AddEvents(marketDate as IMarketDataEvents);

            var controller = marketDate.GetTaskController();
            controller.Begin();

            writer.Flush();
            writer.Close();
        }
        static StreamWriter writer;
        private static readonly object _sync = new object();

        private static void DisplayCandleStick(ExchangeCandlestickEvent obj)
        {
            lock (_sync)
            {
                var candlestick = obj.Candlestick;
                Console.WriteLine($"Live,  {candlestick.Symbol} - O: {candlestick.Open:0.00000000} | C: {candlestick.Close:0.00000000} - [{candlestick.OpenTime.ToLongTimeString()}] - [{candlestick.CloseTime.ToLongTimeString()}]".PadRight(119));
                writer.WriteLine($"Live,{candlestick.Symbol},{candlestick.Open:0.00000000},{candlestick.High:0.00000000},{candlestick.Low:0.00000000},{candlestick.Close:0.00000000},{candlestick.OpenTime:dd/MM/yyyy HH:mm:ss},{candlestick.CloseTime:dd/MM/yyyy HH:mm:ss}");
            }
        }

        private static void DisplayHistoricCandleStick(IEnumerable<ExchangeCandlestick> obj)
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

        private static void LoadHistoricData(IExchangeProvider provider)
        {
            var from = new DateTime(2020, 01, 29);
            var to = new DateTime(2020, 07, 29);
            var interval = CandleInterval.Hour_1;

            foreach (DateTime dt in SplitDates(interval, from, to))
            {
                var task = LoadHistoricData(provider, "BNBBTC", from, dt);
                from = dt;
                task.Wait();
            }
        }

        private static async System.Threading.Tasks.Task LoadHistoricData(IExchangeProvider provider, string symbol, DateTime from, DateTime to)
        {
            var test = await provider.GetCandlesticksAsync(symbol, CandleInterval.Hour_1, from.ToUniversalTime(), to.ToUniversalTime());

            foreach (var item in test)
            {
                Console.WriteLine(item.OpenTime.ToString() + " - " + item.CloseTime.ToString());
            }
        }

        protected static IEnumerable<DateTime> SplitDates(CandleInterval interval, DateTime from, DateTime to)
        {
            switch (interval)
            {
                case CandleInterval.Minute_3:
                case CandleInterval.Minute_5:
                case CandleInterval.Minute_15:
                case CandleInterval.Minute_30:
                    return SplitByDay(from, to);
                case CandleInterval.Hour_1:
                case CandleInterval.Hour_2:
                case CandleInterval.Hour_4:
                case CandleInterval.Hour_6:
                case CandleInterval.Hour_8:
                case CandleInterval.Hour_12:
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

        private static void AddEvents(IMarketDataEvents marketDate)
        {
            marketDate.InitialDataLoadSubscribe("ETHBTC", CandleInterval.Minute_15, DisplayHistoricCandleStick);
            marketDate.InitialDataStreamSubscribe("ETHBTC", CandleInterval.Minute_15, DisplayCandleStick);
        }

    }
}
