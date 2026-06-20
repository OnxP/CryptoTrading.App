using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Exchange.BinanceNet;
using CryptoTrading.App.Persistence;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketDataService
{
    public class ExchangeProviderRegistry
    {
        private readonly ConcurrentDictionary<string, IExchangeProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
        private readonly BinanceNetExchangeProviderFactory _binanceFactory = new();
        private readonly ILogger<ExchangeProviderRegistry> _logger;

        public ExchangeProviderRegistry(ILogger<ExchangeProviderRegistry> logger)
        {
            _logger = logger;
        }

        public void Register(ExchangeConfig config, TradingVenue venue = TradingVenue.Spot)
        {
            var provider = _binanceFactory.Create(config, venue);
            _providers[config.ExchangeId] = provider;
            _logger.LogInformation("Registered exchange provider: {ExchangeId} ({Venue})", config.ExchangeId, venue);
        }

        public IExchangeProvider Get(string exchangeId)
        {
            if (_providers.TryGetValue(exchangeId, out var provider))
                return provider;

            throw new KeyNotFoundException($"No exchange provider registered for '{exchangeId}'");
        }

        public bool TryGet(string exchangeId, out IExchangeProvider provider)
        {
            return _providers.TryGetValue(exchangeId, out provider);
        }

        public IReadOnlyCollection<string> GetRegisteredExchanges()
        {
            return _providers.Keys.ToList();
        }
    }
}
