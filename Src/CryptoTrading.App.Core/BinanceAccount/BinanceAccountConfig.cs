using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance;

namespace CryptoTrading.App.Core.BinanceAccount
{
    internal class BinanceAccountConfig :IAccountConfig
    {
        private IBinanceApi Api { get;}
        private IBinanceApiUser User { get; }
        public BinanceAccountConfig(IBinanceApi api,IBinanceApiUser user)
        { 
            Api = api;
            User = user;
        }
        public async Task<List<Symbol>> LoadCurrencies()
        {
            /* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var symbols = await Api.GetSymbolsAsync().ConfigureAwait(false);
            var sym = symbols.Where(x => x.QuoteAsset == Asset.BTC && x.Status == SymbolStatus.Trading);
            await Symbol.UpdateCacheAsync(Api).ConfigureAwait(false);
            return sym.ToList();
        }

        public async Task<List<AccountBalance>> LoadPositions()
        {
            var accountInfo = await Api.GetAccountInfoAsync(User);
/* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            return accountInfo.Balances.ToList();
        }
    }
}