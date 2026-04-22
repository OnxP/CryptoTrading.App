using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public class MarketRequest : IMarketRequest
    {
        private ITransaction currentTransaction;

        public MarketRequest(ITransaction currentTransaction)
        {
            this.currentTransaction = currentTransaction;
        }

        public ExchangeOrderSide? OrderType => currentTransaction.Base.Quantity < 0 ? ExchangeOrderSide.Sell : ExchangeOrderSide.Buy;
        public decimal Quantity => Math.Abs(currentTransaction.Base.Quantity);
        public decimal Price => currentTransaction.Price;
        public string Symbol => currentTransaction.Pair;
    }
}
