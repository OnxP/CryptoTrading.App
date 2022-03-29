using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Binance;

namespace CryptoTrading.App.Core.BinanceAccount
{
    internal class TestBinanceAccountConfig :IAccountConfig
    {
        private IBinanceApi Api { get;}
        private IBinanceApiUser User { get; }
        private IConfig Config { get; set; }
        public TestBinanceAccountConfig(IConfig config, IBinanceApi api,IBinanceApiUser user)
        {
            Config = config;
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
            var accountInfo = Api.GetAccountInfoAsync(User).Result.Balances.ToList();
            var btcAccountInfo = accountInfo.First(x => x.Asset == Asset.BTC);
            var bnbAccountInfo = accountInfo.First(x => x.Asset == Asset.BNB);
            accountInfo.Remove(btcAccountInfo);
            accountInfo.Remove(bnbAccountInfo);
            accountInfo.Add(new AccountBalance(Asset.BTC,(decimal)Config.StartBtcAmount,0m));
            accountInfo.Add(new AccountBalance(Asset.BNB,(decimal)Config.StartBnbAmount,0m));
            return accountInfo;
        }
    }
}
