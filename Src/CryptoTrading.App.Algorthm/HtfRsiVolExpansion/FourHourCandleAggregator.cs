using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Aggregates 15-minute candles into 4-hour candles aligned to exchange boundaries
    /// (00:00, 04:00, 08:00, 12:00, 16:00, 20:00 UTC).
    /// Each 4H candle consists of exactly 16 consecutive 15M candles.
    /// </summary>
    public class FourHourCandleAggregator
    {
        private readonly List<Quote> _buffer = new List<Quote>();
        private DateTime _currentPeriodStart = DateTime.MinValue;

        /// <summary>
        /// Feed a single 15M candle. Returns a completed 4H candle when a period boundary
        /// is crossed, or null if the current 4H period is still building.
        /// </summary>
        public Quote TryAggregate(Quote candle15M)
        {
            var periodStart = Get4HPeriodStart(candle15M.Timestamp);

            if (_currentPeriodStart == DateTime.MinValue)
            {
                _currentPeriodStart = periodStart;
                _buffer.Add(candle15M);
                return null;
            }

            if (periodStart != _currentPeriodStart)
            {
                // New 4H period started - the previous buffer is a complete 4H candle
                var aggregated = Aggregate(_buffer);
                _buffer.Clear();
                _buffer.Add(candle15M);
                _currentPeriodStart = periodStart;
                return aggregated;
            }

            _buffer.Add(candle15M);
            return null;
        }

        /// <summary>
        /// Aggregate a batch of historic 15M candles into 4H candles.
        /// Groups by 4H period and aggregates each complete group.
        /// </summary>
        public List<Quote> AggregateHistoric(IEnumerable<Quote> candles15M)
        {
            var result = new List<Quote>();
            var groups = candles15M
                .GroupBy(c => Get4HPeriodStart(c.Timestamp))
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                var bars = group.OrderBy(c => c.Timestamp).ToList();
                // Only include groups with enough candles (allow slightly incomplete)
                if (bars.Count >= 14)
                    result.Add(Aggregate(bars));
            }

            // Prime the buffer with any trailing incomplete group
            var lastGroup = candles15M
                .GroupBy(c => Get4HPeriodStart(c.Timestamp))
                .OrderBy(g => g.Key)
                .LastOrDefault();

            if (lastGroup != null)
            {
                var lastBars = lastGroup.OrderBy(c => c.Timestamp).ToList();
                if (lastBars.Count < 16)
                {
                    // Incomplete final period - keep in buffer for live aggregation
                    _buffer.Clear();
                    _buffer.AddRange(lastBars);
                    _currentPeriodStart = Get4HPeriodStart(lastBars.First().Timestamp);
                }
                else
                {
                    _currentPeriodStart = Get4HPeriodStart(lastBars.Last().Timestamp);
                }
            }

            return result;
        }

        /// <summary>
        /// Determine the 4H period start for a candle given its close timestamp.
        /// A 15M candle with CloseTime T covers [T-15min, T).
        /// The open time determines which 4H period the candle belongs to.
        /// </summary>
        private static DateTime Get4HPeriodStart(DateTime candleCloseTime)
        {
            var openTime = candleCloseTime.AddMinutes(-15);
            int periodHour = (openTime.Hour / 4) * 4;
            return openTime.Date.AddHours(periodHour);
        }

        /// <summary>
        /// Aggregate multiple 15M candles into a single 4H candle (OHLCV).
        /// </summary>
        private static Quote Aggregate(List<Quote> candles)
        {
            return new Quote
            {
                Timestamp = candles.Last().Timestamp,
                Open = candles.First().Open,
                High = candles.Max(c => c.High),
                Low = candles.Min(c => c.Low),
                Close = candles.Last().Close,
                Volume = candles.Sum(c => c.Volume)
            };
        }
    }
}
