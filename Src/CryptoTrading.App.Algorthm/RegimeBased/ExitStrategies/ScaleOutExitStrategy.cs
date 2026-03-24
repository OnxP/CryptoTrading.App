using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;

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
            Logger?.LogDebug($"[1M EXIT ScaleOut] R:{rMultiple:F2} price:{currentPrice:F2} pos:{positionSize}");

            if (rMultiple >= 3.0m)
            {
                Logger?.LogInformation($"[1M EXIT ScaleOut] FULL EXIT @ 3R ({rMultiple:F2}) price:{currentPrice:F2}");
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }
            else if (rMultiple >= 2.0m && IsMomentumExhausted())
            {
                Logger?.LogInformation($"[1M EXIT ScaleOut] EXIT @ 2R momentum exhausted ({rMultiple:F2}) price:{currentPrice:F2}");
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }
            else if (rMultiple >= 1.0m)
            {
                Logger?.LogInformation($"[1M EXIT ScaleOut] PARTIAL 33% @ 1R ({rMultiple:F2}) price:{currentPrice:F2}");
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize * 0.33m;
                result.OrderType = "LIMIT";
            }

            return result;
        }
    }
}
