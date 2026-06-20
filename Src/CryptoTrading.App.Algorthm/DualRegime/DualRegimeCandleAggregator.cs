using CryptoTrading.App.Algorithm.DualRegime.Persistence;
using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.DualRegime
{
    public sealed class DualRegimeCandleAggregator
    {
        private readonly DualRegimeModelArtifact _artifact;
        private readonly FeatureEngine _featureEngine;

        // Ring buffers
        private readonly List<Bar15M> _bars15M = new();
        private readonly List<AggBar> _bars1H = new();
        private readonly List<AggBar> _bars4H = new();

        // Phase-shifted accumulators for majority vote
        private readonly PhaseAccumulator[] _phases1H = new PhaseAccumulator[4];
        private readonly PhaseAccumulator[] _phases4H = new PhaseAccumulator[4];

        private int _bar15MCount;

        // Most-recently completed 4H regime (3-class). Assigned causally to each
        // new 1H bar at creation time. -1 until the first 4H window completes.
        private int _lastCompleted4HRegime = -1;

        // Keep a manageable window
        private const int Max15MBars = 400;
        private const int Max1HBars = 200;
        private const int Max4HBars = 100;

        public IReadOnlyList<AggBar> Bars1H => _bars1H;
        public IReadOnlyList<AggBar> Bars4H => _bars4H;
        public int Count1H => _bars1H.Count;
        public bool Ready => _bars1H.Count >= 30 && _bars4H.Count >= 5;

        public DualRegimeCandleAggregator(DualRegimeModelArtifact artifact)
        {
            _artifact = artifact;
            _featureEngine = new FeatureEngine(artifact);

            // 1H phases: offsets 0,1,2,3 in 15-min units, group size 4
            for (int i = 0; i < 4; i++)
                _phases1H[i] = new PhaseAccumulator(4, i, artifact.Sigma1H);

            // 4H phases: offsets 0,4,8,12 in 15-min units, group size 16
            for (int i = 0; i < 4; i++)
                _phases4H[i] = new PhaseAccumulator(16, i * 4, artifact.Sigma4H);
        }

        public AggBar AddBar15M(ExchangeCandlestick candle)
        {
            double ret = 0;
            if (_bars15M.Count > 0)
            {
                var prev = _bars15M[^1];
                if (prev.Close > 0)
                    ret = Math.Log((double)candle.Close / (double)prev.Close);
            }

            int class15M = RegimeClassifier.Classify5Class(ret, _artifact.Sigma15M);

            var bar = new Bar15M
            {
                Timestamp = candle.CloseTime,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = candle.Volume,
                Ret = ret,
                Class15M = class15M,
            };
            _bars15M.Add(bar);
            if (_bars15M.Count > Max15MBars)
                _bars15M.RemoveAt(0);

            _bar15MCount++;

            // Feed each phase accumulator; emit 1H bar when ALL 4 phases have a new bar
            bool[] completed1H = new bool[4];
            int[] classes1H = new int[4];
            AggBar baseBar1H = null;

            for (int p = 0; p < 4; p++)
            {
                var result = _phases1H[p].Add(bar, _bar15MCount - 1);
                if (result != null)
                {
                    completed1H[p] = true;
                    classes1H[p] = result.ClassCombined;
                    if (p == 0) baseBar1H = result;
                }
            }

            // Only emit a combined 1H bar when phase-0 completes
            AggBar newBar1H = null;
            if (completed1H[0] && baseBar1H != null)
            {
                // Majority vote: use the most recently completed class from each phase
                var voteClasses = new int[4];
                for (int p = 0; p < 4; p++)
                    voteClasses[p] = completed1H[p] ? classes1H[p] : _phases1H[p].LastClass;

                baseBar1H.ClassCombined = RegimeClassifier.MajorityVote(voteClasses);

                // Attach 15m sub-bar data
                AttachSubBarData(baseBar1H);

                // Volume normalization
                _featureEngine.NormalizeVolume(baseBar1H);

                // Compute ret relative to previous 1H bar
                if (_bars1H.Count > 0)
                {
                    var prev1H = _bars1H[^1];
                    if (prev1H.Close > 0)
                        baseBar1H.Ret = Math.Log((double)baseBar1H.Close / (double)prev1H.Close);
                }

                // Causal 4H regime context: assign the most-recently completed 4H
                // regime. (The 4H window containing this 1H bar has NOT closed yet,
                // so we use the last one that has — the only choice available live.)
                baseBar1H.Regime4H3Class = _lastCompleted4HRegime;

                _bars1H.Add(baseBar1H);
                if (_bars1H.Count > Max1HBars)
                    _bars1H.RemoveAt(0);

                newBar1H = baseBar1H;
            }

            // 4H aggregation
            for (int p = 0; p < 4; p++)
            {
                var result = _phases4H[p].Add(bar, _bar15MCount - 1);
                if (result != null && p == 0)
                {
                    // Get majority vote for 4H
                    var vote4H = new int[4];
                    for (int q = 0; q < 4; q++)
                        vote4H[q] = _phases4H[q].LastClass;

                    int class4H5 = RegimeClassifier.MajorityVote(vote4H);
                    result.ClassCombined = class4H5;
                    result.Regime4H3Class = RegimeClassifier.Collapse4H(class4H5);

                    if (_bars4H.Count > 0)
                    {
                        var prev4H = _bars4H[^1];
                        if (prev4H.Close > 0)
                            result.Ret = Math.Log((double)result.Close / (double)prev4H.Close);
                    }

                    _bars4H.Add(result);
                    if (_bars4H.Count > Max4HBars)
                        _bars4H.RemoveAt(0);

                    // Publish this regime so subsequent 1H bars pick it up causally.
                    _lastCompleted4HRegime = result.Regime4H3Class;
                }
            }

            return newBar1H;
        }

        private void AttachSubBarData(AggBar bar1H)
        {
            // Last 4 15M bars form this 1H bar
            if (_bars15M.Count < 4) return;
            var subBars = _bars15M.Skip(_bars15M.Count - 4).Take(4).ToArray();
            bar1H.SubClasses = subBars.Select(b => b.Class15M).ToArray();
            bar1H.SubRets = subBars.Select(b => b.Ret).ToArray();
        }

        public float[] GetLatestFeatures()
        {
            if (_bars1H.Count < 25) return null;
            return _featureEngine.ComputeFeatures(_bars1H, _bars1H.Count - 1);
        }

        public AggBar GetLatest1H() => _bars1H.Count > 0 ? _bars1H[^1] : null;
        public AggBar GetLatest4H() => _bars4H.Count > 0 ? _bars4H[^1] : null;

        public double GetRollingSigma24()
        {
            if (_bars1H.Count < 24) return 0;
            var rets = _bars1H.Skip(_bars1H.Count - 24).Select(b => b.Ret).ToList();
            double mean = rets.Average();
            double sumSq = rets.Sum(r => (r - mean) * (r - mean));
            return Math.Sqrt(sumSq / (rets.Count - 1));
        }

        // ── Snapshot / Restore ──

        public AggregatorSnapshot ToSnapshot()
        {
            var snap = new AggregatorSnapshot
            {
                Bar15MCount = _bar15MCount,
                LastCompleted4HRegime = _lastCompleted4HRegime,
            };

            foreach (var b in _bars15M)
                snap.Bars15M.Add(new BarSnapshot
                {
                    Timestamp = b.Timestamp, Open = b.Open, High = b.High, Low = b.Low,
                    Close = b.Close, Volume = b.Volume, Ret = b.Ret, Class15M = b.Class15M
                });

            foreach (var b in _bars1H)
                snap.Bars1H.Add(new AggBarSnapshot
                {
                    Timestamp = b.Timestamp, Open = b.Open, High = b.High, Low = b.Low,
                    Close = b.Close, Volume = b.Volume, Ret = b.Ret,
                    ClassCombined = b.ClassCombined, Regime4H3Class = b.Regime4H3Class,
                    SubClasses = b.SubClasses, SubRets = b.SubRets
                });

            foreach (var b in _bars4H)
                snap.Bars4H.Add(new AggBarSnapshot
                {
                    Timestamp = b.Timestamp, Open = b.Open, High = b.High, Low = b.Low,
                    Close = b.Close, Volume = b.Volume, Ret = b.Ret,
                    ClassCombined = b.ClassCombined, Regime4H3Class = b.Regime4H3Class,
                    SubClasses = b.SubClasses, SubRets = b.SubRets
                });

            for (int i = 0; i < 4; i++)
            {
                snap.Phases1H.Add(SnapshotPhase(_phases1H[i]));
                snap.Phases4H.Add(SnapshotPhase(_phases4H[i]));
            }

            return snap;
        }

        public void RestoreFromSnapshot(AggregatorSnapshot snap)
        {
            _bars15M.Clear();
            foreach (var b in snap.Bars15M)
                _bars15M.Add(new Bar15M
                {
                    Timestamp = b.Timestamp, Open = b.Open, High = b.High, Low = b.Low,
                    Close = b.Close, Volume = b.Volume, Ret = b.Ret, Class15M = b.Class15M
                });

            _bars1H.Clear();
            foreach (var b in snap.Bars1H)
                _bars1H.Add(new AggBar
                {
                    Timestamp = b.Timestamp, Open = b.Open, High = b.High, Low = b.Low,
                    Close = b.Close, Volume = b.Volume, Ret = b.Ret,
                    ClassCombined = b.ClassCombined, Regime4H3Class = b.Regime4H3Class,
                    SubClasses = b.SubClasses, SubRets = b.SubRets
                });

            _bars4H.Clear();
            foreach (var b in snap.Bars4H)
                _bars4H.Add(new AggBar
                {
                    Timestamp = b.Timestamp, Open = b.Open, High = b.High, Low = b.Low,
                    Close = b.Close, Volume = b.Volume, Ret = b.Ret,
                    ClassCombined = b.ClassCombined, Regime4H3Class = b.Regime4H3Class,
                    SubClasses = b.SubClasses, SubRets = b.SubRets
                });

            _bar15MCount = snap.Bar15MCount;
            _lastCompleted4HRegime = snap.LastCompleted4HRegime;

            for (int i = 0; i < 4 && i < snap.Phases1H.Count; i++)
                RestorePhase(_phases1H[i], snap.Phases1H[i]);

            for (int i = 0; i < 4 && i < snap.Phases4H.Count; i++)
                RestorePhase(_phases4H[i], snap.Phases4H[i]);
        }

        private static PhaseAccumulatorSnapshot SnapshotPhase(PhaseAccumulator pa)
        {
            return new PhaseAccumulatorSnapshot
            {
                GroupSize = pa.GroupSize,
                Offset = pa.Offset,
                Sigma = pa.SigmaValue,
                Count = pa.CountValue,
                LastClass = pa.LastClass,
                Timestamp = pa.TimestampValue,
                Open = pa.OpenValue,
                High = pa.HighValue,
                Low = pa.LowValue,
                Close = pa.CloseValue,
                Volume = pa.VolumeValue,
                RetSum = pa.RetSumValue
            };
        }

        private static void RestorePhase(PhaseAccumulator pa, PhaseAccumulatorSnapshot snap)
        {
            pa.RestoreFrom(snap.Count, snap.LastClass, snap.Timestamp,
                snap.Open, snap.High, snap.Low, snap.Close, snap.Volume, snap.RetSum);
        }

        // ── Inner types ──

        private sealed class Bar15M
        {
            public DateTime Timestamp { get; set; }
            public decimal Open { get; set; }
            public decimal High { get; set; }
            public decimal Low { get; set; }
            public decimal Close { get; set; }
            public decimal Volume { get; set; }
            public double Ret { get; set; }
            public int Class15M { get; set; }
        }

        private sealed class PhaseAccumulator
        {
            private readonly int _groupSize;
            private readonly int _offset;
            private readonly double _sigma;
            private int _count;
            public int LastClass { get; private set; } = 2;

            private decimal _open, _high, _low, _close, _volume;
            private double _retSum;
            private DateTime _timestamp;

            public int GroupSize => _groupSize;
            public int Offset => _offset;
            public double SigmaValue => _sigma;
            public int CountValue => _count;
            public DateTime TimestampValue => _timestamp;
            public decimal OpenValue => _open;
            public decimal HighValue => _high;
            public decimal LowValue => _low;
            public decimal CloseValue => _close;
            public decimal VolumeValue => _volume;
            public double RetSumValue => _retSum;

            public PhaseAccumulator(int groupSize, int offset, double sigma)
            {
                _groupSize = groupSize;
                _offset = offset;
                _sigma = sigma;
            }

            public void RestoreFrom(int count, int lastClass, DateTime timestamp,
                decimal open, decimal high, decimal low, decimal close, decimal volume, double retSum)
            {
                _count = count;
                LastClass = lastClass;
                _timestamp = timestamp;
                _open = open;
                _high = high;
                _low = low;
                _close = close;
                _volume = volume;
                _retSum = retSum;
            }

            public AggBar Add(Bar15M bar, int globalIndex)
            {
                // Skip bars before offset alignment
                if (globalIndex < _offset) return null;

                int posInGroup = (globalIndex - _offset) % _groupSize;

                if (posInGroup == 0)
                {
                    _timestamp = bar.Timestamp;
                    _open = bar.Open;
                    _high = bar.High;
                    _low = bar.Low;
                    _close = bar.Close;
                    _volume = bar.Volume;
                    _retSum = bar.Ret;
                }
                else
                {
                    if (bar.High > _high) _high = bar.High;
                    if (bar.Low < _low) _low = bar.Low;
                    _close = bar.Close;
                    _volume += bar.Volume;
                    _retSum += bar.Ret;
                }
                _count++;

                if (posInGroup == _groupSize - 1)
                {
                    int cls = RegimeClassifier.Classify5Class(_retSum, _sigma);
                    LastClass = cls;

                    return new AggBar
                    {
                        Timestamp = _timestamp,
                        Open = _open,
                        High = _high,
                        Low = _low,
                        Close = _close,
                        Volume = _volume,
                        Ret = _retSum,
                        ClassCombined = cls,
                    };
                }

                return null;
            }
        }
    }
}
