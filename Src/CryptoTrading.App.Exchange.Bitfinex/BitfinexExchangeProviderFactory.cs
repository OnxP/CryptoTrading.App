using System;
using System.Net.Http;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.BitfinexAdapter
{
    /// <summary>
    /// Factory for creating Bitfinex exchange providers from configuration.
    /// </summary>
    public class BitfinexExchangeProviderFactory
    {
        private readonly HttpClient _httpClient;

        public BitfinexExchangeProviderFactory(HttpClient httpClient = null)
        {
            _httpClient = httpClient;
        }

        public IExchangeProvider Create(ExchangeConfig config)
        {
            if (!string.Equals(config.ExchangeId, BitfinexMapper.ExchangeName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Expected Bitfinex config, got {config.ExchangeId}");

            return new BitfinexExchangeProvider(config.ApiKey, config.ApiSecret, _httpClient);
        }
    }
}
