using System.Collections.Generic;

namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Factory for creating exchange provider instances from configuration.
    /// Allows runtime registration and creation of exchange-specific providers.
    /// </summary>
    public interface IExchangeProviderFactory
    {
        /// <summary>
        /// Create an exchange provider from configuration
        /// </summary>
        IExchangeProvider Create(ExchangeConfig config);

        /// <summary>
        /// Get the list of supported exchange identifiers
        /// </summary>
        IEnumerable<string> GetSupportedExchanges();
    }
}
