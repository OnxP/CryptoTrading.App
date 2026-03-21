using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Algorithm;
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
using CryptoTrading.App.Core.Trade;
using System.Text;
using System.Linq;
using Tulip;
using CryptoTrading.App.Core.Database.StoreTrades;

namespace CryptoTrading.App.AlgorithmTesting
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
        public string CustomText { get; set; }
        public decimal Increment { get; set; }
        public IMarketData marketData { get; set; }
        public ITradeProcessor tradeMonitor { get; set; }
        public string Key => stratgy.FullName + NoOfTrades + Risk + Increment;

        private IServiceScope _scope;
        public void RunApp(ServiceProvider masterServices)
        {
            Dictionary<string, IPosition> dictionaryPositions = new Dictionary<string, IPosition>();
            foreach (var item in symbols)
            {
                AddPosition(item.BaseAsset, dictionaryPositions);
            }
            dictionaryPositions["BNB"].CreateTransaction(5);
            AddPosition("BTC", dictionaryPositions, 1);

            if (!Directory.Exists($@"C:\Temp\{ stratgy.FullName }"))
            {
                Directory.CreateDirectory($@"C:\Temp\{ stratgy.FullName }");
            }

            var filePath = $@"C:\Temp\{stratgy.FullName}\AlgoLoggingTest_{CustomText}_{NoOfTrades}_{Risk}_{Increment}.txt";
            var services = new ServiceCollection()
                                    .AddLogging(builder => builder // configure logging.
                        .SetMinimumLevel(LogLevel.Trace)
                        .AddFile(filePath,10000, LogLevel.Information)
                        .AddConsole()
                        )
                    .AddKey(Key)
                    .AddAlgorithm(NoOfTrades, Risk, Increment)
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
            var completedTrades = tradeMonitor.GetCompletedTrades();

            Console.WriteLine(PrintTrades(completedTrades));
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine(PrintSummary(tradeMonitor, completedTrades));

            File.WriteAllLines($@"C:\temp\{stratgy.FullName}\TradeResults_{CustomText}_{NoOfTrades}_{Risk}_{Increment}.txt",
                new List<string>() { PrintTrades(completedTrades), Environment.NewLine, PrintSummary(tradeMonitor, completedTrades) });
            //Load to Database

            using (var context = new TradeContext())
            {
                completedTrades.ForEach(x => context.Trades.Add(new TradesDb(x, stratgy, NoOfTrades, Risk, Increment)));
                context.SaveChanges();
            }
        }

        private string PrintSummary(ITradeProcessor processor, List<ITrade> completedTrades)
        {
            var count = completedTrades.Count();
            var sb = new StringBuilder();
            sb.Append($"Total Number of Trades: [{count}]");
            sb.Append(Environment.NewLine);
            sb.Append($"Winning Trades: [{completedTrades.Count(x => x.Profit > 0)}] - {((double)completedTrades.Count(x => x.Profit > 0) / count) * 100}%");
            sb.Append(Environment.NewLine);
            sb.Append($"Losing Trades: [{completedTrades.Count(x => x.Profit < 0)}] - {((double)completedTrades.Count(x => x.Profit < 0) / count) * 100}%");
            sb.Append(Environment.NewLine);
            sb.Append($"Total Profit: [{completedTrades.Sum(x => x.Profit)}]%");
            sb.Append(Environment.NewLine);
            sb.Append($"Total BTC: {Math.Round(processor.Positions.GetPosition("BTC").FreeAmount,4)} : {Math.Round((processor.Positions.GetPosition("BTC").FreeAmount-1) * 100,4)}%");
            sb.Append(Environment.NewLine);
            return sb.ToString();
        }

        private string PrintTrades(List<ITrade> trades)
        {
            return trades.ToStringTable(x => x.Pair,
                x => x.StartPrice, //.FirstTransaction.Price.ToString("0.#########"),
                x => x.Price,//.CurrentTransaction.Price.ToString("0.#########"),
                x => x.Quantity, //CurrentTransaction.Base.Quantity.ToString("0.####"),
                x => x.StartDate, //FirstTransaction.TransactionDate.ToString(),
                x => x.CloseDate, //CurrentTransaction.TransactionDate.ToString(),
                x => x.Profit);
        }

        List<ExchangeSymbol> symbols
        {
            get
            {
                return new List<ExchangeSymbol>()
            {
new ExchangeSymbol { Ticker = "SOLBTC", BaseAsset = "SOL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FETBTC", BaseAsset = "FET", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "MDABTC", BaseAsset = "MDA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "VIDTBTC", BaseAsset = "VIDT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SUSHIBTC", BaseAsset = "SUSHI", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RUNEBTC", BaseAsset = "RUNE", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "TROYBTC", BaseAsset = "TROY", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ARPABTC", BaseAsset = "ARPA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AAVEBTC", BaseAsset = "AAVE", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FXSBTC", BaseAsset = "FXS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "LITBTC", BaseAsset = "LIT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NANOBTC", BaseAsset = "NANO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "TRXBTC", BaseAsset = "TRX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AXSBTC", BaseAsset = "AXS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ANKRBTC", BaseAsset = "ANKR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PERPBTC", BaseAsset = "PERP", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "MATICBTC", BaseAsset = "MATIC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "VIBBTC", BaseAsset = "VIB", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "WNXMBTC", BaseAsset = "WNXM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NKNBTC", BaseAsset = "NKN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BNBBTC", BaseAsset = "BNB", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BNTBTC", BaseAsset = "BNT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "GRTBTC", BaseAsset = "GRT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "TCTBTC", BaseAsset = "TCT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "STRAXBTC", BaseAsset = "STRAX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "QKCBTC", BaseAsset = "QKC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DUSKBTC", BaseAsset = "DUSK", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NXSBTC", BaseAsset = "NXS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CELOBTC", BaseAsset = "CELO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "GXSBTC", BaseAsset = "GXS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SRMBTC", BaseAsset = "SRM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PHABTC", BaseAsset = "PHA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AUCTIONBTC", BaseAsset = "AUCTION", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "HIVEBTC", BaseAsset = "HIVE", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "POWRBTC", BaseAsset = "POWR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DNTBTC", BaseAsset = "DNT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RCNBTC", BaseAsset = "RCN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PHBBTC", BaseAsset = "PHB", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SUSDBTC", BaseAsset = "SUSD", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RVNBTC", BaseAsset = "RVN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ORNBTC", BaseAsset = "ORN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ATOMBTC", BaseAsset = "ATOM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DIABTC", BaseAsset = "DIA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "JUVBTC", BaseAsset = "JUV", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CTSIBTC", BaseAsset = "CTSI", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NASBTC", BaseAsset = "NAS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BRDBTC", BaseAsset = "BRD", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CNDBTC", BaseAsset = "CND", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "STORJBTC", BaseAsset = "STORJ", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ARKBTC", BaseAsset = "ARK", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CHZBTC", BaseAsset = "CHZ", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "UMABTC", BaseAsset = "UMA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "KMDBTC", BaseAsset = "KMD", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FILBTC", BaseAsset = "FIL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BCHBTC", BaseAsset = "BCH", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "VETBTC", BaseAsset = "VET", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SYSBTC", BaseAsset = "SYS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "YFIIBTC", BaseAsset = "YFII", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RDNBTC", BaseAsset = "RDN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SUPERBTC", BaseAsset = "SUPER", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "KAVABTC", BaseAsset = "KAVA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RSRBTC", BaseAsset = "RSR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DCRBTC", BaseAsset = "DCR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "XVSBTC", BaseAsset = "XVS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BELBTC", BaseAsset = "BEL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FUNBTC", BaseAsset = "FUN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "IOSTBTC", BaseAsset = "IOST", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "WRXBTC", BaseAsset = "WRX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SANDBTC", BaseAsset = "SAND", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "JSTBTC", BaseAsset = "JST", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "OMBTC", BaseAsset = "OM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SUNBTC", BaseAsset = "SUN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FIOBTC", BaseAsset = "FIO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PNTBTC", BaseAsset = "PNT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "GLMBTC", BaseAsset = "GLM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "TRBBTC", BaseAsset = "TRB", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PONDBTC", BaseAsset = "POND", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "WTCBTC", BaseAsset = "WTC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ONTBTC", BaseAsset = "ONT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DOGEBTC", BaseAsset = "DOGE", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "WPRBTC", BaseAsset = "WPR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FLMBTC", BaseAsset = "FLM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "WBTCBTC", BaseAsset = "WBTC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "MTLBTC", BaseAsset = "MTL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "TOMOBTC", BaseAsset = "TOMO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CFXBTC", BaseAsset = "CFX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "QTUMBTC", BaseAsset = "QTUM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "POABTC", BaseAsset = "POA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DATABTC", BaseAsset = "DATA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "THETABTC", BaseAsset = "THETA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "UTKBTC", BaseAsset = "UTK", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "GOBTC", BaseAsset = "GO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "VIABTC", BaseAsset = "VIA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "HARDBTC", BaseAsset = "HARD", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CKBBTC", BaseAsset = "CKB", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "GRSBTC", BaseAsset = "GRS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "KSMBTC", BaseAsset = "KSM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "LRCBTC", BaseAsset = "LRC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "STXBTC", BaseAsset = "STX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "IOTXBTC", BaseAsset = "IOTX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CRVBTC", BaseAsset = "CRV", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CTKBTC", BaseAsset = "CTK", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "OGBTC", BaseAsset = "OG", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "UNFIBTC", BaseAsset = "UNFI", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "GASBTC", BaseAsset = "GAS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "QSPBTC", BaseAsset = "QSP", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RIFBTC", BaseAsset = "RIF", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "MDTBTC", BaseAsset = "MDT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FRONTBTC", BaseAsset = "FRONT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ONGBTC", BaseAsset = "ONG", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ADABTC", BaseAsset = "ADA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CAKEBTC", BaseAsset = "CAKE", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PSGBTC", BaseAsset = "PSG", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PIVXBTC", BaseAsset = "PIVX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BQXBTC", BaseAsset = "BQX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SNTBTC", BaseAsset = "SNT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "XLMBTC", BaseAsset = "XLM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FTTBTC", BaseAsset = "FTT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SKYBTC", BaseAsset = "SKY", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "EGLDBTC", BaseAsset = "EGLD", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "STPTBTC", BaseAsset = "STPT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AEROBTC", BaseAsset = "AERGO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BATBTC", BaseAsset = "BAT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ZECBTC", BaseAsset = "ZEC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "UNIBTC", BaseAsset = "UNI", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BTSBTC", BaseAsset = "BTS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "APPCBTC", BaseAsset = "APPC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "REPBTC", BaseAsset = "REP", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CELRBTC", BaseAsset = "CELR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RENBTC", BaseAsset = "REN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ONEBTC", BaseAsset = "ONE", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "GVTBTC", BaseAsset = "GVT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "OSTBTC", BaseAsset = "OST", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "IRISBTC", BaseAsset = "IRIS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "KNCBTC", BaseAsset = "KNC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "MANABTC", BaseAsset = "MANA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NULSBTC", BaseAsset = "NULS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NEBLBTC", BaseAsset = "NEBL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "TRUBTC", BaseAsset = "TRU", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BALBTC", BaseAsset = "BAL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NEARBTC", BaseAsset = "NEAR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "COTIBTC", BaseAsset = "COTI", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DLTBTC", BaseAsset = "DLT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "XRPBTC", BaseAsset = "XRP", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ASRBTC", BaseAsset = "ASR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "YOYOBTC", BaseAsset = "YOYO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ETHBTC", BaseAsset = "ETH", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "EOSBTC", BaseAsset = "EOS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "HBARBTC", BaseAsset = "HBAR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AIONBTC", BaseAsset = "AION", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DOCKBTC", BaseAsset = "DOCK", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "LUNABTC", BaseAsset = "LUNA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NBSBTC", BaseAsset = "NBS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "LINKBTC", BaseAsset = "LINK", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "MTHBTC", BaseAsset = "MTH", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PPTBTC", BaseAsset = "PPT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DODOBTC", BaseAsset = "DODO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ETCBTC", BaseAsset = "ETC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "TVKBTC", BaseAsset = "TVK", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "HNTBTC", BaseAsset = "HNT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PAXGBTC", BaseAsset = "PAXG", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ENJBTC", BaseAsset = "ENJ", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "STEEMBTC", BaseAsset = "STEEM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ZILBTC", BaseAsset = "ZIL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BTGBTC", BaseAsset = "BTG", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "MKRBTC", BaseAsset = "MKR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "YFIBTC", BaseAsset = "YFI", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "MITHBTC", BaseAsset = "MITH", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "LOOMBTC", BaseAsset = "LOOM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ARDRBTC", BaseAsset = "ARDR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "IDEXBTC", BaseAsset = "IDEX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SNGLSBTC", BaseAsset = "SNGLS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DGBBTC", BaseAsset = "DGB", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "OAXBTC", BaseAsset = "OAX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FTMBTC", BaseAsset = "FTM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AMBBTC", BaseAsset = "AMB", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DOTBTC", BaseAsset = "DOT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FIROBTC", BaseAsset = "FIRO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CTXCBTC", BaseAsset = "CTXC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BZRXBTC", BaseAsset = "BZRX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SKLBTC", BaseAsset = "SKL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BADGERBTC", BaseAsset = "BADGER", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ELFBTC", BaseAsset = "ELF", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "OGNBTC", BaseAsset = "OGN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DEGOBTC", BaseAsset = "DEGO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "REQBTC", BaseAsset = "REQ", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AVAXBTC", BaseAsset = "AVAX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "XMRBTC", BaseAsset = "XMR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ALGOBTC", BaseAsset = "ALGO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NMRBTC", BaseAsset = "NMR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ICXBTC", BaseAsset = "ICX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "WINGBTC", BaseAsset = "WING", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "OCEANBTC", BaseAsset = "OCEAN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "OMGBTC", BaseAsset = "OMG", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "STMXBTC", BaseAsset = "STMX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AVABTC", BaseAsset = "AVA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CVCBTC", BaseAsset = "CVC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "IOTABTC", BaseAsset = "IOTA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "VITEBTC", BaseAsset = "VITE", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "XTZBTC", BaseAsset = "XTZ", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BTCSTBTC", BaseAsset = "BTCST", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SNXBTC", BaseAsset = "SNX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "PERLBTC", BaseAsset = "PERL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RENBTCBTC", BaseAsset = "RENBTC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "DASHBTC", BaseAsset = "DASH", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "TFUELBTC", BaseAsset = "TFUEL", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CHRBTC", BaseAsset = "CHR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "INJBTC", BaseAsset = "INJ", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SFPBTC", BaseAsset = "SFP", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ATMBTC", BaseAsset = "ATM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "LINABTC", BaseAsset = "LINA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BCDBTC", BaseAsset = "BCD", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "QLCBTC", BaseAsset = "QLC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ADXBTC", BaseAsset = "ADX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RLCBTC", BaseAsset = "RLC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "TWTBTC", BaseAsset = "TWT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "1INCHBTC", BaseAsset = "1INCH", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "XVGBTC", BaseAsset = "XVG", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NEOBTC", BaseAsset = "NEO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "WABIBTC", BaseAsset = "WABI", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SCBTC", BaseAsset = "SC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ASTBTC", BaseAsset = "AST", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "WAVESBTC", BaseAsset = "WAVES", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AKROBTC", BaseAsset = "AKRO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ALICEBTC", BaseAsset = "ALICE", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "RAMPBTC", BaseAsset = "RAMP", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "POLYBTC", BaseAsset = "POLY", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "EVXBTC", BaseAsset = "EVX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FORBTC", BaseAsset = "FOR", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SCRTBTC", BaseAsset = "SCRT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ALPHABTC", BaseAsset = "ALPHA", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "CDTBTC", BaseAsset = "CDT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "OXTBTC", BaseAsset = "OXT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BLZBTC", BaseAsset = "BLZ", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "LTCBTC", BaseAsset = "LTC", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ZRXBTC", BaseAsset = "ZRX", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "WANBTC", BaseAsset = "WAN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ZENBTC", BaseAsset = "ZEN", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "NAVBTC", BaseAsset = "NAV", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "REEFBTC", BaseAsset = "REEF", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "LSKBTC", BaseAsset = "LSK", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "XEMBTC", BaseAsset = "XEM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ACMBTC", BaseAsset = "ACM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BANDBTC", BaseAsset = "BAND", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "COMPBTC", BaseAsset = "COMP", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "FISBTC", BaseAsset = "FIS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ANTBTC", BaseAsset = "ANT", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "COSBTC", BaseAsset = "COS", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "SXPBTC", BaseAsset = "SXP", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "BEAMBTC", BaseAsset = "BEAM", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "AUDIOBTC", BaseAsset = "AUDIO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "ROSEBTC", BaseAsset = "ROSE", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "GTOBTC", BaseAsset = "GTO", QuoteAsset = "BTC" }
,new ExchangeSymbol { Ticker = "LTOBTC", BaseAsset = "LTO", QuoteAsset = "BTC" }
            };
            }
        }

        private void WireMarketDataEvents(IMarketData marketData, ServiceProvider services)
        {
            var symbol = new List<ExchangeSymbol>() { new ExchangeSymbol { Ticker = "ETHBTC", BaseAsset = "ETH", QuoteAsset = "BTC" } };


            List<CandleInterval> intervals = new List<CandleInterval>()
            {
                CandleInterval.Minute_15
            };
            AddEvents(marketData as AbstractMarketData, symbols, intervals, services);
        }

        private void AddEvents(AbstractMarketData marketDate, List<ExchangeSymbol> symbols, List<CandleInterval> intervals, ServiceProvider services)
        {
            foreach (var symbol in symbols)
            {
                foreach (var interval in intervals)
                {
                    var algo = services.GetService<IAlgorithm>();
                    marketDate.InitialDataLoadSubscribe(symbol.Ticker, interval, algo.ProcessHistoricMarketData);
                    marketDate.InitialDataStreamSubscribe(symbol.Ticker, interval, algo.ProcessLiveCandleStick);
                }
            }
        }


        private IEnumerable<(string, ExchangeCandlestick)> LoadFile()
        {
            char[] seperators = { ',' };
            using (StreamReader reader = new StreamReader(Directory.GetCurrentDirectory() + @"/MarketDataTest.csv"))
            {
                string line = reader.ReadLine();//skips the columns
                while ((line = reader.ReadLine()) != null)
                {
                    var read = line.Split(seperators, StringSplitOptions.None);
                    var candleStick = new ExchangeCandlestick
                    {
                        Symbol = read[1],
                        Interval = CandleInterval.Minute_15,
                        OpenTime = Convert.ToDateTime(read[6]),
                        Open = Convert.ToDecimal(read[2]),
                        High = Convert.ToDecimal(read[3]),
                        Low = Convert.ToDecimal(read[4]),
                        Close = Convert.ToDecimal(read[5]),
                        Volume = 0,
                        CloseTime = Convert.ToDateTime(read[7]),
                        QuoteVolume = 0,
                        NumberOfTrades = 0,
                        TakerBuyBaseAssetVolume = 0,
                        TakerBuyQuoteAssetVolume = 0
                    };
                    yield return (read[0], candleStick);
                }
            }
        }
    }
}
