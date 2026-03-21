using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Database
{
    internal class DatabaseAccountConfig : IAccountConfig
    {
        public DatabaseAccountConfig(IConfig config)
        {
            Config = config;
        }

        public IConfig Config { get; }

        //public
        public async Task<List<ExchangeSymbol>> LoadCurrencies()
        {
            using var context = new CryptoDbContext();
            var res = context.CandleSticks.SqlQuery(Symbols, Config.From, Config.To, Config.Interval).Select(x=>x.Symbol).Distinct().ToList();
            var list = new List<ExchangeSymbol>();
            foreach (var symbol in res)
            {
                var baseAssetString = symbol.Substring(0, symbol.Length - 3);
                var quoteAssetString = symbol.Substring(symbol.Length - 3);

                var symbolObject = new ExchangeSymbol
                {
                    Ticker = symbol,
                    BaseAsset = baseAssetString,
                    QuoteAsset = quoteAssetString,
                    MinQuantity = 1.00000000m,
                    MaxQuantity = 90000000.00000000m,
                    StepSize = 1.00000000m,
                    TickSize = 0.00000001m,
                    MinNotional = 0.00010000m,
                    IsActive = true
                };

                list.Add(symbolObject);
            }

            return list;
        }

        private string Symbols => @"select * from CandleStickDbs where (opentime=@p0 OR opentime=@p1) and Interval=@p2";

        public async Task<List<ExchangeBalance>> LoadPositions()
        {
            using var context = new CryptoDbContext();
            var res = context.CandleSticks.SqlQuery(Symbols, Config.From, Config.To, Config.Interval).Select(x => x.Symbol).Distinct().ToList();

            var list = new List<ExchangeBalance>();
            list.Add(new ExchangeBalance { Asset = "USDT", Free = Convert.ToDecimal(Config.StartBtcAmount * 100000), Locked = 0m });
            list.Add(new ExchangeBalance { Asset = "BTC", Free = Convert.ToDecimal(Config.StartBtcAmount), Locked = 0m });

            foreach (var symbol in res)
            {
                var baseAssetString = symbol.Substring(0, symbol.Length - 3);
                decimal free = 0m;
                if (baseAssetString == "BNB") free = Convert.ToDecimal(Config.StartBnbAmount);

                list.Add(new ExchangeBalance { Asset = baseAssetString, Free = free, Locked = 0m });
            }
            return list;
        }
    }
}
