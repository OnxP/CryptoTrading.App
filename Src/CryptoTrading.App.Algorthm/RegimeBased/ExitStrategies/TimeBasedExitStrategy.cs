using CryptoTrading.App.Core.Strategy;
using System;

namespace CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Exits after a configurable number of bars if the trade is stagnant
    /// (less than 0.5% movement), preventing capital from being tied up
    /// in non-performing positions.
    /// </summary>
    public class TimeBasedExitStrategy : RegimeBasedExitStrategyBase
    {
        private readonly int _timeStopBars = 20; // Reduced from 30 — cut non-performing trades faster

        public TimeBasedExitStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal pnlPercent = EntryPrice != 0
                ? (Setup.Direction == TradeDirection.Long
                    ? (currentPrice - EntryPrice) / EntryPrice * 100
                    : (EntryPrice - currentPrice) / EntryPrice * 100)
                : 0;

            bool tradeStagnant = Math.Abs(pnlPercent) < 0.5m;

            if (BarsHeld >= _timeStopBars && tradeStagnant)
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
