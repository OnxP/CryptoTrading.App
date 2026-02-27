using System;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Classifies a price zone as either supply (resistance) or demand (support).
    /// </summary>
    public enum ZoneType
    {
        Supply,  // Resistance area - price rejected downward from here
        Demand   // Support area - price rejected upward from here
    }

    /// <summary>
    /// Represents a supply or demand zone detected on the 4H timeframe.
    /// Zones are passed downstream to 15M and 1M timeframes for
    /// trade filtering and precise entry placement.
    /// </summary>
    public class SupplyDemandZone
    {
        /// <summary>Whether this is a supply (resistance) or demand (support) zone.</summary>
        public ZoneType Type { get; set; }

        /// <summary>Upper boundary of the zone.</summary>
        public decimal High { get; set; }

        /// <summary>Lower boundary of the zone.</summary>
        public decimal Low { get; set; }

        /// <summary>Midpoint of the zone for proximity calculations.</summary>
        public decimal MidPoint => (High + Low) / 2m;

        /// <summary>Number of times price has tested this zone.</summary>
        public int Strength { get; set; }

        /// <summary>True if price hasn't returned to this zone since creation (first test = highest probability).</summary>
        public bool IsFresh { get; set; } = true;

        /// <summary>When this zone was first detected.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Check if a price falls within this zone.</summary>
        public bool Contains(decimal price) => price >= Low && price <= High;

        /// <summary>Distance from a price to the nearest edge of this zone.</summary>
        public decimal DistanceTo(decimal price)
        {
            if (Contains(price)) return 0;
            return Math.Min(Math.Abs(price - High), Math.Abs(price - Low));
        }
    }
}
