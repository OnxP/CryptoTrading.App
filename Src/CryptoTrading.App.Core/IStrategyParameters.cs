using System.Collections.Generic;

namespace CryptoTrading.App.Core
{
    /// <summary>
    /// Interface for accessing strategy parameters.
    /// Allows hot-reload from database without restarting the application.
    /// </summary>
    public interface IStrategyParameters
    {
        /// <summary>
        /// Get a parameter value by name, returning default if not found.
        /// </summary>
        double Get(string name, double defaultValue);

        /// <summary>
        /// Get a decimal parameter value by name, returning default if not found.
        /// </summary>
        decimal GetDecimal(string name, decimal defaultValue);

        /// <summary>
        /// Get an integer parameter value by name, returning default if not found.
        /// </summary>
        int GetInt(string name, int defaultValue);

        /// <summary>
        /// Get all parameters for a given strategy.
        /// </summary>
        IDictionary<string, double> GetAll(string strategyName = null);

        /// <summary>
        /// Force reload parameters from the backing store.
        /// </summary>
        void Reload();

        /// <summary>
        /// The name of the active parameter set.
        /// </summary>
        string ActiveParameterSet { get; }
    }
}
