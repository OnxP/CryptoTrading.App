using CryptoTrading.App.Core.RequestTracker;
using CryptoTrading.App.Core.Trade;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Broker.Position
{
    public class BrokerPosition : IPosition
    {
        private readonly object _lock = new object();
        private readonly List<TransactionLeg> _legs;

        public string Symbol { get; }

        public BrokerPosition(string symbol, decimal freeAmount)
        {
            _legs = new List<TransactionLeg>();
            if (freeAmount > 0)
                _legs.Add(new TransactionLeg { Symbol = symbol, Quantity = freeAmount, Status = TransactionLegStatus.Completed });
            Symbol = symbol;
            IsLocked = false;
        }

        public decimal FreeAmount
        {
            get
            {
                lock (_lock)
                {
                    return _legs
                        .Where(x => x.Status == TransactionLegStatus.Completed || (x.Status == TransactionLegStatus.Pending && x.Quantity < 0))
                        .Sum(x => x.Quantity);
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

        public bool CheckHasEnoughBalance(decimal amount)
        {
            return FreeAmount > 0 && FreeAmount > amount;
        }

        public bool CheckHasEnoughBalanceIncludingFee(decimal amount)
        {
            return FreeAmount > 0 && FreeAmount > (amount + (amount * 0.02m));
        }

        public bool CheckHasEnoughBalanceFee(ITradeRequest request)
        {
            var closePrice = CandleStickTracker.GetClosePrice(request.BaseSymbol + Symbol);
            if (closePrice.HasValue)
                return FreeAmount > 0 && FreeAmount > (request.Amount * closePrice.Value * 0.02m);
            return false;
        }

        public bool HasOpenPosition => _legs.Any(x => x.Status == TransactionLegStatus.Pending) || IsLocked;

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
