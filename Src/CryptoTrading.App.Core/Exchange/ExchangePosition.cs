using System;

namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Exchange-agnostic open-position snapshot. Used primarily for derivatives
    /// (futures) where balances alone don't describe exposure.
    ///
    /// For spot/margin venues this is typically synthesized from balances.
    /// For futures this maps to a single symbol × positionSide row on the
    /// exchange (Binance /fapi/v2/positionRisk or equivalent).
    /// </summary>
    public class ExchangePosition
    {
        public string ExchangeId { get; set; }
        public string Symbol { get; set; }
        public PositionSide Side { get; set; }

        /// <summary>Signed quantity in the base asset. Positive = long, negative = short.</summary>
        public decimal Quantity { get; set; }

        /// <summary>Volume-weighted entry price.</summary>
        public decimal EntryPrice { get; set; }

        /// <summary>Exchange mark price at the time the snapshot was taken.</summary>
        public decimal MarkPrice { get; set; }

        /// <summary>Currently applied leverage for this symbol.</summary>
        public int Leverage { get; set; }

        /// <summary>Unrealized PnL reported by the exchange, in quote asset.</summary>
        public decimal UnrealizedPnl { get; set; }

        /// <summary>Notional value in quote asset (abs(Quantity) × MarkPrice).</summary>
        public decimal Notional { get; set; }

        public DateTime Timestamp { get; set; }

        public bool IsFlat => Quantity == 0;
    }
}
