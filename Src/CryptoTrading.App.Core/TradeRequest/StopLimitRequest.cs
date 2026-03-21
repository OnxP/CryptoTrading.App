using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    public class StopLimitRequest : IStopLimitRequest
    {
        private ITransaction currentTransaction;

        public StopLimitRequest(ITransaction currentTransaction)
        {
            if (currentTransaction == null) throw new Exception();
            this.currentTransaction = currentTransaction;
        }

        public ExchangeOrderSide? OrderType => currentTransaction.Base.Quantity < 0 ? ExchangeOrderSide.Sell : ExchangeOrderSide.Buy;
        public decimal Quantity => Math.Abs(currentTransaction.Base.Quantity);
        public decimal Price => currentTransaction.Price;
        public string Symbol => currentTransaction.Pair;

        public decimal StopPrice => currentTransaction.Price;

    }

    public class LimitRequest : ILimitRequest
    {
        private ITransaction currentTransaction;

        public LimitRequest(ITransaction currentTransaction)
        {
            if (currentTransaction == null) throw new Exception();
            this.currentTransaction = currentTransaction;
        }

        public ExchangeOrderSide? OrderType => currentTransaction.Base.Quantity < 0 ? ExchangeOrderSide.Sell : ExchangeOrderSide.Buy;
        public decimal Quantity => Math.Abs(currentTransaction.Base.Quantity);
        public decimal Price => currentTransaction.Price;
        public string Symbol => currentTransaction.Pair;
    }
}
