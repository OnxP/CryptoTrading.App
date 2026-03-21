using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Exchange
{
    public interface ISymbolCache
    {
        ExchangeSymbol Get(string exchangeId, string ticker);
        ExchangeSymbol Get(string ticker);
        IEnumerable<ExchangeSymbol> GetAll(string exchangeId);
        IEnumerable<ExchangeSymbol> GetAll();
        Task RefreshAsync(IExchangeProvider provider);
        void SetDefaultExchange(string exchangeId);
    }
}
