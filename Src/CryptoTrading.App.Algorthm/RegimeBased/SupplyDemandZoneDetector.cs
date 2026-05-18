using CryptoTrading.App.Core.Strategy;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.RegimeBased
{
    /// <summary>
    /// Lightweight supply/demand zone detector that works directly with IQuote data.
    ///
    /// Detection algorithm:
    /// 1. Scan for "base candles" - candles with a small body relative to their range (consolidation)
    /// 2. Check if the next candle is a strong "departure" that moves away sharply
    /// 3. Bullish departure from base → Demand zone (buying pressure area)
    /// 4. Bearish departure from base → Supply zone (selling pressure area)
    /// 5. Track freshness and touch count; prune broken zones
    /// </summary>
    public class SupplyDemandZoneDetector
    {
        private readonly int _lookbackPeriods;
        private readonly decimal _bodyToRangeThreshold;
        private readonly decimal _departureMultiple;
        private readonly int _maxZones;
        private readonly int _maxTouches;

        private List<SupplyDemandZone> _zones = new();

        public SupplyDemandZoneDetector(
            int lookbackPeriods = 50,
            decimal bodyToRangeThreshold = 0.4m,
            decimal departureMultiple = 1.5m,
            int maxZones = 6,
            int maxTouches = 3)
        {
            _lookbackPeriods = lookbackPeriods;
            _bodyToRangeThreshold = bodyToRangeThreshold;
            _departureMultiple = departureMultiple;
            _maxZones = maxZones;
            _maxTouches = maxTouches;
        }

        /// <summary>
        /// Detect and update zones from quotes (typically 4H candles).
        /// Called on each candle close. Returns the currently active zones.
        /// </summary>
        public List<SupplyDemandZone> Detect(IReadOnlyList<IQuote> quotes)
        {
            if (quotes == null || quotes.Count < _lookbackPeriods)
                return _zones;

            var recentQuotes = quotes.TakeLast(_lookbackPeriods).ToList();
            var currentPrice = (decimal)recentQuotes.Last().Close;

            // Calculate average body size for departure threshold
            decimal avgBody = recentQuotes
                .Select(q => Math.Abs(q.Close - q.Open))
                .Average();

            // Scan for base + departure patterns
            for (int i = 0; i < recentQuotes.Count - 1; i++)
            {
                var baseCandle = recentQuotes[i];
                var departureCandle = recentQuotes[i + 1];

                decimal baseBody = Math.Abs(baseCandle.Close - baseCandle.Open);
                decimal baseRange = baseCandle.High - baseCandle.Low;

                if (baseRange == 0) continue;

                decimal bodyRatio = (decimal)(baseBody / baseRange);

                // Is this a base candle? (small body relative to range = consolidation)
                if (bodyRatio >= _bodyToRangeThreshold) continue;

                decimal departureBody = Math.Abs(departureCandle.Close - departureCandle.Open);

                // Is the departure candle strong enough?
                if ((decimal)departureBody <= (decimal)avgBody * _departureMultiple) continue;

                // Bullish departure → Demand zone
                if (departureCandle.Close > departureCandle.Open)
                {
                    var zoneHigh = Math.Max(baseCandle.Open, baseCandle.Close);
                    var zoneLow = baseCandle.Low;

                    if (!ZoneOverlaps((decimal)zoneLow, (decimal)zoneHigh))
                    {
                        _zones.Add(new SupplyDemandZone
                        {
                            Type = ZoneType.Demand,
                            High = (decimal)zoneHigh,
                            Low = (decimal)zoneLow,
                            IsFresh = true,
                            Strength = 1,
                            CreatedAt = baseCandle.Timestamp
                        });
                    }
                }
                // Bearish departure → Supply zone
                else if (departureCandle.Close < departureCandle.Open)
                {
                    var zoneHigh = baseCandle.High;
                    var zoneLow = Math.Min(baseCandle.Open, baseCandle.Close);

                    if (!ZoneOverlaps((decimal)zoneLow, (decimal)zoneHigh))
                    {
                        _zones.Add(new SupplyDemandZone
                        {
                            Type = ZoneType.Supply,
                            High = (decimal)zoneHigh,
                            Low = (decimal)zoneLow,
                            IsFresh = true,
                            Strength = 1,
                            CreatedAt = baseCandle.Timestamp
                        });
                    }
                }
            }

            // Update existing zones: check if price has returned (freshness & touches)
            UpdateZoneFreshness(currentPrice);

            // Prune broken zones (too many touches)
            _zones.RemoveAll(z => z.Strength > _maxTouches);

            // Keep only the nearest zones, prioritising fresh ones
            PruneToMax(currentPrice);

            return _zones;
        }

        /// <summary>
        /// Get the nearest zone to a given price, optionally filtered by type.
        /// </summary>
        public SupplyDemandZone GetNearestZone(decimal price, ZoneType? typeFilter = null)
        {
            var candidates = typeFilter.HasValue
                ? _zones.Where(z => z.Type == typeFilter.Value)
                : _zones.AsEnumerable();

            return candidates
                .OrderBy(z => z.DistanceTo(price))
                .FirstOrDefault();
        }

        /// <summary>
        /// Check if a price is within any active zone.
        /// </summary>
        public bool IsInZone(decimal price, out SupplyDemandZone zone)
        {
            zone = _zones.FirstOrDefault(z => z.Contains(price));
            return zone != null;
        }

        private bool ZoneOverlaps(decimal low, decimal high)
        {
            return _zones.Any(z =>
                (low >= z.Low && low <= z.High) ||
                (high >= z.Low && high <= z.High) ||
                (low <= z.Low && high >= z.High));
        }

        private void UpdateZoneFreshness(decimal currentPrice)
        {
            foreach (var zone in _zones)
            {
                if (zone.Contains(currentPrice) && zone.IsFresh)
                {
                    zone.IsFresh = false;
                    zone.Strength++;
                }
                else if (zone.Contains(currentPrice) && !zone.IsFresh)
                {
                    zone.Strength++;
                }
            }
        }

        private void PruneToMax(decimal currentPrice)
        {
            if (_zones.Count <= _maxZones) return;

            // Sort: fresh first, then by proximity to current price
            _zones = _zones
                .OrderByDescending(z => z.IsFresh)
                .ThenBy(z => z.DistanceTo(currentPrice))
                .Take(_maxZones)
                .ToList();
        }
    }
}
