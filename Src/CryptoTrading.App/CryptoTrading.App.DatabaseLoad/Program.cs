using System;
using System.IO;
using Binance;
using Binance.Application;
using CryptoTrading.App.Core;
using CryptoTrading.App.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Binance.Application;
using Binance.Application.Logging;
using Microsoft.Extensions.Logging;
using IMarketDataEvents = CryptoTrading.App.Core.IMarketDataEvents;
using System.Collections.Generic;
using Binance.Client;
using System.Data.Entity;

namespace CryptoTrading.App.DatabaseLoad
{
    class Program
    {
        static void Main(string[] args)
        {
            var Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, false)
                .AddUserSecrets<Program>() // for access to API key and secret.
                .Build();

            // Configure services.
            var ServiceProvider = new ServiceCollection()
                .AddBinance(useSingleCombinedStream: true)
                .AddHistoricMarketData()
                // Configure logging.
                .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace)
                    .AddFile(Configuration.GetSection("Logging:File")))

                .BuildServiceProvider();
            context.Database.ExecuteSqlCommand("TRUNCATE TABLE myCandlesticks");
            IMarketData marketDate = ServiceProvider.GetService<IMarketData>();
            marketDate.Configure(null);
            marketDate.From = new DateTime(2020, 12, 22);
            List<Symbol> symbols = new List<Symbol>() 
            { 
                Symbol.ETH_BTC,
                Symbol.BTC_USDT,
                Symbol.LTC_BTC,
                Symbol.BNB_BTC,
                Symbol.EOS_BTC,
                Symbol.SYS_BTC,
                Symbol.TRX_BTC,
                Symbol.XRP_BTC 
            };
            List<CandlestickInterval> intervals = new List<CandlestickInterval>()
            {
              CandlestickInterval.Minute
            , CandlestickInterval.Minutes_15
            , CandlestickInterval.Minutes_30
            , CandlestickInterval.Minutes_5
            , CandlestickInterval.Hour
            , CandlestickInterval.Hours_4
            , CandlestickInterval.Day
            };
        
            //subscribe to several symbols
            AddEvents(marketDate as AbstractMarketData, symbols,intervals);

            marketDate.StartStream();
            context.Dispose();

        }

        private static void AddEvents(AbstractMarketData marketDate, List<Symbol> symbols, List<CandlestickInterval> intervals)
        {
            foreach (var symbol in symbols)
            {
                foreach (var interval in intervals)
                {
                    marketDate.InitialDataLoadSubscribe(symbol, interval, SaveHistoricCandleStick);
                    marketDate.InitialDataStreamSubscribe(symbol, interval, SaveCandleStick);
                }
            }
        }
        public static MyContext context = new MyContext();
        private static void SaveCandleStick(CandlestickEventArgs obj)
        {
            lock (_object)
            {
                context.CandleSticks.Add(new MyCandleStick(obj.Candlestick));
                context.SaveChanges();
            }
        }
        static object _object = new object();
        private static void SaveHistoricCandleStick(IEnumerable<Candlestick> obj)
        {
            lock (_object)
            {
                foreach (var candlestick in obj)
                {
                    context.CandleSticks.Add(new MyCandleStick(candlestick));
                    context.SaveChanges();
                }
            }
        }

        public class MyContext : DbContext
        {
            public MyContext() : base(@"Data Source=AnkurPC\AnkurPC;Initial Catalog=CryptoDb;Integrated Security=True") {}
            public virtual DbSet<MyCandleStick> CandleSticks { get; set; }
        }

        public class MyCandleStick
        {
            public MyCandleStick(Candlestick candlestick)
            {
                Symbol = candlestick.Symbol;
                Interval = candlestick.Interval;
                OpenTime = candlestick.OpenTime;
                Open = Convert.ToDouble(candlestick.Open);
                High = Convert.ToDouble(candlestick.High);
                Low = Convert.ToDouble(candlestick.Low);
                Close = Convert.ToDouble(candlestick.Close);
                Volume = candlestick.Volume;
                CloseTime = candlestick.CloseTime;
                QuoteAssetVolume = candlestick.QuoteAssetVolume;
                NumberOfTrades = candlestick.NumberOfTrades;
                TakerBuyBaseAssetVolume = candlestick.TakerBuyBaseAssetVolume;
                TakerBuyQuoteAssetVolume = candlestick.TakerBuyQuoteAssetVolume;
        }

            public int ID { get; set; }
            public string Symbol { get; set; }

            /// <summary>
            /// Get the interval.
            /// </summary>
            public CandlestickInterval Interval { get; set; }

            /// <summary>
            /// Get the open time.
            /// </summary>
            public DateTime OpenTime { get; set; }

            /// <summary>
            /// Get the open price in quote asset units.
            /// </summary>
            public double Open { get; set; }

            /// <summary>
            /// Get the high price in quote asset units.
            /// </summary>
            public double High { get; set; }

            /// <summary>
            /// Get the low price in quote asset units.
            /// </summary>
            public double Low { get; set; }

            /// <summary>
            /// Get the close price in quote asset units.
            /// </summary>
            public double Close { get; set; }

            /// <summary>
            /// Get the volume in base asset units.
            /// </summary>
            public decimal Volume { get; set; }

            /// <summary>
            /// Get the close time.
            /// </summary>
            public DateTime CloseTime { get; set; }

            /// <summary>
            /// Get the volume in quote asset units.
            /// </summary>
            public decimal QuoteAssetVolume { get; set; }

            /// <summary>
            /// Get the number of trades.
            /// </summary>
            public long NumberOfTrades { get; set; }

            /// <summary>
            /// Get the taker buy base asset volume.
            /// </summary>
            public decimal TakerBuyBaseAssetVolume { get; set; }

            /// <summary>
            /// Get the taker buy quote asset volume.
            /// </summary>
            public decimal TakerBuyQuoteAssetVolume { get; set; }
        }
    }
}
