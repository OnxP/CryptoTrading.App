using CryptoTrading.App.Core.Strategy;

namespace CryptoTrading.App.Algorithm.RegimeBased.ExitStrategies
{
    /// <summary>
    /// ATR-based trailing stop that activates after the trade reaches
    /// a configurable R-multiple, trailing at a multiple of ATR from the extreme.
    /// </summary>
    public class TrailingStopExitStrategy : RegimeBasedExitStrategyBase
    {
        private readonly decimal _trailingStartMultiple = 1.0m;
        private readonly decimal _trailingAtrMultiple = 1.5m;

        public TrailingStopExitStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            decimal rMultiple = GetRMultiple(currentPrice);

            if (rMultiple >= _trailingStartMultiple)
            {
                decimal atr = GetCurrentAtr();
                decimal trailingDistance = atr * _trailingAtrMultiple;

                decimal trailingStopLevel = Setup.Direction == TradeDirection.Long
                    ? HighestPrice - trailingDistance
                    : LowestPrice + trailingDistance;

                bool trailingStopHit = Setup.Direction == TradeDirection.Long
                    ? currentPrice <= trailingStopLevel
                    : currentPrice >= trailingStopLevel;

                if (trailingStopHit)
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
