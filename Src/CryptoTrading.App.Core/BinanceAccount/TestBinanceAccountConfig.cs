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
/* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var symbols = await Api.GetSymbolsAsync().ConfigureAwait(false).Where(x => x.QuoteAsset == Asset.BTC && x.Status == SymbolStatus.Trading);
            Symbol.UpdateCacheAsync(Api).ConfigureAwait(false);
            return symbols.ToList();
        }

        public List<AccountBalance> LoadPositions()
        {
/* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var accountInfo = await Api.GetAccountInfoAsync(User).ConfigureAwait(false).Balances.ToList();
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