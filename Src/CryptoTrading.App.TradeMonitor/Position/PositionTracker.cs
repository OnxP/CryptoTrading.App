using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using System;
using System.Collections.Generic;

namespace CryptoTrading.App.Monitor.Position
{
    public class PositionTracker
    {
        private readonly List<OrderRecord> _entryOrders = new();
        private readonly List<OrderRecord> _exitOrders = new();
        private decimal _totalEntryCost;
        private decimal _totalExitProceeds;
        private decimal _exitedQty;

        public string Symbol { get; set; }
        public TradeDirection Direction { get; set; }
        public decimal TargetQty { get; set; }
        public decimal FilledQty { get; private set; }
        public decimal AvgEntryPrice { get; private set; }
        public PositionState State { get; set; } = PositionState.NoPosition;
        public DateTime? EntryTime { get; private set; }
        public DateTime? ExitTime { get; private set; }

        public bool IsOpen => FilledQty > 0 && RemainingQty > 0;
        public decimal RemainingQty => FilledQty - _exitedQty;

        public decimal ProfitPct(decimal currentPrice)
        {
            if (AvgEntryPrice <= 0) return 0;
            if (Direction == TradeDirection.Short)
                return ((AvgEntryPrice - currentPrice) / AvgEntryPrice) * 100;
            return ((currentPrice - AvgEntryPrice) / AvgEntryPrice) * 100;
        }

        public decimal UnrealizedPnl(decimal currentPrice)
        {
            if (Direction == TradeDirection.Short)
                return (AvgEntryPrice - currentPrice) * RemainingQty;
            return (currentPrice - AvgEntryPrice) * RemainingQty;
        }

        public decimal RealizedPnl
        {
            get
            {
                if (_exitedQty <= 0) return 0;
                var avgExitPrice = _totalExitProceeds / _exitedQty;
                if (Direction == TradeDirection.Short)
                    return (AvgEntryPrice - avgExitPrice) * _exitedQty;
                return (avgExitPrice - AvgEntryPrice) * _exitedQty;
            }
        }

        public decimal TotalPnl(decimal currentPrice) => RealizedPnl + UnrealizedPnl(currentPrice);

        public void RecordFill(ExchangeOrder order)
        {
            _entryOrders.Add(new OrderRecord(order, DateTime.UtcNow));
            FilledQty += order.FilledQuantity;
            _totalEntryCost += order.Price * order.FilledQuantity;
            AvgEntryPrice = FilledQty > 0 ? _totalEntryCost / FilledQty : 0;
            EntryTime ??= order.Timestamp;
        }

        public void RecordExit(ExchangeOrder order)
        {
            _exitOrders.Add(new OrderRecord(order, DateTime.UtcNow));
            _exitedQty += order.FilledQuantity;
            _totalExitProceeds += order.Price * order.FilledQuantity;
            ExitTime = order.Timestamp;
        }

        public HistoricTradeRecord ToHistoricTrade()
        {
            var avgExitPrice = _exitedQty > 0 ? _totalExitProceeds / _exitedQty : 0;
            return new HistoricTradeRecord
            {
                Symbol = Symbol,
                Direction = Direction,
                EntryPrice = AvgEntryPrice,
                ExitPrice = avgExitPrice,
                Quantity = FilledQty,
                Profit = RealizedPnl,
                ProfitPct = AvgEntryPrice > 0 && avgExitPrice > 0
                    ? (Direction == TradeDirection.Short
                        ? ((AvgEntryPrice - avgExitPrice) / AvgEntryPrice) * 100
                        : ((avgExitPrice - AvgEntryPrice) / AvgEntryPrice) * 100)
                    : 0,
                EntryTime = EntryTime ?? DateTime.MinValue,
                ExitTime = ExitTime ?? DateTime.UtcNow,
                EntryOrderCount = _entryOrders.Count,
                ExitOrderCount = _exitOrders.Count
            };
        }

        public void Reset()
        {
            FilledQty = 0;
            AvgEntryPrice = 0;
            _totalEntryCost = 0;
            _totalExitProceeds = 0;
            _exitedQty = 0;
            _entryOrders.Clear();
            _exitOrders.Clear();
            EntryTime = null;
            ExitTime = null;
            State = PositionState.NoPosition;
        }

        public IReadOnlyList<OrderRecord> EntryOrders => _entryOrders;
        public IReadOnlyList<OrderRecord> ExitOrders => _exitOrders;
    }

    public class OrderRecord
    {
        public ExchangeOrder Order { get; }
        public DateTime RecordedAt { get; }

        public OrderRecord(ExchangeOrder order, DateTime recordedAt)
        {
            Order = order;
            RecordedAt = recordedAt;
        }
    }

    public class HistoricTradeRecord
    {
        public string Symbol { get; set; }
        public TradeDirection Direction { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal Profit { get; set; }
        public decimal ProfitPct { get; set; }
        public DateTime EntryTime { get; set; }
        public DateTime ExitTime { get; set; }
        public int EntryOrderCount { get; set; }
        public int ExitOrderCount { get; set; }
    }
}
