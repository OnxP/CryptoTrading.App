using System.Linq;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Exchange.BinanceNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketData
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMarketData(this IServiceCollection services, bool liveStream = false)
        {
            if (liveStream)
            {
                services.AddTransient<IMarketData, LiveMarketData>();
            }
            else
            {
                services.AddTransient<IMarketData, HistoricalMarketData>();
            }
            return services;
        }

        public static IServiceCollection AddHistoricMarketData(this IServiceCollection services)
        {
            services.AddTransient<IMarketData, HistoricalMarketData>();
            return services;
        }

        public static IServiceCollection AddMarketData(this IServiceCollection services, IConfig config)
        {
            // PR 5b: the live implementations (HistoricalMarketData,
            // LiveMarketData, LiveMarketMonitor) now depend on the neutral
            // IExchangeProvider. If the composition root hasn't already
            // registered one (Broker does this for Live/LiveTesting but not
            // BackTesting), wire up the Binance.Net provider here. Idempotent
            // so double-registration is safe.
            EnsureExchangeProvider(services, config);

            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddTransient<IMarketData, DbMarketData>(p => new DbMarketData(
                        p.GetService<ILogger<DbMarketData>>(),
                        p.GetService<ICandleStickManagement>(),
                        p.GetService<IDbData>(),
                        config.From,
                        config.To));
                    services.AddSingleton<ICandleStickManagement, DbCandleStickManagement>();
                    break;
                case RunTypeEnum.LiveTesting:
                case RunTypeEnum.Live:
                    services.AddTransient<IMarketData, LiveMarketData>();
                    break;
            }

            return services;
        }

        public static IServiceCollection AddMarketMonitor(this IServiceCollection services, IConfig config)
        {
            EnsureExchangeProvider(services, config);

            switch (config.RunType)
            {
                case RunTypeEnum.BackTesting:
                    services.AddSingleton<IMarketMonitor, DbMarketMonitor>();
                    break;
                case RunTypeEnum.LiveTesting:
                    services.AddTransient<IMarketMonitor, TestLiveMarketMonitor>();
                    break;
                case RunTypeEnum.Live:
                    services.AddTransient<IMarketMonitor, LiveMarketMonitor>();
                    break;
            }

            return services;
        }

        /// <summary>
        /// Idempotently register an <see cref="IExchangeProvider"/>. Other
        /// composition helpers (AddBroker) do the same check — if any of
        /// them has already wired a provider we leave it alone so runtime
        /// code sees a single provider instance. DbMarketData in
        /// BackTesting mode does not need a network provider, but the
        /// DI graph still needs to resolve constructors of anything that
        /// does (e.g. if consumers swap algorithms), so we always register.
        /// </summary>
        private static void EnsureExchangeProvider(IServiceCollection services, IConfig config)
        {
            if (services.Any(s => s.ServiceType == typeof(IExchangeProvider)))
                return;
            services.AddBinanceNetExchangeProvider(config);
        }
    }
}
