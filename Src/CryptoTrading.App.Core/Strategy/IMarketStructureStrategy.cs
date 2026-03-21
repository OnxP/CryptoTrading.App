using CryptoTrading.App.Core;
using Skender.Stock.Indicators;
using System;

namespace CryptoTrading.App.Core.Strategy
{
    public interface IMarketStructureStrategy
    {
        IMarketStructureResult Calculate();
        void SetQuotes(QuoteHub<IQuote> quoteHubHigh);
    }
}