using System.Collections.Generic;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    public interface IAccountConfig
    {
        Task<List<ExchangeSymbol>> LoadCurrencies();
        Task<List<ExchangeBalance>> LoadPositions();
    }
}
