using Binance;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoTrading.App.DatabaseLoad
{
    /// <summary>
    /// Alternative gap-filling processing path that separates the three phases cleanly:
    ///
    ///   FETCH  — all missing ranges for a (symbol, interval) are requested from Binance
    ///            concurrently and the raw candles are held in memory.
    ///
    ///   COLLATE / DEDUPLICATE — the in-memory candles are merged into one collection,
    ///            any within-batch duplicate OpenTimes are collapsed, and candles whose
    ///            OpenTime is already present in the database are discarded.
    ///
    ///   UPLOAD  — the clean, deduplicated set is written to the DB in a single bulk insert.
    ///
    /// Contrast with <see cref="MissingCandleDetector"/>, which inserts each downloaded
    /// batch immediately and sequentially within a gap range.  This class trades higher
    /// peak memory usage for fewer, larger write operations.
    /// </summary>
    public class CandleGapFiller
    {
        private readonly IBinanceApi _api;
        private readonly ILogger<CandleGapFiller> _logger;
        private readonly SemaphoreSlim _apiSemaphore;
        private const int BinanceMaxLimit = 1000;

        /// <param name="maxConcurrency">
        /// Maximum simultaneous Binance API calls across all in-flight (symbol, interval)
        /// tasks. Binance permits ~1 200 requests/min; 10 is a conservative default.
        /// </param>
        public CandleGapFiller(
            IBinanceApi api,
            ILogger<CandleGapFiller> logger,
            int maxConcurrency = 10)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Checks every (symbol, interval) combination and uploads any missing candles.
        /// All symbols are processed concurrently; within each symbol all intervals run
        /// concurrently; Binance API calls are gated by the shared semaphore.
        /// </summary>
        public Task FillAsync(
            IEnumerable<string> symbols,
            IEnumerable<CandlestickInterval> intervals,
            DateTime from,
            DateTime to)
        {
            var intervalList = intervals.ToList();
            var tasks = symbols.Select(symbol =>
                FillForSymbolAsync(symbol, intervalList, from, to));
            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Runs the fetch → collate → deduplicate → upload pipeline for every interval
        /// of a single symbol.
        /// </summary>
        public Task FillForSymbolAsync(
            string symbol,
            IEnumerable<CandlestickInterval> intervals,
            DateTime from,
            DateTime to)
        {
            var tasks = intervals.Select(interval =>
                FillForSymbolIntervalAsync(symbol, interval, from, to));
            return Task.WhenAll(tasks);
        }

        // -------------------------------------------------------------------------
        // Core per-(symbol, interval) pipeline
        // -------------------------------------------------------------------------

        private async Task FillForSymbolIntervalAsync(
            string symbol,
            CandlestickInterval interval,
            DateTime from,
            DateTime to)
        {
            try
            {
                _logger.LogInformation(
                    $"[GapFill] {symbol} {interval} | Scanning DB for gaps " +
                    $"{from:yyyy-MM-dd HH:mm} – {to:yyyy-MM-dd HH:mm}");

                // ── PHASE 1: determine what is already in the DB ──────────────────
                // PR 5f: CandleStickDb.Interval is now neutral CandleInterval; bridge once.
                var neutralInterval = (CandleInterval)(int)interval;
                HashSet<DateTime> existing;
                using (var ctx = new CryptoDbContext())
                {
                    existing = ctx.CandleSticks
                        .Where(c => c.Symbol   == symbol
                                 && c.Interval == neutralInterval
                                 && c.OpenTime >= from
                                 && c.OpenTime <  to)
                        .Select(c => c.OpenTime)
                        .ToHashSet();
                }

                _logger.LogInformation(
                    $"[GapFill] {symbol} {interval} | {existing.Count} candles already in DB");

                // ── PHASE 2: compute the expected sequence and find missing times ──
                var expected = GenerateExpectedOpenTimes(interval, from, to);

                var missing = expected
                    .Where(t => !existing.Contains(t))
                    .OrderBy(t => t)
                    .ToList();

                if (!missing.Any())
                {
                    _logger.LogInformation(
                        $"[GapFill] {symbol} {interval} | DB is complete — nothing to do.");
                    return;
                }

                _logger.LogInformation(
                    $"[GapFill] {symbol} {interval} | {missing.Count} missing candle(s) detected");

                // Group consecutive missing timestamps into contiguous ranges so the
                // number of API calls is kept minimal.
                var ranges = GroupIntoRanges(missing, interval);

                _logger.LogInformation(
                    $"[GapFill] {symbol} {interval} | {ranges.Count} gap range(s) to fetch");

                // ── PHASE 3: FETCH — all ranges in parallel, gated by semaphore ────
                var fetchTasks = ranges
                    .Select(r => FetchRangeAsync(symbol, interval, r.Start, r.End))
                    .ToList();

                var fetchedBatches = await Task.WhenAll(fetchTasks).ConfigureAwait(false);

                // ── PHASE 4: COLLATE & DEDUPLICATE ────────────────────────────────
                //
                // a) Flatten all batches into one sequence.
                // b) Collapse any duplicate OpenTimes that appeared across overlapping
                //    API windows (keep first occurrence).
                // c) Discard any candle whose OpenTime is already in the database.
                var toInsert = fetchedBatches
                    .SelectMany(batch => batch)
                    .GroupBy(c => c.OpenTime)          // (b) intra-fetch deduplication
                    .Select(g => g.First())
                    .Where(c => !existing.Contains(c.OpenTime))  // (c) vs DB
                    .OrderBy(c => c.OpenTime)
                    .Select(c => new CandleStickDb(c))
                    .ToList();

                if (!toInsert.Any())
                {
                    _logger.LogInformation(
                        $"[GapFill] {symbol} {interval} | " +
                        $"No new candles remain after deduplication — nothing uploaded.");
                    return;
                }

                _logger.LogInformation(
                    $"[GapFill] {symbol} {interval} | " +
                    $"Collated {toInsert.Count} unique candle(s) ready for upload");

                // ── PHASE 5: UPLOAD — single bulk insert per (symbol, interval) ───
                using (var ctx = new CryptoDbContext())
                {
                    ctx.BulkInsert(toInsert);
                }

                _logger.LogInformation(
                    $"[GapFill] {symbol} {interval} | Uploaded {toInsert.Count} candle(s). Done.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"[GapFill] Unhandled error filling gaps for {symbol} {interval}");
            }
        }

        // -------------------------------------------------------------------------
        // Fetch a single contiguous gap range from Binance
        // Paginates internally at BinanceMaxLimit candles per request.
        // Returns the raw candles; deduplication happens in the caller.
        // -------------------------------------------------------------------------

        private async Task<List<Candlestick>> FetchRangeAsync(
            string symbol,
            CandlestickInterval interval,
            DateTime rangeStart,
            DateTime rangeEnd)
        {
            var intervalSpan = GetIntervalSpan(interval);
            // Advance the API end-time by one interval so the last candle is included.
            var apiEnd  = rangeEnd + intervalSpan;
            var current = rangeStart;
            var results = new List<Candlestick>();

            _logger.LogDebug(
                $"[GapFill] {symbol} {interval} | Fetching range " +
                $"{rangeStart:yyyy-MM-dd HH:mm} – {rangeEnd:yyyy-MM-dd HH:mm}");

            while (current <= rangeEnd)
            {
                // Cap each HTTP request to BinanceMaxLimit candles.
                var batchEnd = current + TimeSpan.FromTicks(intervalSpan.Ticks * BinanceMaxLimit);
                if (batchEnd > apiEnd) batchEnd = apiEnd;

                IEnumerable<Candlestick> candles;

                await _apiSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    candles = await _api.GetCandlesticksAsync(
                        symbol, interval, BinanceMaxLimit,
                        current.ToUniversalTime(), batchEnd.ToUniversalTime())
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        $"[GapFill] API error for {symbol} {interval} " +
                        $"at {current:yyyy-MM-dd HH:mm} — range fetch aborted");
                    break;
                }
                finally
                {
                    _apiSemaphore.Release();
                }

                var batch = candles?.ToList() ?? new List<Candlestick>();

                if (!batch.Any())
                {
                    _logger.LogWarning(
                        $"[GapFill] Empty response for {symbol} {interval} " +
                        $"at {current:yyyy-MM-dd HH:mm} — stopping range fetch");
                    break;
                }

                results.AddRange(batch);

                // Advance past the last candle returned by this request.
                current = batch.Last().OpenTime + intervalSpan;
            }

            return results;
        }

        // -------------------------------------------------------------------------
        // Expected timestamp generation
        // -------------------------------------------------------------------------

        private List<DateTime> GenerateExpectedOpenTimes(
            CandlestickInterval interval, DateTime from, DateTime to)
        {
            var result  = new List<DateTime>();
            var step    = GetIntervalSpan(interval);
            var current = from;

            while (current < to)
            {
                result.Add(current);
                current = AdvanceByInterval(current, interval, step);
            }

            return result;
        }

        private static DateTime AdvanceByInterval(
            DateTime dt, CandlestickInterval interval, TimeSpan step)
        {
            return interval == CandlestickInterval.Month
                ? dt.AddMonths(1)
                : dt + step;
        }

        // -------------------------------------------------------------------------
        // Gap grouping — consecutive missing timestamps → (Start, End) ranges
        // -------------------------------------------------------------------------

        private List<(DateTime Start, DateTime End)> GroupIntoRanges(
            List<DateTime> missingTimes, CandlestickInterval interval)
        {
            var ranges = new List<(DateTime, DateTime)>();
            if (!missingTimes.Any()) return ranges;

            var step       = GetIntervalSpan(interval);
            var rangeStart = missingTimes[0];
            var rangeEnd   = missingTimes[0];

            for (int i = 1; i < missingTimes.Count; i++)
            {
                var nextExpected = AdvanceByInterval(rangeEnd, interval, step);

                // Allow a small tolerance for DST / sub-millisecond rounding.
                if (missingTimes[i] <= nextExpected + TimeSpan.FromMinutes(1))
                {
                    rangeEnd = missingTimes[i];  // extend the current range
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

        private static TimeSpan GetIntervalSpan(CandlestickInterval interval) => interval switch
        {
            CandlestickInterval.Minute     => TimeSpan.FromMinutes(1),
            CandlestickInterval.Minutes_3  => TimeSpan.FromMinutes(3),
            CandlestickInterval.Minutes_5  => TimeSpan.FromMinutes(5),
            CandlestickInterval.Minutes_15 => TimeSpan.FromMinutes(15),
            CandlestickInterval.Minutes_30 => TimeSpan.FromMinutes(30),
            CandlestickInterval.Hour       => TimeSpan.FromHours(1),
            CandlestickInterval.Hours_2    => TimeSpan.FromHours(2),
            CandlestickInterval.Hours_4    => TimeSpan.FromHours(4),
            CandlestickInterval.Hours_6    => TimeSpan.FromHours(6),
            CandlestickInterval.Hours_8    => TimeSpan.FromHours(8),
            CandlestickInterval.Hours_12   => TimeSpan.FromHours(12),
            CandlestickInterval.Day        => TimeSpan.FromDays(1),
            CandlestickInterval.Days_3     => TimeSpan.FromDays(3),
            CandlestickInterval.Week       => TimeSpan.FromDays(7),
            CandlestickInterval.Month      => TimeSpan.FromDays(30), // approx; calendar logic in AdvanceByInterval
            _                              => TimeSpan.FromMinutes(1)
        };
    }
}
