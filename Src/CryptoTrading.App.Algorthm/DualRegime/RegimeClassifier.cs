using System;
using System.Linq;

namespace CryptoTrading.App.Algorithm.DualRegime
{
    public static class RegimeClassifier
    {
        // 5-class: 0=strong_bear, 1=bear, 2=chop, 3=bull, 4=strong_bull
        public static int Classify5Class(double ret, double sigma)
        {
            double thrStrong = 2.0 * sigma;
            double thrMild = 0.5 * sigma;
            if (ret > thrStrong) return 4;
            if (ret > thrMild) return 3;
            if (ret > -thrMild) return 2;
            if (ret > -thrStrong) return 1;
            return 0;
        }

        // 3-class collapse for 4H context: 0=bearish, 1=neutral, 2=bullish
        public static int Collapse4H(int class5)
        {
            if (class5 <= 1) return 0;
            if (class5 == 2) return 1;
            return 2;
        }

        // Majority vote with tie-break closest to chop (2)
        public static int MajorityVote(int[] classes)
        {
            var groups = classes.GroupBy(c => c)
                .Select(g => new { Class = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToArray();

            int maxCount = groups[0].Count;
            var candidates = groups.Where(g => g.Count == maxCount)
                .Select(g => g.Class).ToArray();

            if (candidates.Length == 1)
                return candidates[0];

            // Tie-break: closest to chop (2); on equal distance pick the smaller
            // class value to match Python's sorted np.argmin(|candidates - 2|).
            return candidates.OrderBy(c => Math.Abs(c - 2)).ThenBy(c => c).First();
        }
    }
}
