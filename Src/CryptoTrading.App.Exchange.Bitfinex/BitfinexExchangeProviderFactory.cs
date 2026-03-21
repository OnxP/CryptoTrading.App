using System.Collections.Generic;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.Bitfinex
{
    public class BitfinexExchangeProviderFactory : IExchangeProviderFactory
    {
        public IExchangeProvider Create(ExchangeConfig config)
        {
            return new BitfinexExchangeProvider(config.ApiKey, config.ApiSecret);
        }

        public IEnumerable<string> GetSupportedExchanges()
        {
            return new[] { "Bitfinex" };
        }
    }
}
