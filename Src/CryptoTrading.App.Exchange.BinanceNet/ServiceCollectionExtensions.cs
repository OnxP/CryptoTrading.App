using System;
using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoTrading.App.Exchange.BinanceNet
{
    /// <summary>
    /// DI glue for the Binance.Net adapter. In this PR only spot is wired;
    /// the (RunType, Venue) switch here is the one place PR 3 (futures)
    /// and PR 4 (margin) plug in. The bundled-SDK path in
    /// <c>CryptoTrading.App.Broker.ServiceCollectionExtensions.AddBroker</c>
    /// is untouched — this registers an <see cref="IExchangeProvider"/>
    /// alongside it so consumers can migrate at their own pace.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Register an <see cref="IExchangeProvider"/> backed by Binance.Net
        /// for the venue selected in <paramref name="config"/>. Should be
        /// called from composition roots (app / backtester / tests) when
        /// the caller wants live or testnet exchange access that goes
        /// through the new SDK.
        /// </summary>
        public static IServiceCollection AddBinanceNetExchangeProvider(
            this IServiceCollection services, IConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            // Register the rest + socket clients as singletons — Binance.Net
            // internally owns HTTP handlers and a single websocket
            // connection manager, so this matches the library's guidance.
            services.AddSingleton<IBinanceRestClient>(_ => new BinanceRestClient(options =>
            {
                if (!string.IsNullOrEmpty(config.ApiKey) && !string.IsNullOrEmpty(config.ApiKeySecret))
                    options.ApiCredentials = new BinanceCredentials(config.ApiKey, config.ApiKeySecret);
            }));

            services.AddSingleton<IBinanceSocketClient>(_ => new BinanceSocketClient(options =>
            {
                if (!string.IsNullOrEmpty(config.ApiKey) && !string.IsNullOrEmpty(config.ApiKeySecret))
                    options.ApiCredentials = new BinanceCredentials(config.ApiKey, config.ApiKeySecret);
            }));

            services.AddTransient<IExchangeProvider>(provider =>
            {
                var rest = provider.GetRequiredService<IBinanceRestClient>();
                var socket = provider.GetRequiredService<IBinanceSocketClient>();
                switch (config.TradingVenue)
                {
                    case TradingVenue.Spot:
                        return new BinanceNetSpotExchangeProvider(rest, socket);
                    case TradingVenue.Margin:
                        throw new NotSupportedException(
                            "Margin venue is implemented in PR 4 (BinanceNetMarginExchangeProvider).");
                    case TradingVenue.Futures:
                        throw new NotSupportedException(
                            "Futures venue is implemented in PR 3 (BinanceNetUsdFuturesExchangeProvider).");
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(config.TradingVenue), config.TradingVenue, "Unknown trading venue.");
                }
            });

            return services;
        }
    }
}
