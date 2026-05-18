using CryptoTrading.App.Core.Strategy;
using System.Linq;

namespace CryptoTrading.App.Monitor.Strategies.RegimeBased.EntryStrategies
{
    /// <summary>
    /// Enters on a clean break of a level with volume confirmation,
    /// or on a retest of the breakout level.
    /// </summary>
    public class BreakoutEntryStrategy : RegimeBasedEntryStrategyBase
    {
        public BreakoutEntryStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails Evaluate(decimal currentPrice, decimal currentPositionSize, decimal targetPositionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            var recentCandles = QuoteHub.Quotes.TakeLast(20).ToList();
            var lastCandle = recentCandles.Last();

            if (Setup.Direction == TradeDirection.Long)
            {
                decimal breakoutLevel = Setup.EntryZoneLow;
                bool volumeConfirm = GetVolumeRatio() > 1.5m;
                bool cleanBreak = (decimal)lastCandle.Close > breakoutLevel && (decimal)lastCandle.Open < breakoutLevel;

                bool retestingBreakout = recentCandles.Any(c => (decimal)c.Close > breakoutLevel) &&
                                         currentPrice <= breakoutLevel * 1.003m &&
                                         currentPrice >= breakoutLevel * 0.997m;

                if ((cleanBreak && volumeConfirm) || retestingBreakout)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetPositionSize;
                    result.OrderType = cleanBreak ? "MARKET" : "LIMIT";
                }
            }
            else
            {
                decimal breakdownLevel = Setup.EntryZoneHigh;
                bool volumeConfirm = GetVolumeRatio() > 1.5m;
                bool cleanBreak = (decimal)lastCandle.Close < breakdownLevel && (decimal)lastCandle.Open > breakdownLevel;

                bool retestingBreakdown = recentCandles.Any(c => (decimal)c.Close < breakdownLevel) &&
                                          currentPrice >= breakdownLevel * 0.997m &&
                                          currentPrice <= breakdownLevel * 1.003m;

                if ((cleanBreak && volumeConfirm) || retestingBreakdown)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetPositionSize;
                    result.OrderType = cleanBreak ? "MARKET" : "LIMIT";
                }
            }

            return result;
        }
    }
}
