using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Optimization
{
    /// <summary>
    /// Defines the parameter search space for optimization.
    /// Each parameter has a min, max, and step size for grid search enumeration.
    /// </summary>
    public class ParameterSpace
    {
        public List<ParameterRange> Ranges { get; } = new List<ParameterRange>();

        public void AddRange(string name, double min, double max, double step)
        {
            Ranges.Add(new ParameterRange(name, min, max, step));
        }

        /// <summary>
        /// Enumerate all parameter combinations in the search space.
        /// Uses grid search: each parameter steps from min to max by step.
        /// </summary>
        public IEnumerable<IDictionary<string, double>> EnumerateAll()
        {
            if (Ranges.Count == 0)
            {
                yield return new Dictionary<string, double>();
                yield break;
            }

            var values = Ranges.Select(r => r.GetValues().ToList()).ToList();
            var indices = new int[Ranges.Count];

            while (true)
            {
                var combination = new Dictionary<string, double>();
                for (int i = 0; i < Ranges.Count; i++)
                {
                    combination[Ranges[i].Name] = values[i][indices[i]];
                }
                yield return combination;

                // Increment indices (like odometer)
                int pos = Ranges.Count - 1;
                while (pos >= 0)
                {
                    indices[pos]++;
                    if (indices[pos] < values[pos].Count)
                        break;
                    indices[pos] = 0;
                    pos--;
                }
                if (pos < 0) break;
            }
        }

        /// <summary>
        /// Total number of combinations in the search space.
        /// </summary>
        public long TotalCombinations
        {
            get
            {
                if (Ranges.Count == 0) return 1;
                long total = 1;
                foreach (var range in Ranges)
                {
                    total *= range.GetValues().Count();
                }
                return total;
            }
        }
    }

    public class ParameterRange
    {
        public string Name { get; }
        public double Min { get; }
        public double Max { get; }
        public double Step { get; }

        public ParameterRange(string name, double min, double max, double step)
        {
            Name = name;
            Min = min;
            Max = max;
            Step = step > 0 ? step : 1;
        }

        public IEnumerable<double> GetValues()
        {
            for (double v = Min; v <= Max + Step * 0.001; v += Step)
            {
                yield return Math.Round(v, 10);
            }
        }
    }
}
