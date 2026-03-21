using System.Collections.Generic;

namespace CryptoTrading.App.Core.Exchange
{
    public interface IExchangeProviderFactory
    {
        IExchangeProvider Create(ExchangeConfig config);
        IEnumerable<string> GetSupportedExchanges();
    }
}
