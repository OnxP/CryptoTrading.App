using System;

namespace CryptoTrading.App.Core.Strategy
{
    public class SupplyDemandZone
    {
        public ZoneType Type { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal MidPoint => (High + Low) / 2m;
        public int Strength { get; set; }
        public bool IsFresh { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        public bool Contains(decimal price) => price >= Low && price <= High;

        public decimal DistanceTo(decimal price)
        {
            if (Contains(price)) return 0;
            return Math.Min(Math.Abs(price - High), Math.Abs(price - Low));
        }
    }
}
