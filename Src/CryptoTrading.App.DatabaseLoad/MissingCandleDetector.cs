using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Database;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.DatabaseLoad
{
    /// <summary>
    /// Detects gaps in the CandleStickDbs table for a given set of symbols and intervals
    /// and downloads only the missing candles from the Binance API.
    ///
    /// Flow per (symbol, interval):
    ///   1. Query DB for all existing OpenTimes in [from, to)
    ///   2. Generate the expected sequence of OpenTimes
    ///   3. Missing = expected - existing
    ///   4. Group consecutive missing timestamps into ranges (fewer API calls)
    ///   5. Fetch each range from Binance (max 1000 candles per request) and bulk-insert
    /// </summary>
    public class MissingCandleDetector
    {
        private readonly IExchangeProvider _provider;
        private readonly ILogger<MissingCandleDetector> _logger;
        private readonly SemaphoreSlim _providerSemaphore;
        private const int BinanceMaxLimit = 1000;

        /// <param name="maxConcurrency">
        /// Maximum number of simultaneous Binance API calls.
        /// Binance allows up to 1 200 requests/min; 10 concurrent callers
        /// is a safe default that keeps well inside that limit.
        /// </param>
        public MissingCandleDetector(IExchangeProvider api, ILogger<MissingCandleDetector> logger,
            int maxConcurrency = 10)
        {
            _provider = api;
            _logger = logger;
            _providerSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        /// <summary>
        /// Checks every (symbol, interval) combination and fills any missing candles.
        /// All symbols are processed concurrently; API calls are gated by the semaphore.
        /// </summary>
        public Task FillMissingCandlesAsync(
            IEnumerable<string> symbols,
            IEnumerable<CandleInterval> intervals,
            DateTime from,
            DateTime to)
        {
            var intervalList = intervals.ToList(); // materialise once — shared across tasks
            var tasks = symbols.Select(symbol =>
                FillMissingForSymbolAsync(symbol, intervalList, from, to));
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Checks all intervals for a single symbol and fills any missing candles.
        /// All intervals are processed concurrently; API calls are gated by the semaphore.
        /// Call this directly from Program.cs when you want to handle each symbol
        /// individually (check → load → move on to the next symbol).
        /// </summary>
        public Task FillMissingForSymbolAsync(
            string symbol,
            IEnumerable<CandleInterval> intervals,
            DateTime from,
            DateTime to)
        {
            var tasks = intervals.Select(interval =>
                FillMissingForSymbolIntervalAsync(symbol, interval, from, to));
            return Task.WhenAll(tasks);
        }

        // -------------------------------------------------------------------------
        // Core per-(symbol, interval) logic
        // -------------------------------------------------------------------------

        private async Task FillMissingForSymbolIntervalAsync(
            string symbol,
            CandleInterval interval,
            DateTime from,
            DateTime to)
        {
            try
            {
                _logger.LogInformation(
                    $"[GapCheck] {symbol} {interval} | Range: {from:yyyy-MM-dd HH:mm} – {to:yyyy-MM-dd HH:mm}");

                // 1. Fetch existing OpenTimes from the DB
                HashSet<DateTime> existing;
                using (var ctx = new CryptoDbContext())
                {
                    existing = ctx.CandleSticks
                        .Where(c => c.Symbol == symbol
                                 && c.Interval == interval
                                 && c.OpenTime >= from
                                 && c.OpenTime < to)
                        .Select(c => c.OpenTime)
                        .ToHashSet();
                }

                _logger.LogInformation(
                    $"[GapCheck] {symbol} {interval} | {existing.Count} candles already in DB");

                // 2. Build expected sequence
                var expected = GenerateExpectedOpenTimes(interval, from, to);

                // 3. Identify missing timestamps
                var missing = expected
                    .Where(t => !existing.Contains(t))
                    .OrderBy(t => t)
                    .ToList();

                if (!missing.Any())
                {
                    _logger.LogInformation(
                        $"[GapCheck] {symbol} {interval} | No gaps found — DB is complete.");
                    return;
                }

                _logger.LogInformation(
                    $"[GapCheck] {symbol} {interval} | {missing.Count} missing candles across the range");

                // 4. Group consecutive missing timestamps into download ranges
                var ranges = GroupIntoRanges(missing, interval);
                _logger.LogInformation(
                    $"[GapCheck] {symbol} {interval} | {ranges.Count} gap range(s) to download");

                // 5. Download and persist each range
                foreach (var (rangeStart, rangeEnd) in ranges)
                {
                    await DownloadAndSaveRangeAsync(symbol, interval, rangeStart, rangeEnd, existing);
                }

                _logger.LogInformation(
                    $"[GapCheck] {symbol} {interval} | Gap-fill complete.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"[GapCheck] Error filling missing candles for {symbol} {interval}");
            }
        }

        // -------------------------------------------------------------------------
        // Download a single contiguous gap range, batching at BinanceMaxLimit
        // -------------------------------------------------------------------------

        private async Task DownloadAndSaveRangeAsync(
            string symbol,
            CandleInterval interval,
            DateTime rangeStart,
            DateTime rangeEnd,
            HashSet<DateTime> alreadyExisting)
        {
            var intervalSpan = GetIntervalSpan(interval);
            // Include the last candle in the range by advancing the API end time by one interval
            var apiEnd = rangeEnd + intervalSpan;

            _logger.LogInformation(
                $"[Download] {symbol} {interval} | " +
                $"{rangeStart:yyyy-MM-dd HH:mm} – {rangeEnd:yyyy-MM-dd HH:mm}");

            var current = rangeStart;

            while (current <= rangeEnd)
            {
                // Cap the batch end so we never request more than BinanceMaxLimit candles
                var batchEnd = current + TimeSpan.FromTicks(intervalSpan.Ticks * BinanceMaxLimit);
                if (batchEnd > apiEnd) batchEnd = apiEnd;

                _logger.LogDebug(
                    $"[Download] {symbol} {interval} | " +
                    $"Batch {current:yyyy-MM-dd HH:mm} – {batchEnd:yyyy-MM-dd HH:mm}");

                IEnumerable<ExchangeCandlestick> candles;
                await _providerSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    candles = await _provider.GetCandlesticksAsync(
                        symbol, interval,
                        current.ToUniversalTime(), batchEnd.ToUniversalTime())
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        $"[Download] API error for {symbol} {interval} at {current:yyyy-MM-dd HH:mm}");
                    break;
                }
                finally
                {
                    _providerSemaphore.Release();
                }

                var candleList = candles?.ToList() ?? new List<ExchangeCandlestick>();

                if (!candleList.Any())
                {
                    _logger.LogWarning(
                        $"[Download] No candles returned for {symbol} {interval} starting {current:yyyy-MM-dd HH:mm}");
                    break;
                }

                // Filter out any that are already in the DB to prevent duplicate rows
                var toInsert = candleList
                    .Where(c => !alreadyExisting.Contains(c.OpenTime))
                    .Select(c => new CandleStickDb(c))
                    .ToList();

                if (toInsert.Any())
                {
                    using var ctx = new CryptoDbContext();
                    ctx.BulkInsert(toInsert);

                    // Track what we just inserted so subsequent batches don't re-insert them
                    foreach (var inserted in toInsert)
                        alreadyExisting.Add(inserted.OpenTime);

                    _logger.LogInformation(
                        $"[Download] Inserted {toInsert.Count} candles for {symbol} {interval} " +
                        $"starting {current:yyyy-MM-dd HH:mm}");
                }

                // Advance past the last returned candle
                current = candleList.Last().OpenTime + intervalSpan;
            }
        }

        // -------------------------------------------------------------------------
        // Expected timestamp generation
        // -------------------------------------------------------------------------

        /// <summary>
        /// Generates all expected OpenTime values for the given interval in [from, to).
        /// </summary>
        private List<DateTime> GenerateExpectedOpenTimes(
            CandleInterval interval, DateTime from, DateTime to)
        {
            var result = new List<DateTime>();
            var step = GetIntervalSpan(interval);
            var current = from;

            while (current < to)
            {
                result.Add(current);
                current = AdvanceByInterval(current, interval, step);
            }

            return result;
        }

        /// <summary>
        /// Advances a DateTime by one interval. Uses calendar arithmetic for Month.
        /// </summary>
        private static DateTime AdvanceByInterval(DateTime dt, CandleInterval interval, TimeSpan step)
        {
            return interval == CandleInterval.Month_1
                ? dt.AddMonths(1)
                : dt + step;
        }

        // -------------------------------------------------------------------------
        // Gap grouping
        // -------------------------------------------------------------------------

        /// <summary>
        /// Groups a sorted list of missing timestamps into contiguous (Start, End) ranges
        /// so that each range can be fetched with a minimal number of API calls.
        /// </summary>
        private List<(DateTime Start, DateTime End)> GroupIntoRanges(
            List<DateTime> missingTimes, CandleInterval interval)
        {
            var ranges = new List<(DateTime, DateTime)>();
            if (!missingTimes.Any()) return ranges;

            var step = GetIntervalSpan(interval);
            var rangeStart = missingTimes[0];
            var rangeEnd   = missingTimes[0];

            for (int i = 1; i < missingTimes.Count; i++)
            {
                var nextExpected = AdvanceByInterval(rangeEnd, interval, step);

                // Allow a small tolerance (half the interval) for DST / rounding
                if (missingTimes[i] <= nextExpected + TimeSpan.FromMinutes(1))
                {
                    rangeEnd = missingTimes[i]; // extend the current range
                }
                else
                {
                    ranges.Add((rangeStart, rangeEnd));
                    rangeStart = missingTimes[i];
                    rangeEnd   = missingTimes[i];
                }
            }

            ranges.Add((rangeStart, rangeEnd));
            return ranges;
        }

        // -------------------------------------------------------------------------
        // Interval helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Returns the fixed-width TimeSpan for an interval.
        /// Month returns 30 days as an approximation (calendar months are handled
        /// separately in AdvanceByInterval).
        /// </summary>
        private static TimeSpan GetIntervalSpan(CandleInterval interval) => interval switch
        {
            CandleInterval.Minute_1   => TimeSpan.FromMinutes(1),
            CandleInterval.Minute_3  => TimeSpan.FromMinutes(3),
            CandleInterval.Minute_5  => TimeSpan.FromMinutes(5),
            CandleInterval.Minute_15 => TimeSpan.FromMinutes(15),
            CandleInterval.Minute_30 => TimeSpan.FromMinutes(30),
            CandleInterval.Hour_1    => TimeSpan.FromHours(1),
            CandleInterval.Hour_2    => TimeSpan.FromHours(2),
            CandleInterval.Hour_4    => TimeSpan.FromHours(4),
            CandleInterval.Hour_6    => TimeSpan.FromHours(6),
            CandleInterval.Hour_8    => TimeSpan.FromHours(8),
            CandleInterval.Hour_12   => TimeSpan.FromHours(12),
            CandleInterval.Day_1     => TimeSpan.FromDays(1),
            CandleInterval.Day_3    => TimeSpan.FromDays(3),
            CandleInterval.Week_1    => TimeSpan.FromDays(7),
            CandleInterval.Month_1   => TimeSpan.FromDays(30), // approximate; calendar logic in AdvanceByInterval
            _                             => TimeSpan.FromMinutes(1)
        };
    }
}
