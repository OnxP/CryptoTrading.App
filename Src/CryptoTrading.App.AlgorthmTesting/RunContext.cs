using Binance;
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
            foreach (Symbol item in Symbol.BtcPairs)
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
            return trades.ToStringTable(x => x.Symbol,
                x => x.StartPrice, //.FirstTransaction.Price.ToString("0.#########"), 
                x => x.Price,//.CurrentTransaction.Price.ToString("0.#########"), 
                x => x.Quantity, //CurrentTransaction.Base.Quantity.ToString("0.####"), 
                x => x.StartDate, //FirstTransaction.TransactionDate.ToString(), 
                x => x.CloseDate, //CurrentTransaction.TransactionDate.ToString(), 
                x => x.Profit);
        }

        List<Symbol> symbols
        {
            get
            {
                return new List<Symbol>()
            {
Symbol.SOL_BTC
,Symbol.FET_BTC
,Symbol.MDA_BTC
,Symbol.VIDT_BTC
,Symbol.SUSHI_BTC
,Symbol.RUNE_BTC
,Symbol.TROY_BTC
,Symbol.ARPA_BTC
,Symbol.AAVE_BTC
,Symbol.FXS_BTC
,Symbol.LIT_BTC
,Symbol.NANO_BTC
,Symbol.TRX_BTC
,Symbol.AXS_BTC
,Symbol.ANKR_BTC
,Symbol.PERP_BTC
,Symbol.MATIC_BTC
,Symbol.VIB_BTC
,Symbol.WNXM_BTC
,Symbol.NKN_BTC
,Symbol.BNB_BTC
,Symbol.BNT_BTC
,Symbol.GRT_BTC
,Symbol.TCT_BTC
,Symbol.STRAX_BTC
,Symbol.QKC_BTC
,Symbol.DUSK_BTC
,Symbol.NXS_BTC
,Symbol.CELO_BTC
,Symbol.GXS_BTC
,Symbol.SRM_BTC
,Symbol.PHA_BTC
,Symbol.AUCTION_BTC
,Symbol.HIVE_BTC
,Symbol.POWR_BTC
,Symbol.DNT_BTC
,Symbol.RCN_BTC
,Symbol.PHB_BTC
,Symbol.SUSD_BTC
,Symbol.RVN_BTC
,Symbol.ORN_BTC
,Symbol.ATOM_BTC
,Symbol.DIA_BTC
,Symbol.JUV_BTC
,Symbol.CTSI_BTC
,Symbol.NAS_BTC
,Symbol.BRD_BTC
,Symbol.CND_BTC
,Symbol.STORJ_BTC
,Symbol.ARK_BTC
,Symbol.CHZ_BTC
,Symbol.UMA_BTC
,Symbol.KMD_BTC
,Symbol.FIL_BTC
,Symbol.BCH_BTC
,Symbol.VET_BTC
,Symbol.SYS_BTC
,Symbol.YFII_BTC
,Symbol.RDN_BTC
,Symbol.SUPER_BTC
,Symbol.KAVA_BTC
,Symbol.RSR_BTC
,Symbol.DCR_BTC
,Symbol.XVS_BTC
,Symbol.BEL_BTC
,Symbol.FUN_BTC
,Symbol.IOST_BTC
,Symbol.WRX_BTC
,Symbol.SAND_BTC
,Symbol.JST_BTC
,Symbol.OM_BTC
,Symbol.SUN_BTC
,Symbol.FIO_BTC
,Symbol.PNT_BTC
,Symbol.GLM_BTC
,Symbol.TRB_BTC
,Symbol.POND_BTC
,Symbol.WTC_BTC
,Symbol.ONT_BTC
,Symbol.DOGE_BTC
,Symbol.WPR_BTC
,Symbol.FLM_BTC
,Symbol.WBTC_BTC
,Symbol.MTL_BTC
,Symbol.TOMO_BTC
,Symbol.CFX_BTC
,Symbol.QTUM_BTC
,Symbol.POA_BTC
,Symbol.DATA_BTC
,Symbol.THETA_BTC
,Symbol.UTK_BTC
,Symbol.GO_BTC
,Symbol.VIA_BTC
,Symbol.HARD_BTC
,Symbol.CKB_BTC
,Symbol.GRS_BTC
,Symbol.KSM_BTC
,Symbol.LRC_BTC
,Symbol.STX_BTC
,Symbol.IOTX_BTC
,Symbol.CRV_BTC
,Symbol.CTK_BTC
,Symbol.OG_BTC
,Symbol.UNFI_BTC
,Symbol.GAS_BTC
,Symbol.QSP_BTC
,Symbol.RIF_BTC
,Symbol.MDT_BTC
,Symbol.FRONT_BTC
,Symbol.ONG_BTC
,Symbol.ADA_BTC
,Symbol.CAKE_BTC
,Symbol.PSG_BTC
,Symbol.PIVX_BTC
,Symbol.BQX_BTC
,Symbol.SNT_BTC
,Symbol.XLM_BTC
,Symbol.FTT_BTC
,Symbol.SKY_BTC
,Symbol.EGLD_BTC
,Symbol.STPT_BTC
,Symbol.AERGO_BTC
,Symbol.BAT_BTC
,Symbol.ZEC_BTC
,Symbol.UNI_BTC
,Symbol.BTS_BTC
,Symbol.APPC_BTC
,Symbol.REP_BTC
,Symbol.CELR_BTC
,Symbol.REN_BTC
,Symbol.ONE_BTC
,Symbol.GVT_BTC
,Symbol.OST_BTC
,Symbol.IRIS_BTC
,Symbol.KNC_BTC
,Symbol.MANA_BTC
,Symbol.NULS_BTC
,Symbol.NEBL_BTC
,Symbol.TRU_BTC
,Symbol.BAL_BTC
,Symbol.NEAR_BTC
,Symbol.COTI_BTC
,Symbol.DLT_BTC
,Symbol.XRP_BTC
,Symbol.ASR_BTC
,Symbol.YOYO_BTC
,Symbol.ETH_BTC
,Symbol.EOS_BTC
,Symbol.HBAR_BTC
,Symbol.AION_BTC
,Symbol.DOCK_BTC
,Symbol.LUNA_BTC
,Symbol.NBS_BTC
,Symbol.LINK_BTC
,Symbol.MTH_BTC
,Symbol.PPT_BTC
,Symbol.DODO_BTC
,Symbol.ETC_BTC
,Symbol.TVK_BTC
,Symbol.HNT_BTC
,Symbol.PAXG_BTC
,Symbol.ENJ_BTC
,Symbol.STEEM_BTC
,Symbol.ZIL_BTC
,Symbol.BTG_BTC
,Symbol.MKR_BTC
,Symbol.YFI_BTC
,Symbol.MITH_BTC
,Symbol.LOOM_BTC
,Symbol.ARDR_BTC
,Symbol.IDEX_BTC
,Symbol.SNGLS_BTC
,Symbol.DGB_BTC
,Symbol.OAX_BTC
,Symbol.FTM_BTC
,Symbol.AMB_BTC
,Symbol.DOT_BTC
,Symbol.FIRO_BTC
,Symbol.CTXC_BTC
,Symbol.BZRX_BTC
,Symbol.SKL_BTC
,Symbol.BADGER_BTC
,Symbol.ELF_BTC
,Symbol.OGN_BTC
,Symbol.DEGO_BTC
,Symbol.REQ_BTC
,Symbol.AVAX_BTC
,Symbol.XMR_BTC
,Symbol.ALGO_BTC
,Symbol.NMR_BTC
,Symbol.ICX_BTC
,Symbol.WING_BTC
,Symbol.OCEAN_BTC
,Symbol.OMG_BTC
,Symbol.STMX_BTC
,Symbol.AVA_BTC
,Symbol.CVC_BTC
,Symbol.IOTA_BTC
,Symbol.VITE_BTC
,Symbol.XTZ_BTC
,Symbol.BTCST_BTC
,Symbol.SNX_BTC
,Symbol.PERL_BTC
,Symbol.RENBTC_BTC
,Symbol.DASH_BTC
,Symbol.TFUEL_BTC
,Symbol.CHR_BTC
,Symbol.INJ_BTC
,Symbol.SFP_BTC
,Symbol.ATM_BTC
,Symbol.LINA_BTC
,Symbol.BCD_BTC
,Symbol.QLC_BTC
,Symbol.ADX_BTC
,Symbol.RLC_BTC
,Symbol.TWT_BTC
,Symbol.INCH_BTC
,Symbol.XVG_BTC
,Symbol.NEO_BTC
,Symbol.WABI_BTC
,Symbol.SC_BTC
,Symbol.AST_BTC
,Symbol.WAVES_BTC
,Symbol.AKRO_BTC
,Symbol.ALICE_BTC
,Symbol.RAMP_BTC
,Symbol.POLY_BTC
,Symbol.EVX_BTC
,Symbol.FOR_BTC
,Symbol.SCRT_BTC
,Symbol.ALPHA_BTC
,Symbol.CDT_BTC
,Symbol.OXT_BTC
,Symbol.BLZ_BTC
,Symbol.LTC_BTC
,Symbol.ZRX_BTC
,Symbol.WAN_BTC
,Symbol.ZEN_BTC
,Symbol.NAV_BTC
,Symbol.REEF_BTC
,Symbol.LSK_BTC
,Symbol.XEM_BTC
,Symbol.ACM_BTC
,Symbol.BAND_BTC
,Symbol.COMP_BTC
,Symbol.FIS_BTC
,Symbol.ANT_BTC
,Symbol.COS_BTC
,Symbol.SXP_BTC
,Symbol.BEAM_BTC
,Symbol.AUDIO_BTC
,Symbol.ROSE_BTC
,Symbol.GTO_BTC
,Symbol.LTO_BTC
            };
            }
        }

        private void WireMarketDataEvents(IMarketData marketData, ServiceProvider services)
        {
            var symbol = new List<Symbol>() { Symbol.ETH_BTC };
            

            List<CandlestickInterval> intervals = new List<CandlestickInterval>()
            {
                CandlestickInterval.Minutes_15
            };
            AddEvents(marketData as AbstractMarketData, symbols, intervals, services);
        }

        private void AddEvents(AbstractMarketData marketDate, List<Symbol> symbols, List<CandlestickInterval> intervals, ServiceProvider services)
        {
            foreach (var symbol in symbols)
            {
                foreach (var interval in intervals)
                {
                    var algo = services.GetService<IAlgorithm>();
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
