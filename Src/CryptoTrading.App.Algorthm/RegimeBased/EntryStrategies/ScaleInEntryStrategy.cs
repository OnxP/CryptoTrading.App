using CryptoTrading.App.Core.Strategy;
using System;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased.EntryStrategies
{
    /// <summary>
    /// Enters in portions at multiple levels within the entry zone,
    /// requiring a reversal signal at each level before adding to the position.
    /// </summary>
    public class ScaleInEntryStrategy : RegimeBasedEntryStrategyBase
    {
        private readonly decimal _scaleInPortion = 0.33m;

        public ScaleInEntryStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails Evaluate(decimal currentPrice, decimal currentPositionSize, decimal targetPositionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal entryZoneRange = Setup.EntryZoneHigh - Setup.EntryZoneLow;
            decimal firstLevel = Setup.EntryZoneLow + entryZoneRange * 0.33m;
            decimal secondLevel = Setup.EntryZoneLow + entryZoneRange * 0.66m;
            decimal thirdLevel = Setup.EntryZoneHigh;

            bool atFirstLevel = Math.Abs(currentPrice - firstLevel) < entryZoneRange * 0.1m;
            bool atSecondLevel = Math.Abs(currentPrice - secondLevel) < entryZoneRange * 0.1m;
            bool atThirdLevel = Math.Abs(currentPrice - thirdLevel) < entryZoneRange * 0.1m;

            var recentCandles = QuoteHub.Quotes.TakeLast(3).ToList();
            bool hasReversalSignal = Setup.Direction == TradeDirection.Long
                ? (IsBullishEngulfing(recentCandles) || IsHammerPattern(recentCandles.Last()))
                : (IsBearishEngulfing(recentCandles) || IsShootingStarPattern(recentCandles.Last()));

            if ((atFirstLevel || atSecondLevel || atThirdLevel) && hasReversalSignal)
            {
                result.ShouldTrade = true;
                result.EntryPrice = currentPrice;
                result.Price = currentPrice;
                result.Quantity = targetPositionSize * _scaleInPortion;
                result.OrderType = "LIMIT";
            }

            return result;
        }
    }
}
