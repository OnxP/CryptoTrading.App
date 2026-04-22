using Binance;
using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Database
{
    internal class DatabaseAccountConfig : IAccountConfig
    {
        // Backtest account tag. We don't care which live exchange the data
        // came from — everything is synthetic for replay.
        private const string ExchangeName = "Backtest";

        public DatabaseAccountConfig(IConfig config)
        {
            Config = config;
        }

        public IConfig Config { get; }

        public async Task<List<string>> LoadCurrencies()
        {
            using var context = new CryptoDbContext();
            var res = context.CandleSticks.SqlQuery(Symbols, Config.From, Config.To, (int)Config.Interval)
                .Select(x => x.Symbol).Distinct().ToList();

            // Warm the Binance.Symbol cache for any downstream code that still
            // looks up exchange-info by pair string. The public return value
            // is the neutral list of pair strings.
            foreach (var symbol in res.Where(x => x == "BTCUSDT"))
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
                    Symbol.Cache.Set(symbol, symbolObject);
                }
            }

            return res.Where(x => x == "BTCUSDT").ToList();
        }

        private string Symbols => @"select * from CandleStickDbs where (opentime=@p0 OR opentime=@p1) and Interval=@p2";

        public async Task<List<ExchangeBalance>> LoadPositions()
        {
            using var context = new CryptoDbContext();
            var res = context.CandleSticks.SqlQuery(Symbols, Config.From, Config.To, (int)Config.Interval)
                .Select(x => x.Symbol).Distinct().ToList();

            var list = new List<ExchangeBalance>
            {
                new ExchangeBalance(ExchangeName, Asset.USDT, Convert.ToDecimal(Config.StartBtcAmount * 200000), 0m)
            };

            foreach (var symbol in res)
            {
                var asset = Symbol.Cache?.Get(symbol)?.BaseAsset;
                if (asset == null) continue;
                decimal free = 0m;
                if (asset.Symbol == "BNB") free = Convert.ToDecimal(Config.StartBnbAmount);

                list.Add(new ExchangeBalance(ExchangeName, asset, free, 0m));
            }
            return list;
        }
    }
}
