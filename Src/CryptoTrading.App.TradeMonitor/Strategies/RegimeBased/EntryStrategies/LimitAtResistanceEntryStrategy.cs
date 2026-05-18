using CryptoTrading.App.Core.Strategy;
using System.Linq;

namespace CryptoTrading.App.Monitor.Strategies.RegimeBased.EntryStrategies
{
    /// <summary>
    /// Places a limit order at a resistance level, waiting for bearish confirmation
    /// patterns (engulfing, shooting star, or falling micro momentum) within the entry zone.
    /// </summary>
    public class LimitAtResistanceEntryStrategy : RegimeBasedEntryStrategyBase
    {
        public LimitAtResistanceEntryStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails Evaluate(decimal currentPrice, decimal currentPositionSize, decimal targetPositionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            bool inEntryZone = currentPrice >= Setup.EntryZoneLow && currentPrice <= Setup.EntryZoneHigh;
            if (!inEntryZone) return result;

            var recentCandles = QuoteHub.Quotes.TakeLast(5).ToList();

            bool bearishEngulfing = IsBearishEngulfing(recentCandles);
            bool shootingStar = IsShootingStarPattern(recentCandles.Last());
            bool fallingMomentum = IsFallingMicroMomentum();

            if (bearishEngulfing || shootingStar || fallingMomentum)
            {
                result.ShouldTrade = true;
                result.EntryPrice = currentPrice;
                result.Price = currentPrice;
                result.Quantity = targetPositionSize;
                result.OrderType = "LIMIT";
            }

            return result;
        }
    }
}
