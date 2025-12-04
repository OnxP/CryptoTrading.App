using Binance;
using CryptoTrading.App.Core;
using Skender.Stock.Indicators;
using System;

namespace CryptoTrading.App.Algorithm
{
    public interface IMarketStructureStrategy
    {
        IMarketStructureResult Calculate();
        void SetQuotes(QuoteHub<IQuote> quoteHubHigh);
    }
}