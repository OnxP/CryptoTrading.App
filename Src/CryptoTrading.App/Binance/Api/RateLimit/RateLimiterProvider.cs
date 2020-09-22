using Microsoft.Extensions.DependencyInjection;
using System;

// ReSharper disable once CheckNamespace
namespace Binance.Api
{
    public sealed class RateLimiterProvider : IRateLimiterProvider
    {
        private IServiceProvider _services;

        public RateLimiterProvider(IServiceProvider services = null)
        {
            _services = services;
        }

        public IRateLimiter CreateRateLimiter()
            => _services?.GetService<IRateLimiter>() ?? new RateLimiter();
    }
}
