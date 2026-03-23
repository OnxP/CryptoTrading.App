using System;

namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Exchange-agnostic order representation (replaces Binance.Order).
    /// Used across all exchange providers for a unified order model.
    /// </summary>
    public class ExchangeOrder
    {
        public string ExchangeId { get; set; }
        public string OrderId { get; set; }
        public string ClientOrderId { get; set; }
        public string Symbol { get; set; }
        public ExchangeOrderSide Side { get; set; }
        public ExchangeOrderType Type { get; set; }
        public ExchangeOrderStatus Status { get; set; }
        public decimal Price { get; set; }
        public decimal StopPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal FilledQuantity { get; set; }
        public decimal QuoteQuantity { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Whether this order has been fully executed
        /// </summary>
        public bool IsFilled => Status == ExchangeOrderStatus.Filled;

        /// <summary>
        /// Whether this order is still active (not terminal)
        /// </summary>
        public bool IsActive => Status == ExchangeOrderStatus.New
                             || Status == ExchangeOrderStatus.PartiallyFilled;

        /// <summary>
        /// Creates a mock filled order for backtesting
        /// </summary>
        public static ExchangeOrder CreateFilledOrder(
            string exchangeId, string symbol, ExchangeOrderSide side,
            decimal price, decimal quantity, DateTime timestamp)
        {
            return new ExchangeOrder
            {
                ExchangeId = exchangeId,
                OrderId = Guid.NewGuid().ToString(),
                ClientOrderId = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Side = side,
                Type = ExchangeOrderType.Market,
                Status = ExchangeOrderStatus.Filled,
                Price = price,
                Quantity = quantity,
                FilledQuantity = quantity,
                QuoteQuantity = price * quantity,
                Timestamp = timestamp
            };
        }
    }
}
