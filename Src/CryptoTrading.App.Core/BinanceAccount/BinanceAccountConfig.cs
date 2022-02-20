using System.Collections.Generic;
using System.Linq;
using Binance;

namespace CryptoTrading.App.Core.BinanceAccount
{
    internal class BinanceAccountConfig :IAccountConfig
    {
        private BinanceApi Api { get;}
        private BinanceApiUser User { get; }
        public BinanceAccountConfig(BinanceApi api,BinanceApiUser user)
        { 
            Api = api;
            User = user;
        }
        public List<Symbol> LoadCurrencies()
        {
            var symbols = Api.GetSymbolsAsync();
            return symbols.Result.ToList();
        }

        public List<AccountBalance> LoadPositions()
        {
            var accountInfo = Api.GetAccountInfoAsync(User);
            return accountInfo.Result.Balances.ToList();
        }
    }
}
