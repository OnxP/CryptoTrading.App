using System.Collections.Generic;
using System.Linq;
using Binance;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Monitor
{
    public class Position : IPosition
    {
        public string Symbol { get; }
        public List<TransactionLeg> _legs;

        public Position(string symbol, decimal freeAmount)
        {
            _legs = new List<TransactionLeg>();
            _legs.Add(new TransactionLeg() { Symbol = symbol, Quantity = freeAmount});
            Symbol = symbol;
        }

        public decimal FreeAmount => _legs.Where(x => x.Status == TransactionStatus.Completed).Sum(x => x.Quantity);
        public decimal NonFreeAmount => _legs.Where(x => x.Status == TransactionStatus.Pending).Sum(x => x.Quantity);

        public bool CheckFunds(double sellAmount)
        {
            return (decimal)sellAmount <= FreeAmount;
        }

        public bool HasOpenPosition => NonFreeAmount != 0;

        public decimal CalculateStopLoss(Order order)
        { 
            return order.Price * 0.9m;
        }

        public TransactionLeg CreateTransaction(decimal quantity)
        {
            var t = new TransactionLeg
            {
                Symbol = Symbol,
                Quantity = quantity,
                Status = TransactionStatus.Pending
            };
            _legs.Add(t);
            return t;
        }
    }
}
