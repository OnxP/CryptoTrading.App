using CryptoTrading.App.Core.Strategy;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Exits when the price breaks the recent swing structure:
    /// for longs, a new lower low with a bearish close;
    /// for shorts, a new higher high with a bullish close.
    /// </summary>
    public class StructureBreakExitStrategy : RegimeBasedExitStrategyBase
    {
        public StructureBreakExitStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            var recentCandles = QuoteHub.Quotes.TakeLast(20).ToList(); // Widened from 10 — true structure breaks, not noise

            if (Setup.Direction == TradeDirection.Long)
            {
                var prevLow = (decimal)recentCandles.SkipLast(1).Min(c => c.Low);
                var currentLow = (decimal)recentCandles.Last().Low;

                if (currentLow < prevLow && recentCandles.Last().Close < recentCandles.Last().Open)
                {
                    result.ShouldTrade = true;
                    result.Price = currentPrice;
                    result.Quantity = positionSize;
                    result.OrderType = "MARKET";
                }
            }
            else
            {
                var prevHigh = (decimal)recentCandles.SkipLast(1).Max(c => c.High);
                var currentHigh = (decimal)recentCandles.Last().High;

                if (currentHigh > prevHigh && recentCandles.Last().Close > recentCandles.Last().Open)
                {
                    result.ShouldTrade = true;
                    result.Price = currentPrice;
                    result.Quantity = positionSize;
                    result.OrderType = "MARKET";
                }
            }

            return result;
        }
    }
}
