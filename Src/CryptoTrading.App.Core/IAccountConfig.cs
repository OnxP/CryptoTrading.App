using System.Collections.Generic;
using System.Threading.Tasks;
using Binance;

namespace CryptoTrading.App.Core
{
    public interface IAccountConfig
    {
        Task<List<Symbol>> LoadCurrencies();
        Task<List<AccountBalance>> LoadPositions();
    }
}