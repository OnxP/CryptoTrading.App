using CryptoTrading.App.Core.Strategy;
using System;

namespace CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Exits at a predetermined target, with early exit if momentum
    /// is exhausted when progress toward the target exceeds 80%.
    /// </summary>
    public class FixedTargetExitStrategy : RegimeBasedExitStrategyBase
    {
        public FixedTargetExitStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            // Check for momentum exhaustion near target
            decimal totalTargetDistance = Math.Abs(Setup.TakeProfit - EntryPrice);
            decimal distanceToTarget = Setup.Direction == TradeDirection.Long
                ? Setup.TakeProfit - currentPrice
                : currentPrice - Setup.TakeProfit;
            decimal progressToTarget = totalTargetDistance > 0 ? 1 - (distanceToTarget / totalTargetDistance) : 0;

            if (progressToTarget > 0.8m && IsMomentumExhausted())
            {
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }

            return result;
        }
    }
}
