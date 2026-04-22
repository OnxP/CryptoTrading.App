using System;
using System.Collections.Generic;
using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Exchange.BinanceNet
{
    /// <summary>
    /// Builds a Binance.Net-backed IExchangeProvider for a given venue.
    /// All three venues (spot, margin, USD-M futures) are now wired; this
    /// factory is the single construction seam for composition roots that
    /// don't use the DI extension in <see cref="ServiceCollectionExtensions"/>.
    /// </summary>
    public class BinanceNetExchangeProviderFactory : IExchangeProviderFactory
    {
        /// <summary>
        /// Create a provider for the venue selected by <paramref name="venue"/>.
        /// Credentials are taken from <paramref name="config"/>.
        /// </summary>
        public IExchangeProvider Create(ExchangeConfig config, TradingVenue venue)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!string.Equals(config.ExchangeId, BinanceNetMapper.ExchangeName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Expected Binance config, got {config.ExchangeId}");

            var rest = new BinanceRestClient(options =>
            {
                if (!string.IsNullOrEmpty(config.ApiKey) && !string.IsNullOrEmpty(config.ApiSecret))
                    options.ApiCredentials = new BinanceCredentials(config.ApiKey, config.ApiSecret);
            });

            var socket = new BinanceSocketClient(options =>
            {
                if (!string.IsNullOrEmpty(config.ApiKey) && !string.IsNullOrEmpty(config.ApiSecret))
                    options.ApiCredentials = new BinanceCredentials(config.ApiKey, config.ApiSecret);
            });

            switch (venue)
            {
                case TradingVenue.Spot:
                    return new BinanceNetSpotExchangeProvider(rest, socket);
                case TradingVenue.Futures:
                    return new BinanceNetUsdFuturesExchangeProvider(rest, socket);
                case TradingVenue.Margin:
                    return new BinanceNetMarginExchangeProvider(rest, socket);
                default:
                    throw new ArgumentOutOfRangeException(nameof(venue), venue, "Unknown trading venue.");
            }
        }

        /// <summary>
        /// Default-venue Create for the <see cref="IExchangeProviderFactory"/>
        /// contract. Uses spot — callers that care about venue selection should
        /// call the venue-aware overload instead.
        /// </summary>
        public IExchangeProvider Create(ExchangeConfig config) => Create(config, TradingVenue.Spot);

        public IEnumerable<string> GetSupportedExchanges()
        {
            yield return BinanceNetMapper.ExchangeName;
        }
    }
}
