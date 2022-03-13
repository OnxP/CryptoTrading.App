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
            var symbols = Api.GetSymbolsAsync().Result.Where(x => x.QuoteAsset == Asset.BTC);
            Symbol.UpdateCacheAsync(Api).ConfigureAwait(false);
            return symbols.ToList();
        }

        public List<AccountBalance> LoadPositions()
        {
            var accountInfo = Api.GetAccountInfoAsync(User);
            return accountInfo.Result.Balances.ToList();
        }
    }
}
