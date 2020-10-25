using System;
using System.Collections.Generic;
using System.Text;
using Binance;

namespace CryptoTrading.App.Broker
{
    public class Position : IPosition
    {
        public string Symbol { get; }
        public List<Order> _orders;

        public Position(string symbol, decimal freeAmount)
        {
            _orders = new List<Order>();
            Symbol = symbol;
            FreeAmount = freeAmount;
            if (freeAmount == 0) HasOpenPosition = false;
        }

        public decimal FreeAmount {get;set;}
        public bool CheckFunds(double sellAmount)
        {
            return (decimal)sellAmount <= FreeAmount;
        }

        public bool HasOpenPosition { get; set; }
        public void UpdateOrder(Order order)
        {
            _orders.Add(order);
            HasOpenPosition = true;
        }

        public decimal CalculateStopLoss(Order order)
        {
            return order.Price * 0.9m;
        }
    }
}
