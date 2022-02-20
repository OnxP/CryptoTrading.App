using System.Collections.Generic;
using Binance;

namespace CryptoTrading.App.Core
{
    public interface IAccountConfig
    {
        List<Symbol> LoadCurrencies();
        List<AccountBalance> LoadPositions();
    }
}