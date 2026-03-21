using System.Collections.Generic;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.Binance
{
    public class BinanceExchangeProviderFactory : IExchangeProviderFactory
    {
        public IExchangeProvider Create(ExchangeConfig config)
        {
            return new BinanceExchangeProvider(config.ApiKey, config.ApiSecret);
        }

        public IEnumerable<string> GetSupportedExchanges()
        {
            return new[] { "Binance" };
        }
    }
}
