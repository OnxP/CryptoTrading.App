using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Position
{
    public class Position : IPosition
    {
        public static readonly object _lock = new object();
        public string Symbol { get; }
        public List<TransactionLeg> _legs;

        public Position(string symbol, decimal freeAmount)
        {
            _legs = new List<TransactionLeg>();
            if (freeAmount > 0) _legs.Add(new TransactionLeg() { Symbol = symbol, Quantity = freeAmount, Status = TransactionLegStatus.Completed });
            Symbol = symbol;
            IsLocked = false;
        }

        public decimal FreeAmount
        {
            get
            {
                lock (_lock)
                {
                    return _legs.Where(x => x.Status == TransactionLegStatus.Completed || (x.Status == TransactionLegStatus.Pending && x.Quantity<0)).Sum(x => x.Quantity);
                }
            }
        }
        public decimal NonFreeAmount
        {
            get
            {
                lock (_lock)
                {
                    return _legs.Where(x => x.Status == TransactionLegStatus.Pending).Sum(x => x.Quantity);
                }
            }
        }


        public bool CheckHasEnoughBalance(ITradeRequest request)
        {
            var valid = request.Validate(FreeAmount, NonFreeAmount);
            return valid && FreeAmount > 0 && FreeAmount > request.QuoteQuantity && request.BaseQuantity!=0m ;
        }

        public bool HasOpenPosition => _legs.Any(x=>x.Status==TransactionLegStatus.Pending) || IsLocked;

        public bool IsLocked { get; set; }

        public TransactionLeg CreatePendingTransaction(decimal quantity)
        {
            var t = new TransactionLeg
            {
                Symbol = Symbol,
                Quantity = quantity,
                Status = TransactionLegStatus.Pending
            };
            lock (_lock)
            {
                _legs.Add(t);
            }
            return t;
        }
        public TransactionLeg CreateTransaction(decimal quantity)
        {
            var t = new TransactionLeg
            {
                Symbol = Symbol,
                Quantity = quantity,
                Status = TransactionLegStatus.Completed
            };
            lock (_lock)
            {
                _legs.Add(t);
            }
            return t;
        }
    }
}
