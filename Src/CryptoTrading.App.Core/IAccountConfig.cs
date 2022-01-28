using System.Collections.Generic;
using Binance;
using CryptoTrading.App.Core.Position;

namespace CryptoTrading.App.Process
{
    public interface IAccountConfig
    {
        List<Symbol> LoadCurrencies();
        List<AccountBalance> LoadPositions();
    }
}