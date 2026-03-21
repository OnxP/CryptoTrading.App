using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Exchange
{
    public class ExchangeSymbolCache : ISymbolCache
    {
        private readonly ConcurrentDictionary<(string exchangeId, string ticker), ExchangeSymbol> _symbols = new();
        private string _defaultExchangeId;

        public ExchangeSymbol Get(string exchangeId, string ticker)
        {
            if (_symbols.TryGetValue((exchangeId, ticker), out var symbol))
                return symbol;
            throw new KeyNotFoundException($"Symbol '{ticker}' not found for exchange '{exchangeId}'");
        }

        public ExchangeSymbol Get(string ticker)
        {
            if (string.IsNullOrEmpty(_defaultExchangeId))
                throw new InvalidOperationException("Default exchange not set. Call SetDefaultExchange first.");
            return Get(_defaultExchangeId, ticker);
        }

        public IEnumerable<ExchangeSymbol> GetAll(string exchangeId)
        {
            return _symbols.Where(kv => kv.Key.exchangeId == exchangeId).Select(kv => kv.Value);
        }

        public IEnumerable<ExchangeSymbol> GetAll()
        {
            if (string.IsNullOrEmpty(_defaultExchangeId))
                return _symbols.Values;
            return GetAll(_defaultExchangeId);
        }

        public async Task RefreshAsync(IExchangeProvider provider)
        {
            var symbols = await provider.GetSymbolsAsync();
            foreach (var symbol in symbols)
            {
                _symbols[(provider.ExchangeId, symbol.Ticker)] = symbol;
            }
        }

        public void SetDefaultExchange(string exchangeId)
        {
            _defaultExchangeId = exchangeId;
        }
    }
}
