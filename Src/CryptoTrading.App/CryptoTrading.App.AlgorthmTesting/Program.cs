using Binance;
using CryptoTrading.App.Algorthm;
using CryptoTrading.App.Algorthm.TradingStrategies;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Extensions;
using CryptoTrading.App.MarketData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CryptoTrading.App.AlgorthmTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            var filePath = @"C:\Temp\AlgoLoggingTest.txt";
            var services = new ServiceCollection()
                    .AddLogging(builder => builder // configure logging.
                        .SetMinimumLevel(LogLevel.Trace)
                        .AddFile(filePath, LogLevel.Information)
                        //.AddConsole()
                        )
                    .AddAlgorthm()
                    .AddDbMarketData()
                    .BuildServiceProvider();

            var marketData = services.GetService<IMarketData>();
            WireMarketDataEvents(marketData,services);
            //replay candle sticks

            marketData.StartStream();
            //var liveData = data.Where(x => x.Item1 == "Live").Select(x => x.Item2);
            //foreach (var item in liveData)
            //{
            //    Algo.ProcessLiveCandleStick(new Binance.Client.CandlestickEventArgs(DateTime.Now, item, 0, 0, true));
            //}
            //use the message broker to see the messages and output them to a file.

        }

        private static void WireMarketDataEvents(IMarketData marketData, ServiceProvider services)
        {
            marketData.Configure(null);
            marketData.From = new DateTime(2020, 11, 22);
            List<Symbol> symbols = new List<Symbol>()
            {
                Symbol.ETH_BTC,
                //Symbol.BTC_USDT,
                //Symbol.LTC_BTC,
                //Symbol.BNB_BTC,
                //Symbol.EOS_BTC,
                //Symbol.SYS_BTC,
                //Symbol.TRX_BTC,
                //Symbol.XRP_BTC
            };
            List<CandlestickInterval> intervals = new List<CandlestickInterval>()
            {
                CandlestickInterval.Minutes_15
            };
            AddEvents(marketData as AbstractMarketData, symbols, intervals, services);
        }

        private static void AddEvents(AbstractMarketData marketDate, List<Symbol> symbols, List<CandlestickInterval> intervals, ServiceProvider services)
        {
            foreach (var symbol in symbols)
            {
                foreach (var interval in intervals)
                {
                    var algo = services.GetService<IAlgorthm>();
                    marketDate.InitialDataLoadSubscribe(symbol, interval, algo.ProcessHistoricMarketData);
                    marketDate.InitialDataStreamSubscribe(symbol, interval, algo.ProcessLiveCandleStick);
                }
            }
        }


        private static IEnumerable<(string, Candlestick)> LoadFile()
        {
            char[] seperators = { ',' };
            using (StreamReader reader = new StreamReader(Directory.GetCurrentDirectory() + @"/MarketDataTest.csv"))
            {
                string line = reader.ReadLine();//skips the columns
                while ((line = reader.ReadLine()) != null)
                {
                    var read = line.Split(seperators, StringSplitOptions.None);
                    var candleStick = new Candlestick(read[1], CandlestickInterval.Minutes_15,
                        Convert.ToDateTime(read[6]), Convert.ToDecimal(read[2]), Convert.ToDecimal(read[3]),
                        Convert.ToDecimal(read[4]), Convert.ToDecimal(read[5]), 0, Convert.ToDateTime(read[7]), 0, 0, 0, 0);
                    yield return (read[0], candleStick);
                }
            }
        }
    }
}
