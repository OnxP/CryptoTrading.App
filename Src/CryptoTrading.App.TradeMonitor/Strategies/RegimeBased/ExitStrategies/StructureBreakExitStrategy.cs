using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace CryptoTrading.App.Monitor.Strategies.RegimeBased.ExitStrategies
{
    /// <summary>
    /// Exits when price breaks recent swing structure with confirmation.
    ///
    /// Improvement over old strategy:
    /// - Old: single bearish candle + 20-bar low → exit (too noisy on 1M)
    /// - New: requires structure break + volume surge (>1.3x avg) + 2 consecutive
    ///   confirming candles to filter out false breaks from noise
    /// </summary>
    public class StructureBreakExitStrategy : RegimeBasedExitStrategyBase
    {
        private int _confirmingBars;  // Count of consecutive confirming bars after a structure break

        public StructureBreakExitStrategy(SetupResult setup) : base(setup) { }

        public override void ResetForNewTrade()
        {
            base.ResetForNewTrade();
            _confirmingBars = 0;
        }

        protected override TradeDetails EvaluateExit(decimal currentPrice, decimal positionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            var recentCandles = QuoteHub.Quotes.TakeLast(20).ToList();
            if (recentCandles.Count < 20) return result;

            // Volume confirmation: current volume must be elevated
            var volumes = QuoteHub.Quotes.TakeLast(21).Select(q => (decimal)q.Volume).ToList();
            var avgVolume = volumes.Count > 1 ? volumes.Take(20).Average() : 0;
            var currentVolume = volumes.Last();
            bool volumeConfirmed = avgVolume > 0 && currentVolume >= avgVolume * 1.3m;

            if (Setup.Direction == TradeDirection.Long)
            {
                var prevLow = (decimal)recentCandles.SkipLast(1).Min(c => c.Low);
                var currentLow = (decimal)recentCandles.Last().Low;
                bool bearishClose = recentCandles.Last().Close < recentCandles.Last().Open;
                bool newLow = currentLow < prevLow;

                if (newLow && bearishClose)
                    _confirmingBars++;
                else
                    _confirmingBars = 0;

                Logger?.LogDebug($"[1M EXIT StructBreak LONG] newLow:{newLow} bearish:{bearishClose} vol:{volumeConfirmed} confirms:{_confirmingBars}");

                // Require 2 confirming bars + volume (was: 1 bar, no volume)
                if (_confirmingBars >= 2 && volumeConfirmed)
                {
                    Logger?.LogInformation($"[1M EXIT StructBreak] TRIGGER LONG @ {currentPrice:F2} (structure break with volume confirmation)");
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
                bool bullishClose = recentCandles.Last().Close > recentCandles.Last().Open;
                bool newHigh = currentHigh > prevHigh;

                if (newHigh && bullishClose)
                    _confirmingBars++;
                else
                    _confirmingBars = 0;

                Logger?.LogDebug($"[1M EXIT StructBreak SHORT] newHigh:{newHigh} bullish:{bullishClose} vol:{volumeConfirmed} confirms:{_confirmingBars}");

                if (_confirmingBars >= 2 && volumeConfirmed)
                {
                    Logger?.LogInformation($"[1M EXIT StructBreak] TRIGGER SHORT @ {currentPrice:F2} (structure break with volume confirmation)");
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
