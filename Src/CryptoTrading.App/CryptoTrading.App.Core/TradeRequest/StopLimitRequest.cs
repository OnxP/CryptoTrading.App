using Binance;
using CryptoTrading.App.Core.Trade;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.TradeRequest
{
    public class StopLimitRequest : IMarketRequest
    {
        private Transaction currentTransaction;

        public StopLimitRequest(Transaction currentTransaction)
        {
            this.currentTransaction = currentTransaction;
        }

        public OrderSide? OrderType => currentTransaction.Base.Quantity < 0 ? OrderSide.Sell : OrderSide.Buy;
        public decimal Quantity => Math.Abs(currentTransaction.Base.Quantity);
        public decimal Price => currentTransaction.Price;
        public string Symbol => currentTransaction.Pair;

        public decimal StopPrice { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
} 
