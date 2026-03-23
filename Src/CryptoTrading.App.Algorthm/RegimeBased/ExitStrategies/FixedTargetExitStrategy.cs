using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
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

            decimal totalTargetDistance = Math.Abs(Setup.TakeProfit - EntryPrice);
            decimal distanceToTarget = Setup.Direction == TradeDirection.Long
                ? Setup.TakeProfit - currentPrice
                : currentPrice - Setup.TakeProfit;
            decimal progressToTarget = totalTargetDistance > 0 ? 1 - (distanceToTarget / totalTargetDistance) : 0;

            bool momentumExhausted = progressToTarget > 0.8m && IsMomentumExhausted();
            Logger?.LogDebug($"[1M EXIT FixedTarget] progress:{progressToTarget:P0} price:{currentPrice:F2} tp:{Setup.TakeProfit:F2} entry:{EntryPrice:F2} momentumExhausted:{momentumExhausted}");

            if (momentumExhausted)
            {
                Logger?.LogInformation($"[1M EXIT FixedTarget] TRIGGER @ {currentPrice:F2} (progress:{progressToTarget:P0} momentum exhausted)");
                result.ShouldTrade = true;
                result.Price = currentPrice;
                result.Quantity = positionSize;
                result.OrderType = "MARKET";
            }

            return result;
        }
    }
}
