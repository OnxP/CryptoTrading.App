using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Exchange.BinanceNet;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.BrokerService
{
    public class BrokerExchangeRegistry
    {
        private readonly ConcurrentDictionary<string, IExchangeProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
        private readonly BinanceNetExchangeProviderFactory _binanceFactory = new();
        private readonly ILogger<BrokerExchangeRegistry> _logger;

        public BrokerExchangeRegistry(ILogger<BrokerExchangeRegistry> logger)
        {
            _logger = logger;
        }

        public void Register(ExchangeConfig config, TradingVenue venue)
        {
            var key = $"{config.ExchangeId}:{venue}";
            var provider = _binanceFactory.Create(config, venue);
            _providers[key] = provider;
            _logger.LogInformation("Registered broker provider: {Key}", key);
        }

        public IExchangeProvider Get(string exchangeId, TradingVenue venue)
        {
            var key = $"{exchangeId}:{venue}";
            if (_providers.TryGetValue(key, out var provider))
                return provider;

            if (_providers.TryGetValue($"{exchangeId}:{TradingVenue.Spot}", out provider))
                return provider;

            throw new KeyNotFoundException($"No broker provider registered for '{key}'");
        }

        public IExchangeProvider Get(string exchangeId, string venueStr)
        {
            var venue = string.IsNullOrEmpty(venueStr) ? TradingVenue.Spot : ParseVenue(venueStr);
            return Get(exchangeId, venue);
        }

        public IReadOnlyCollection<string> GetRegisteredKeys()
        {
            return _providers.Keys.ToList();
        }

        private static TradingVenue ParseVenue(string venue)
        {
            if (Enum.TryParse<TradingVenue>(venue, true, out var result))
                return result;
            return TradingVenue.Spot;
        }
    }
}
