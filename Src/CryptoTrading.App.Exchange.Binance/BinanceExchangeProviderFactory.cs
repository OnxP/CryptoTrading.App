using System;
using System.Collections.Generic;
using Binance;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.BinanceAdapter
{
    /// <summary>
    /// Factory for creating Binance exchange providers from configuration.
    /// </summary>
    public class BinanceExchangeProviderFactory
    {
        private readonly IBinanceApi _api;

        public BinanceExchangeProviderFactory(IBinanceApi api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public IExchangeProvider Create(ExchangeConfig config)
        {
            if (!string.Equals(config.ExchangeId, BinanceMapper.ExchangeName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Expected Binance config, got {config.ExchangeId}");

            var user = new BinanceApiUser(config.ApiKey, config.ApiSecret);
            return new BinanceExchangeProvider(_api, user);
        }
    }
}
