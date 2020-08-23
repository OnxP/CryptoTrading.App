using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Binance;
using Binance.Application;
using Binance.Client;
using Binance.Utility;
using Binance.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketDataTesting
{
    class Program
    {
        public static void Main(string[] args)
        {
            var api = new BinanceApi();

            //// Check connectivity.
            //System.Threading.Tasks.Task task = PingAsync(api);
            //task.Wait();

            LiveStream.StreamData();
            //LoadHistoricData(api);

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

    }
}