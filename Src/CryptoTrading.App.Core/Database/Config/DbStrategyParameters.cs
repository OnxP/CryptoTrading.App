using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core.Database.Config
{
    /// <summary>
    /// Database-backed implementation of IStrategyParameters.
    /// Caches parameters in memory with periodic reload from SQL Server.
    /// </summary>
    public class DbStrategyParameters : IStrategyParameters
    {
        private readonly string _dataSource;
        private readonly string _strategyName;
        private Dictionary<string, double> _parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastReload = DateTime.MinValue;
        private readonly TimeSpan _reloadInterval = TimeSpan.FromSeconds(60);

        public string ActiveParameterSet { get; private set; } = "Default";

        public DbStrategyParameters(string dataSource, string strategyName)
        {
            _dataSource = dataSource;
            _strategyName = strategyName;
            Reload();
        }

        public double Get(string name, double defaultValue)
        {
            EnsureFresh();
            return _parameters.TryGetValue(name, out var value) ? value : defaultValue;
        }

        public decimal GetDecimal(string name, decimal defaultValue)
        {
            EnsureFresh();
            return _parameters.TryGetValue(name, out var value) ? (decimal)value : defaultValue;
        }

        public int GetInt(string name, int defaultValue)
        {
            EnsureFresh();
            return _parameters.TryGetValue(name, out var value) ? (int)value : defaultValue;
        }

        public IDictionary<string, double> GetAll(string strategyName = null)
        {
            EnsureFresh();
            return new Dictionary<string, double>(_parameters);
        }

        public void Reload()
        {
            try
            {
                using (var context = new StrategyParameterContext(_dataSource))
                {
                    var dbParams = context.StrategyParameters
                        .Where(p => p.StrategyName == _strategyName && p.IsActive)
                        .ToList();

                    var newParams = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in dbParams)
                    {
                        newParams[p.ParameterName] = p.Value;
                    }

                    _parameters = newParams;
                    _lastReload = DateTime.UtcNow;
                }
            }
            catch (Exception)
            {
                // If DB is unavailable, keep using cached values
                // All callers use Get(name, default) so defaults are used for missing params
                _lastReload = DateTime.UtcNow;
            }
        }

        private void EnsureFresh()
        {
            if (DateTime.UtcNow - _lastReload > _reloadInterval)
            {
                Reload();
            }
        }
    }
}
