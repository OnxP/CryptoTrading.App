using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        public List<Symbol> LoadCurrencies()
        {
/* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var symbols = await Api.GetSymbolsAsync().ConfigureAwait(false).Where(x => x.QuoteAsset == Asset.BTC && x.Status == SymbolStatus.Trading);
            Symbol.UpdateCacheAsync(Api).ConfigureAwait(false);
            return symbols.ToList();
        }

        public List<AccountBalance> LoadPositions()
        {
            var accountInfo = Api.GetAccountInfoAsync(User);
/* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            return accountInfo.Result.Balances.ToList();
        }
    }
}