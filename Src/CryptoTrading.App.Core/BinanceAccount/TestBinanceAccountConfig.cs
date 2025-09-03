using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public async Task<List<Symbol>> LoadCurrencies()
        {
            /* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var symbols = await Api.GetSymbolsAsync().ConfigureAwait(false);
            var sym = symbols.Where(x => x.QuoteAsset == Asset.USDT && x.Status == SymbolStatus.Trading);
            await Symbol.UpdateCacheAsync(Api).ConfigureAwait(false);
            return sym.ToList();
        }

        public async Task<List<AccountBalance>> LoadPositions()
        {
            /* TODO: avoid blocking on async — consider replacing .Result/.Wait() with await */
            var account = await Api.GetAccountInfoAsync(User).ConfigureAwait(false);
            var accountInfo = account.Balances.ToList();
            var btcAccountInfo = accountInfo.First(x => x.Asset == Asset.USDT);
            var bnbAccountInfo = accountInfo.First(x => x.Asset == Asset.BNB);
            accountInfo.Remove(btcAccountInfo);
            accountInfo.Remove(bnbAccountInfo);
            accountInfo.Add(new AccountBalance(Asset.USDT,10000.0m,0m));
            accountInfo.Add(new AccountBalance(Asset.BNB,(decimal)Config.StartBnbAmount,0m));
            return accountInfo;
        }
    }
}