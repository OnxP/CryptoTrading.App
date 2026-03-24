using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
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

            if (Setup.NearestZone == null)
            {
                Logger?.LogDebug("[1M ENTRY ZoneEdge] No zone in setup");
                return result;
            }

            var zone = Setup.NearestZone;

            bool nearZone = zone.Contains(currentPrice) ||
                            zone.DistanceTo(currentPrice) <= (zone.High - zone.Low) * 0.5m;
            if (!nearZone)
            {
                Logger?.LogDebug($"[1M ENTRY ZoneEdge] Price {currentPrice:F2} not near zone [{zone.Low:F2}-{zone.High:F2}] dist:{zone.DistanceTo(currentPrice):F2}");
                return result;
            }

            var recentCandles = QuoteHub.Quotes.TakeLast(5).ToList();

            if (Setup.Direction == TradeDirection.Long && zone.Type == ZoneType.Demand)
            {
                bool bullishEngulfing = IsBullishEngulfing(recentCandles);
                bool hammer = IsHammerPattern(recentCandles.Last());
                bool risingMomentum = IsRisingMicroMomentum();

                Logger?.LogDebug($"[1M ENTRY ZoneEdge LONG] price:{currentPrice:F2} zone:[{zone.Low:F2}-{zone.High:F2}] | engulfing:{bullishEngulfing} hammer:{hammer} momentum:{risingMomentum}");

                if (bullishEngulfing || hammer || risingMomentum)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = zone.High;
                    result.Price = zone.High;
                    result.Quantity = targetPositionSize;
                    result.OrderType = "LIMIT";
                    Logger?.LogInformation($"[1M ENTRY ZoneEdge] TRIGGER LONG LIMIT @ {zone.High:F2}");
                }
            }
            else if (Setup.Direction == TradeDirection.Short && zone.Type == ZoneType.Supply)
            {
                bool bearishEngulfing = IsBearishEngulfing(recentCandles);
                bool shootingStar = IsShootingStarPattern(recentCandles.Last());
                bool fallingMomentum = IsFallingMicroMomentum();

                Logger?.LogDebug($"[1M ENTRY ZoneEdge SHORT] price:{currentPrice:F2} zone:[{zone.Low:F2}-{zone.High:F2}] | engulfing:{bearishEngulfing} shootingStar:{shootingStar} momentum:{fallingMomentum}");

                if (bearishEngulfing || shootingStar || fallingMomentum)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = zone.Low;
                    result.Price = zone.Low;
                    result.Quantity = targetPositionSize;
                    result.OrderType = "LIMIT";
                    Logger?.LogInformation($"[1M ENTRY ZoneEdge] TRIGGER SHORT LIMIT @ {zone.Low:F2}");
                }
            }

            return result;
        }
    }
}
