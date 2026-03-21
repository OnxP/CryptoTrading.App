using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Exchange
{
    public class ExchangeAccountConfig : IAccountConfig
    {
        private readonly IExchangeProvider _exchangeProvider;

        public ExchangeAccountConfig(IExchangeProvider exchangeProvider)
        {
            _exchangeProvider = exchangeProvider;
        }

        public async Task<List<ExchangeSymbol>> LoadCurrencies()
        {
            var symbols = await _exchangeProvider.GetSymbolsAsync();
            return symbols.ToList();
        }

        public async Task<List<ExchangeBalance>> LoadPositions()
        {
            var balances = await _exchangeProvider.GetBalancesAsync();
            return balances.ToList();
        }
    }
}
