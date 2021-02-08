using Binance;
using CryptoTrading.App.Algorthm;
using CryptoTrading.App.Core;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core.Extensions;
using CryptoTrading.App.MarketData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using CryptoTrading.App.Monitor;
using CryptoTrading.App.Core.Position;
using System.Threading;

namespace CryptoTrading.App.AlgorthmTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            Dictionary<string, IPosition> dictionaryPositions = new Dictionary<string, IPosition>();
            dictionaryPositions.Add("ETH", new Position("ETH", 0m));
            dictionaryPositions.Add("LTC", new Position("LTC", 0m));
            dictionaryPositions.Add("TRX", new Position("TRX", 0m));
            dictionaryPositions.Add("XRP", new Position("XRP", 0m));
            dictionaryPositions.Add("EOS", new Position("EOS", 0m));
            dictionaryPositions.Add("USDT", new Position("USDT", 0m));
            dictionaryPositions.Add("SYS", new Position("SYS", 0m));
            dictionaryPositions.Add("BTC", new Position("BTC", 1));
            dictionaryPositions.Add("BNB", new Position("BNB", 5));

            var filePath = @"C:\Temp\AlgoLoggingTest.txt";
            var services = new ServiceCollection()
                    .AddLogging(builder => builder // configure logging.
                        .SetMinimumLevel(LogLevel.Trace)
                        .AddFile(filePath, LogLevel.Information)
                        //.AddConsole()
                        )
                    .AddAlgorthm()
                    .AddDbMarketData()
                    .AddTestBroker()
                    .AddTradeMonitor(RunTypeEnum.BackTesting, dictionaryPositions)
                    .BuildServiceProvider();

            //Service1
            var marketData = services.GetService<IMarketData>();
            WireMarketDataEvents(marketData,services);
            //Service2
            var broker = services.GetService<IBroker>();
            //Service3

            var tradeMonitor = services.GetService<ITradeProcessor>();

            marketData.StartStream();
            tradeMonitor.CompleteAllTransactions();

            Thread.Sleep(100000);


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
            marketData.From = new DateTime(2020, 11, 22,00,00,00);
            List<Symbol> symbols = new List<Symbol>()
            {
                Symbol.SYS_BTC,
                Symbol.EOS_BTC,
                Symbol.BTC_USDT,
                Symbol.BNB_BTC,
                Symbol.LTC_BTC,
                Symbol.XRP_BTC,
                Symbol.TRX_BTC,
                Symbol.ETH_BTC
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
