using CryptoTrading.App.Core.Strategy;
using System.Linq;

namespace CryptoTrading.App.Monitor.Strategies.RegimeBased.EntryStrategies
{
    /// <summary>
    /// Enters with a market order after N consecutive confirming bars
    /// and a break of the micro high/low.
    /// </summary>
    public class MarketOnConfirmationEntryStrategy : RegimeBasedEntryStrategyBase
    {
        private readonly int _confirmationBars = 2;

        public MarketOnConfirmationEntryStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails Evaluate(decimal currentPrice, decimal currentPositionSize, decimal targetPositionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            var recentCandles = QuoteHub.Quotes.TakeLast(_confirmationBars + 1).ToList();

            if (Setup.Direction == TradeDirection.Long)
            {
                bool consecutiveBullish = recentCandles.TakeLast(_confirmationBars).All(c => c.Close > c.Open);
                bool breakingMicroHigh = currentPrice > (decimal)recentCandles.SkipLast(1).Max(c => c.High);

                if (consecutiveBullish && breakingMicroHigh)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetPositionSize;
                    result.OrderType = "MARKET";
                }
            }
            else
            {
                bool consecutiveBearish = recentCandles.TakeLast(_confirmationBars).All(c => c.Close < c.Open);
                bool breakingMicroLow = currentPrice < (decimal)recentCandles.SkipLast(1).Min(c => c.Low);

                if (consecutiveBearish && breakingMicroLow)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetPositionSize;
                    result.OrderType = "MARKET";
                }
            }

            return result;
        }
    }
}
