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
using CryptoTrading.App.Core.Trade;
using System.Text;
using System.Linq;
using Tulip;
using CryptoTrading.App.Core.Database.Indicators;
using CryptoTrading.App.Core.Database.StoreTrades;
using System.Threading.Tasks;

namespace CryptoTrading.App.AlgorthmTesting
{
    public class RunContext
    {
        void AddPosition(string symbol, Dictionary<string, IPosition> positions, decimal amount = 0.0m)
        {
            positions.Add(symbol, new Position(symbol, amount));

        }

        public Indicator stratgy { get; set; }
        public double NoOfTrades { get; set; }
        public decimal Risk { get; set; }
        public decimal Increment { get; set; }
        public IMarketData marketData { get; set; }
        public ITradeProcessor tradeMonitor { get; set; }
        public string Key => stratgy.FullName + NoOfTrades + Risk + Increment;

        private IServiceScope _scope;
        public void RunApp(ServiceProvider masterServices)
        {
            Dictionary<string, IPosition> dictionaryPositions = new Dictionary<string, IPosition>();
            AddPosition("ETH", dictionaryPositions);
            AddPosition("LTC", dictionaryPositions);
            AddPosition("EOS", dictionaryPositions);
            AddPosition("SYS", dictionaryPositions);
            AddPosition("TRX", dictionaryPositions);
            AddPosition("XRP", dictionaryPositions);
            AddPosition("ADA", dictionaryPositions);
            AddPosition("DOGE", dictionaryPositions);
            AddPosition("LINK", dictionaryPositions);
            AddPosition("QTUM", dictionaryPositions);
            AddPosition("XLM", dictionaryPositions);
            AddPosition("ONT", dictionaryPositions);
            AddPosition("BTC", dictionaryPositions, 1);
            AddPosition("BNB", dictionaryPositions, 5);

            //if (!Directory.Exists($@"C:\Temp\{ stratgy.FullName }"))
            //{
            //    Directory.CreateDirectory($@"C:\Temp\{ stratgy.FullName }");
            //}

            //var filePath = $@"C:\Temp\{stratgy.FullName}\AlgoLoggingTest_{NoOfTrades}_{Risk}_{Increment}.txt";
            var services = new ServiceCollection()
                    .AddKey(Key)
                    .AddAlgorthm(NoOfTrades, Risk, Increment)
                    .AddTestBroker()
                    .AddTradeMonitor(dictionaryPositions, masterServices)
                    .BuildServiceProvider();
            var serviceScopeFactory = services.GetRequiredService<IServiceScopeFactory>();
            _scope = serviceScopeFactory.CreateScope();
            
                //Service1
            WireMarketDataEvents(marketData, services);
            //Service2
            var broker = _scope.ServiceProvider.GetService<IBroker>();
            //Service3

            tradeMonitor = _scope.ServiceProvider.GetService<ITradeProcessor>();
            
        }

        public void CompleteApp()
        {
            tradeMonitor.CompleteAllTransactions();
            _scope.Dispose();
            //need to add summary of trades and PnL

            Console.WriteLine(PrintTrades(tradeMonitor.Trades));
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine(PrintSummary(tradeMonitor.Trades));
            
            //File.WriteAllLines($@"C:\temp\{stratgy.FullName}\TradeResults_{NoOfTrades}_{Risk}_{Increment}.txt", new List<string>() { PrintTrades(tradeMonitor.Trades), Environment.NewLine, PrintSummary(tradeMonitor.Trades) });
            //Load to Database

            using (var context = new TradeContext())
            {
                tradeMonitor.Trades.ForEach(x => context.Trades.Add(new TradesDb(x, stratgy, NoOfTrades, Risk, Increment)));
                context.SaveChanges();
            }
        }

        private string PrintSummary(List<ITrade> trades)
        {
            var count = trades.Count();
            var sb = new StringBuilder();
            sb.Append($"Total Number of Trades: [{count}]");
            sb.Append(Environment.NewLine);
            sb.Append($"Winning Trades: [{trades.Count(x => x.Profit > 0)}] - {((double)trades.Count(x => x.Profit > 0) / count) * 100}%");
            sb.Append(Environment.NewLine);
            sb.Append($"Losing Trades: [{trades.Count(x => x.Profit < 0)}] - {((double)trades.Count(x => x.Profit < 0) / count) * 100}%");
            sb.Append(Environment.NewLine);
            sb.Append($"Total Profit: [{trades.Sum(x => x.Profit)}]%");
            sb.Append(Environment.NewLine);

            return sb.ToString();
        }

        private string PrintTrades(List<ITrade> trades)
        {
            return trades.ToStringTable(x => x.Symbol,
                x => x.StartPrice, //.FirstTransaction.Price.ToString("0.#########"), 
                x => x.Price,//.CurrentTransaction.Price.ToString("0.#########"), 
                x => x.Quantity, //CurrentTransaction.Base.Quantity.ToString("0.####"), 
                x => x.StartDate, //FirstTransaction.TransactionDate.ToString(), 
                x => x.CloseDate, //CurrentTransaction.TransactionDate.ToString(), 
                x => x.Profit);
        }

        private void WireMarketDataEvents(IMarketData marketData, ServiceProvider services)
        {
            List<Symbol> symbols = new List<Symbol>()
            {
                Symbol.ETH_BTC,
                Symbol.BTC_USDT,
                Symbol.LTC_BTC,
                Symbol.BNB_BTC,
                Symbol.EOS_BTC,
                Symbol.SYS_BTC,
                //Symbol.TRX_BTC,
                Symbol.XRP_BTC ,
                Symbol.ADA_BTC,
                //Symbol.DOGE_BTC,
                Symbol.LINK_BTC,
                Symbol.QTUM_BTC,
                Symbol.XLM_BTC,
                Symbol.ONT_BTC
            };
            List<CandlestickInterval> intervals = new List<CandlestickInterval>()
            {
                CandlestickInterval.Minutes_30
            };
            AddEvents(marketData as AbstractMarketData, symbols, intervals, services);
        }

        private void AddEvents(AbstractMarketData marketDate, List<Symbol> symbols, List<CandlestickInterval> intervals, ServiceProvider services)
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


        private IEnumerable<(string, Candlestick)> LoadFile()
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
