using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core.BinanceAccount
{
    internal class TestBinanceAccountConfig : IAccountConfig
    {
        // See note in BinanceAccountConfig: we tag the neutral balances
        // with "Binance" and avoid a cycle back to Exchange.Binance.
        private const string ExchangeName = "Binance";

        private IBinanceApi Api { get; }
        private IBinanceApiUser User { get; }
        private IConfig Config { get; set; }
        public TestBinanceAccountConfig(IConfig config, IBinanceApi api, IBinanceApiUser user)
        {
            Config = config;
            Api = api;
            User = user;
        }

        public async Task<List<string>> LoadCurrencies()
        {
            var symbols = await Api.GetSymbolsAsync().ConfigureAwait(false);
            var sym = symbols.Where(x => x.QuoteAsset == Asset.USDT && x.Status == SymbolStatus.Trading);
            await Symbol.UpdateCacheAsync(Api).ConfigureAwait(false);
            return sym.Select(x => (string)x).ToList();
        }

        public async Task<List<ExchangeBalance>> LoadPositions()
        {
            var account = await Api.GetAccountInfoAsync(User).ConfigureAwait(false);
            var balances = account.Balances
                .Where(b => b.Asset != Asset.USDT && b.Asset != Asset.BNB)
                .Select(b => new ExchangeBalance(ExchangeName, b.Asset, b.Free, b.Locked))
                .ToList();

            // Sandbox: seed USDT/BNB with the configured test starting amounts so
            // paper runs don't deplete real-account reserves.
            balances.Add(new ExchangeBalance(ExchangeName, Asset.USDT, 10000.0m, 0m));
            balances.Add(new ExchangeBalance(ExchangeName, Asset.BNB, (decimal)Config.StartBnbAmount, 0m));
            return balances;
        }
    }
}
