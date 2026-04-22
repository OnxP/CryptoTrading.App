using System;

namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Exchange-agnostic execution (partial or full fill) of an order.
    /// Emitted by the user-data stream / order-update websocket so the
    /// trade monitor can reconcile fills to submitted orders without
    /// polling REST.
    /// </summary>
    public class ExchangeFill
    {
        public string ExchangeId { get; set; }
        public string OrderId { get; set; }
        public string ClientOrderId { get; set; }
        public string Symbol { get; set; }
        public ExchangeOrderSide Side { get; set; }

        /// <summary>Quantity actually filled on this execution (not cumulative).</summary>
        public decimal FilledQuantity { get; set; }

        /// <summary>Price of this execution.</summary>
        public decimal Price { get; set; }

        /// <summary>Commission / fee paid for this execution, in <see cref="CommissionAsset"/>.</summary>
        public decimal Commission { get; set; }
        public string CommissionAsset { get; set; }

        /// <summary>Order status AFTER this execution was applied.</summary>
        public ExchangeOrderStatus Status { get; set; }

        public DateTime Timestamp { get; set; }
    }
}
