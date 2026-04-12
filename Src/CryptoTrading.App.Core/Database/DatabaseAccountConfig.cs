using Binance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Database
{
    internal class DatabaseAccountConfig :IAccountConfig
    {
        public DatabaseAccountConfig(IConfig config)
        {
            Config = config;
        }

        public IConfig Config { get; }

        //public 
        public async Task<List<Symbol>> LoadCurrencies()
        {
            using var context = new CryptoDbContext();
            var res = context.CandleSticks.SqlQuery(Symbols, Config.From, Config.To, Config.Interval).Select(x=>x.Symbol).Distinct().ToList();
            var list = new List<Symbol>();
            foreach (var symbol in res.Where(x=>x=="BTCUSDT"))
            {
                var symbolObject = Symbol.Cache.Get(symbol);
                if (symbolObject == null)
                {
                    // Determine base/quote assets from the symbol string
                    // USDT-quoted pairs (e.g. "BTCUSDT") need 4-char quote strip
                    string assetString;
                    Asset quoteAsset;
                    if (symbol.EndsWith("USDT"))
                    {
                        assetString = symbol.Substring(0, symbol.Length - 4);
                        quoteAsset = Asset.USDT;
                    }
                    else
                    {
                        assetString = symbol.Substring(0, symbol.Length - 3);
                        quoteAsset = Asset.BTC;
                    }

                    var asset = Asset.Cache.Get(assetString);

                    if (asset == null)
                    {
                        Asset.Cache.Set(assetString, new Asset(assetString, 8));
                        asset = Asset.Cache.Get(assetString);
                    }

                    symbolObject = new Symbol(SymbolStatus.Trading,
                        asset, quoteAsset,
                        (1.00000000m, 90000000.00000000m, 1.00000000m), (0m, 0m, 0.00000001m), 0.00010000m, true,
                        new List<OrderType>
                        {
                            OrderType.Limit, OrderType.LimitMaker, OrderType.Market, OrderType.StopLossLimit,
                            OrderType.TakeProfitLimit
                        });
                    Symbol.Cache.Set(symbol,symbolObject);
                }

                list.Add(symbolObject);
            }

            return list;
        }

        private string Symbols => @"select * from CandleStickDbs where (opentime=@p0 OR opentime=@p1) and Interval=@p2";

        public async Task<List<AccountBalance>> LoadPositions()
        {
            using var context = new CryptoDbContext();
            var res = context.CandleSticks.SqlQuery(Symbols, Config.From, Config.To, Config.Interval).Select(x => x.Symbol).Distinct().ToList();

            var list = new List<AccountBalance>();
            list.Add(new AccountBalance(Asset.USDT, Convert.ToDecimal(Config.StartBtcAmount*200000), 0m));
            //list.Add(new AccountBalance(Asset.BTC, Convert.ToDecimal(Config.StartBtcAmount), 0m));

            foreach (var symbol in res)
            {
                var asset = Symbol.Cache?.Get(symbol)?.BaseAsset;
                if (asset == null) continue;
                decimal free = 0m;
                if (asset.Symbol == "BNB") free = Convert.ToDecimal(Config.StartBnbAmount);

                list.Add(new AccountBalance(asset, free, 0m));
            }
            return list;
        }
    }
}
