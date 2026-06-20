using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.DualRegime
{
    public sealed class AggBar
    {
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public double Ret { get; set; }
        public int ClassCombined { get; set; }
        public int Regime4H3Class { get; set; } = -1;

        // Volume normalization
        public double LogVol { get; set; }
        public double VolZAdj { get; set; }
        public int VolBucketAdj { get; set; }

        // 15m sub-bar data
        public int[] SubClasses { get; set; }
        public double[] SubRets { get; set; }
    }

    public sealed class FeatureEngine
    {
        private readonly DualRegimeModelArtifact _artifact;

        public FeatureEngine(DualRegimeModelArtifact artifact)
        {
            _artifact = artifact;
        }

        // Compute volume normalization for a bar
        public void NormalizeVolume(AggBar bar)
        {
            bar.LogVol = Math.Log((double)bar.Volume + 1e-9);
            int hour = bar.Timestamp.Hour;
            // pandas dayofweek: Mon=0..Sun=6, so >=5 means Sat or Sun.
            // .NET DayOfWeek: Sun=0..Sat=6 — must compare explicitly, NOT >=5.
            int isWknd = (bar.Timestamp.DayOfWeek == DayOfWeek.Saturday
                       || bar.Timestamp.DayOfWeek == DayOfWeek.Sunday) ? 1 : 0;

            double expectedLogVol = 0;
            if (_artifact.ExpectedLogVol.TryGetValue((hour, isWknd), out var ev))
                expectedLogVol = ev;
            double residual = bar.LogVol - expectedLogVol;

            if (_artifact.ClassResidStats.TryGetValue(bar.ClassCombined, out var stats) && stats.std > 0)
                bar.VolZAdj = (residual - stats.mean) / stats.std;
            else
                bar.VolZAdj = 0;

            if (bar.VolZAdj < -0.5) bar.VolBucketAdj = 0;
            else if (bar.VolZAdj > 0.5) bar.VolBucketAdj = 2;
            else bar.VolBucketAdj = 1;
        }

        // Compute 17 features for a given bar position in the 1h ring buffer
        public float[] ComputeFeatures(IReadOnlyList<AggBar> bars1h, int idx)
        {
            var bar = bars1h[idx];
            var features = new float[17];

            // Rolling sigma 24
            features[0] = (float)RollingSigma(bars1h, idx, 24);

            // Price pct in range (24-bar)
            features[1] = (float)PricePctInRange(bars1h, idx, 24);

            // Momentum 4 and 12
            features[2] = (float)RollingRetSum(bars1h, idx, 4);
            features[3] = (float)RollingRetSum(bars1h, idx, 12);

            // Regime 12h proxy (mean class - 2)
            features[4] = (float)Regime12HProxy(bars1h, idx, 12);

            // Abs ret ratio
            features[5] = (float)AbsRetRatio(bars1h, idx, 24);

            // Bars in regime
            features[6] = (float)BarsInRegime(bars1h, idx);

            // Vol z adj
            features[7] = (float)bar.VolZAdj;

            // Prev class
            features[8] = idx > 0 ? bars1h[idx - 1].ClassCombined : -1;

            // Class combined
            features[9] = bar.ClassCombined;

            // Regime 4h 3class
            features[10] = bar.Regime4H3Class;

            // Vol bucket adj
            features[11] = bar.VolBucketAdj;

            // 15m features
            if (bar.SubClasses != null && bar.SubClasses.Length == 4)
            {
                features[12] = bar.SubClasses[3]; // last_15m_class
                features[13] = (bar.SubClasses[0] - 2) + (bar.SubClasses[1] - 2)
                             + (bar.SubClasses[2] - 2) + (bar.SubClasses[3] - 2); // sum_15m_classes
                features[14] = bar.SubClasses.Max() - bar.SubClasses.Min(); // range_15m_classes
            }

            if (bar.SubRets != null && bar.SubRets.Length == 4)
            {
                features[15] = (float)(bar.SubRets[2] + bar.SubRets[3]); // last_two_15m_ret
                features[16] = (float)(bar.SubRets[0] + bar.SubRets[1]); // first_two_15m_ret
            }

            return features;
        }

        private static double RollingSigma(IReadOnlyList<AggBar> bars, int idx, int window)
        {
            int start = Math.Max(0, idx - window + 1);
            if (idx - start + 1 < 2) return 0;
            var rets = new List<double>();
            for (int i = start; i <= idx; i++)
                rets.Add(bars[i].Ret);
            double mean = rets.Average();
            double sumSq = rets.Sum(r => (r - mean) * (r - mean));
            return Math.Sqrt(sumSq / (rets.Count - 1));
        }

        private static double PricePctInRange(IReadOnlyList<AggBar> bars, int idx, int window)
        {
            int start = Math.Max(0, idx - window + 1);
            decimal min = decimal.MaxValue, max = decimal.MinValue;
            for (int i = start; i <= idx; i++)
            {
                if (bars[i].Close < min) min = bars[i].Close;
                if (bars[i].Close > max) max = bars[i].Close;
            }
            decimal range = max - min;
            if (range <= 0) return 0.5;
            return (double)((bars[idx].Close - min) / range);
        }

        private static double RollingRetSum(IReadOnlyList<AggBar> bars, int idx, int window)
        {
            int start = Math.Max(0, idx - window + 1);
            double sum = 0;
            for (int i = start; i <= idx; i++)
                sum += bars[i].Ret;
            return sum;
        }

        private static double Regime12HProxy(IReadOnlyList<AggBar> bars, int idx, int window)
        {
            int start = Math.Max(0, idx - window + 1);
            double sum = 0;
            int count = 0;
            for (int i = start; i <= idx; i++)
            {
                sum += bars[i].ClassCombined;
                count++;
            }
            return count > 0 ? (sum / count) - 2 : 0;
        }

        private static double AbsRetRatio(IReadOnlyList<AggBar> bars, int idx, int window)
        {
            double absRet = Math.Abs(bars[idx].Ret);
            int start = Math.Max(0, idx - window + 1);
            double sum = 0;
            int count = 0;
            for (int i = start; i <= idx; i++)
            {
                sum += Math.Abs(bars[i].Ret);
                count++;
            }
            double avg = count > 0 ? sum / count : 1e-9;
            return absRet / (avg + 1e-9);
        }

        private static int BarsInRegime(IReadOnlyList<AggBar> bars, int idx)
        {
            if (idx == 0) return 1;
            int currentClass = bars[idx].ClassCombined;
            int count = 1;
            for (int i = idx - 1; i >= 0; i--)
            {
                if (bars[i].ClassCombined != currentClass) break;
                count++;
            }
            return count;
        }
    }
}
