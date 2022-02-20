using Binance;
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

        public OrderSide? OrderType => currentTransaction.Base.Quantity < 0 ? OrderSide.Sell : OrderSide.Buy;
        public decimal Quantity => Math.Abs(currentTransaction.Base.Quantity);
        public decimal Price => currentTransaction.Price;
        public string Symbol => currentTransaction.Pair;

        public decimal StopPrice {get;set;}

        //public decimal StopPrice { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
} 
