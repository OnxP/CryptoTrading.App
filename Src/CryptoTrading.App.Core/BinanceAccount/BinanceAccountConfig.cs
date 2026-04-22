using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core.BinanceAccount
{
    internal class BinanceAccountConfig : IAccountConfig
    {
        // Exchange tag for the neutral balances we emit. Matches the
        // constant used by the real Binance adapter in Exchange.Binance,
        // but we inline it here to avoid a Core -> Exchange.Binance cycle.
        private const string ExchangeName = "Binance";

        private IBinanceApi Api { get; }
        private IBinanceApiUser User { get; }
        public BinanceAccountConfig(IBinanceApi api, IBinanceApiUser user)
        {
            Api = api;
            User = user;
        }

        public async Task<List<string>> LoadCurrencies()
        {
            var symbols = await Api.GetSymbolsAsync().ConfigureAwait(false);
            var sym = symbols.Where(x => x.QuoteAsset == Asset.BTC && x.Status == SymbolStatus.Trading);
            await Symbol.UpdateCacheAsync(Api).ConfigureAwait(false);
            // Expose pair strings so the public contract stays exchange-agnostic;
            // the Binance.Symbol cache is still warmed above for internal use.
            return sym.Select(x => (string)x).ToList();
        }

        public async Task<List<ExchangeBalance>> LoadPositions()
        {
            var accountInfo = await Api.GetAccountInfoAsync(User).ConfigureAwait(false);
            return accountInfo.Balances
                .Select(b => new ExchangeBalance(ExchangeName, b.Asset, b.Free, b.Locked))
                .ToList();
        }
    }
}
