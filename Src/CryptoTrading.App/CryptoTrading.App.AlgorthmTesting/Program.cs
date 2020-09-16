using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Binance;
using CryptoTrading.App.Algorthm.TradingStrategies;
using CryptoTrading.App.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;

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
                    .BuildServiceProvider();


            var list = new List<ITradingStrategy>();
            var logger = services.GetService<ILoggerProvider>().CreateLogger("");
            list.Add(new EmaTradingStrategy(logger));
            //build Algo
            var Algo = new Algorthm.Algorthm(list, logger);
            //replay candle sticks
            var data = LoadFile();

            var historicCandleSticks = data.Where(x=>x.Item1 == "Historic").Select(x => x.Item2).OrderBy(x => x.CloseTime);
            
            Algo.ProcessHistoricMarketData(historicCandleSticks);
            var liveData = data.Where(x => x.Item1 == "Live").Select(x => x.Item2);
            foreach (var item in liveData)
            {
                Algo.ProcessLiveCandleStick(new Binance.Client.CandlestickEventArgs(DateTime.Now, item, 0, 0, true));
            }
            //use the message broker to see the messages and output them to a file.

        }

        private static IEnumerable<Candlestick> GenerateLiveData()
        {
            throw new NotImplementedException();
        }

        private static IEnumerable<Candlestick> GenerateHistoricCandleSticks()
        {
            throw new System.NotImplementedException();
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
                    var candleStick = new Candlestick(read[1],CandlestickInterval.Minutes_15,
                        Convert.ToDateTime(read[6]), Convert.ToDecimal(read[2]), Convert.ToDecimal(read[3]),
                        Convert.ToDecimal(read[4]), Convert.ToDecimal(read[5]), 0, Convert.ToDateTime(read[7]),0,0,0,0);
                    yield return (read[0], candleStick);
                }
            }
        }
    }
}
