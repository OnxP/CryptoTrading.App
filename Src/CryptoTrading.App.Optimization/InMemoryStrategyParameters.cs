using System;
using System.Collections.Generic;
using CryptoTrading.App.Core;

namespace CryptoTrading.App.Optimization
{
    /// <summary>
    /// In-memory implementation of IStrategyParameters for fast optimization runs.
    /// No database writes during search - purely in-memory parameter storage.
    /// </summary>
    public class InMemoryStrategyParameters : IStrategyParameters
    {
        private readonly Dictionary<string, double> _parameters;

        public string ActiveParameterSet { get; set; } = "Optimization";

        public InMemoryStrategyParameters()
        {
            _parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        public InMemoryStrategyParameters(IDictionary<string, double> parameters)
        {
            _parameters = new Dictionary<string, double>(parameters, StringComparer.OrdinalIgnoreCase);
        }

        public void Set(string name, double value)
        {
            _parameters[name] = value;
        }

        public double Get(string name, double defaultValue)
        {
            return _parameters.TryGetValue(name, out var value) ? value : defaultValue;
        }

        public decimal GetDecimal(string name, decimal defaultValue)
        {
            return _parameters.TryGetValue(name, out var value) ? (decimal)value : defaultValue;
        }

        public int GetInt(string name, int defaultValue)
        {
            return _parameters.TryGetValue(name, out var value) ? (int)value : defaultValue;
        }

        public IDictionary<string, double> GetAll(string strategyName = null)
        {
            return new Dictionary<string, double>(_parameters);
        }

        public void Reload() { /* No-op for in-memory */ }
    }
}
