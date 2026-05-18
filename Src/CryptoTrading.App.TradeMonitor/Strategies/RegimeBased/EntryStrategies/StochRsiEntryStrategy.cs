using CryptoTrading.App.Core.Strategy;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System.Linq;

namespace CryptoTrading.App.Monitor.Strategies.RegimeBased.EntryStrategies
{
    /// <summary>
    /// Uses Stochastic RSI on the 1M timeframe for precise entry timing in trending markets.
    ///
    /// For LONG entries: Wait for StochRsi to dip below oversold threshold (20),
    /// then cross above the Signal line (bullish crossover) with rising micro momentum.
    ///
    /// For SHORT entries: Wait for StochRsi to rise above overbought threshold (80),
    /// then cross below the Signal line (bearish crossover) with falling micro momentum.
    /// </summary>
    public class StochRsiEntryStrategy : RegimeBasedEntryStrategyBase
    {
        private readonly int _rsiPeriod = 14;
        private readonly int _stochPeriod = 14;
        private readonly int _signalPeriod = 3;
        private readonly int _smoothPeriod = 3;
        private readonly decimal _oversoldThreshold = 20m;
        private readonly decimal _overboughtThreshold = 80m;

        public StochRsiEntryStrategy(SetupResult setup) : base(setup) { }

        protected override TradeDetails Evaluate(decimal currentPrice, decimal currentPositionSize, decimal targetPositionSize)
        {
            var result = new TradeDetails { ShouldTrade = false };

            // Need enough data for StochRsi calculation
            if (QuoteHub.Quotes.Count < 50)
            {
                Logger?.LogDebug($"[1M ENTRY StochRsi] Insufficient data ({QuoteHub.Quotes.Count}/50)");
                return result;
            }

            var stochRsi = QuoteHub.Quotes.ToStochRsi(_rsiPeriod, _stochPeriod, _signalPeriod, _smoothPeriod);
            if (stochRsi == null || stochRsi.Count < 2) return result;

            var current = stochRsi[stochRsi.Count - 1];
            var previous = stochRsi[stochRsi.Count - 2];

            if (current.StochRsi == null || current.Signal == null ||
                previous.StochRsi == null || previous.Signal == null)
                return result;

            decimal currStoch = (decimal)current.StochRsi.Value;
            decimal currSignal = (decimal)current.Signal.Value;
            decimal prevStoch = (decimal)previous.StochRsi.Value;
            decimal prevSignal = (decimal)previous.Signal.Value;

            if (Setup.Direction == TradeDirection.Long)
            {
                bool wasOversold = prevStoch < _oversoldThreshold;
                bool bullishCrossover = prevStoch <= prevSignal && currStoch > currSignal;
                bool confirmingMomentum = IsRisingMicroMomentum();

                Logger?.LogDebug($"[1M ENTRY StochRsi LONG] stoch:{currStoch:F2} sig:{currSignal:F2} prevStoch:{prevStoch:F2} prevSig:{prevSignal:F2} | wasOversold:{wasOversold} cross:{bullishCrossover} momentum:{confirmingMomentum}");

                if ((wasOversold || currStoch < 50) && bullishCrossover && confirmingMomentum)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetPositionSize;
                    result.OrderType = "MARKET";
                    Logger?.LogInformation($"[1M ENTRY StochRsi] TRIGGER LONG @ {currentPrice:F2}");
                }
            }
            else
            {
                bool wasOverbought = prevStoch > _overboughtThreshold;
                bool bearishCrossover = prevStoch >= prevSignal && currStoch < currSignal;
                bool confirmingMomentum = IsFallingMicroMomentum();

                Logger?.LogDebug($"[1M ENTRY StochRsi SHORT] stoch:{currStoch:F2} sig:{currSignal:F2} prevStoch:{prevStoch:F2} prevSig:{prevSignal:F2} | wasOverbought:{wasOverbought} cross:{bearishCrossover} momentum:{confirmingMomentum}");

                if ((wasOverbought || currStoch > 50) && bearishCrossover && confirmingMomentum)
                {
                    result.ShouldTrade = true;
                    result.EntryPrice = currentPrice;
                    result.Price = currentPrice;
                    result.Quantity = targetPositionSize;
                    result.OrderType = "MARKET";
                    Logger?.LogInformation($"[1M ENTRY StochRsi] TRIGGER SHORT @ {currentPrice:F2}");
                }
            }

            return result;
        }
    }
}
