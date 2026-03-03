using CryptoTrading.App.Core.Strategy;

namespace CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Exits in portions at R-multiples:
    /// - 1R: partial exit (1/3 of position)
    /// - 2R: exit remaining if momentum exhausted
    /// - 3R: full exit
    /// </summary>
    public class ScaleOutExitStrategy : RegimeBasedExitStrategyBase
    {
        public ScaleOutExitStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal rMultiple = GetRMultiple(currentPrice);

            // Scale out at R-multiples
            if (rMultiple >= 3.0m)
            {
                // Final exit
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }
            else if (rMultiple >= 2.0m && IsMomentumExhausted())
            {
                // Exit remaining on reversal
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }
            else if (rMultiple >= 1.0m)
            {
                // Partial exit at 1R (take 1/3)
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize * 0.33m;
                result.OrderType = "LIMIT";
            }

            return result;
        }
    }
}
