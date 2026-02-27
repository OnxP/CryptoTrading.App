using CryptoTrading.App.Core.Strategy;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased.EntryStrategies
{
    /// <summary>
    /// Places a limit order at the edge of a supply/demand zone.
    ///
    /// For LONG at demand zone: Wait for bullish reversal pattern (engulfing/hammer),
    /// then place limit at the zone's upper edge.
    ///
    /// For SHORT at supply zone: Wait for bearish reversal pattern (engulfing/shooting star),
    /// then place limit at the zone's lower edge.
    ///
    /// This strategy requires the SetupResult to have a NearestZone populated by
    /// the 15M setup evaluator.
    /// </summary>
    public class LimitAtZoneEdgeEntryStrategy : RegimeBasedEntryStrategyBase
    {
        public LimitAtZoneEdgeEntryStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails Evaluate(decimal currentPrice, decimal currentPositionSize, decimal targetPositionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            // Must have a zone from the setup
            if (Setup.NearestZone == null) return result;

            var zone = Setup.NearestZone;

            // Price must be within or very close to the zone
            bool nearZone = zone.Contains(currentPrice) ||
                            zone.DistanceTo(currentPrice) <= (zone.High - zone.Low) * 0.5m;
            if (!nearZone) return result;

            var recentCandles = QuoteHub.Quotes.TakeLast(5).ToList();

            if (Setup.Direction == TradeDirection.Long && zone.Type == ZoneType.Demand)
            {
                // At demand zone: look for bullish reversal patterns
                bool bullishEngulfing = IsBullishEngulfing(recentCandles);
                bool hammer = IsHammerPattern(recentCandles.Last());
                bool risingMomentum = IsRisingMicroMomentum();

                if (bullishEngulfing || hammer || risingMomentum)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = zone.High;
                    result.Price = zone.High;
                    result.Quantity = targetPositionSize;
                    result.OrderType = "LIMIT";
                }
            }
            else if (Setup.Direction == TradeDirection.Short && zone.Type == ZoneType.Supply)
            {
                // At supply zone: look for bearish reversal patterns
                bool bearishEngulfing = IsBearishEngulfing(recentCandles);
                bool shootingStar = IsShootingStarPattern(recentCandles.Last());
                bool fallingMomentum = IsFallingMicroMomentum();

                if (bearishEngulfing || shootingStar || fallingMomentum)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = zone.Low;
                    result.Price = zone.Low;
                    result.Quantity = targetPositionSize;
                    result.OrderType = "LIMIT";
                }
            }

            return result;
        }
    }
}
