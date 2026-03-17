using System.Collections.Generic;
using CryptoTrading.App.Core.Strategy;

namespace CryptoTrading.App.ML
{
    /// <summary>
    /// Interface for ML-based market regime detection.
    /// Implementations can use trained models or rule-based fallbacks.
    /// </summary>
    public interface IRegimeDetector
    {
        /// <summary>
        /// Predict the current market regime from recent price data.
        /// </summary>
        MarketRegime Predict(IReadOnlyList<decimal[]> recentFeatures);

        /// <summary>
        /// Confidence of the last prediction (0.0 to 1.0).
        /// </summary>
        float GetConfidence();

        /// <summary>
        /// Hot-swap the model file without restarting.
        /// </summary>
        void ReloadModel(string modelPath);

        /// <summary>
        /// Whether a trained model is available.
        /// </summary>
        bool IsModelLoaded { get; }
    }
}
