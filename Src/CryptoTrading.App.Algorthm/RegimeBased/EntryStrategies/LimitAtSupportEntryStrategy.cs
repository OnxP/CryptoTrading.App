using CryptoTrading.App.Core.Strategy;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased.EntryStrategies
{
    /// <summary>
    /// Places a limit order at a support level, waiting for bullish confirmation
    /// patterns (engulfing, hammer, or rising micro momentum) within the entry zone.
    /// </summary>
    public class LimitAtSupportEntryStrategy : RegimeBasedEntryStrategyBase
    {
        public LimitAtSupportEntryStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails Evaluate(decimal currentPrice, decimal currentPositionSize, decimal targetPositionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            bool inEntryZone = currentPrice >= Setup.EntryZoneLow && currentPrice <= Setup.EntryZoneHigh;
            if (!inEntryZone) return result;

            var recentCandles = QuoteHub.Quotes.TakeLast(5).ToList();

            bool bullishEngulfing = IsBullishEngulfing(recentCandles);
            bool hammerPattern = IsHammerPattern(recentCandles.Last());
            bool risingMomentum = IsRisingMicroMomentum();

            if (bullishEngulfing || hammerPattern || risingMomentum)
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
