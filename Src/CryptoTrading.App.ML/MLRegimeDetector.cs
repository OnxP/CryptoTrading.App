using System;
using System.Collections.Generic;
using System.IO;
using CryptoTrading.App.Core.Strategy;
using Microsoft.ML;

namespace CryptoTrading.App.ML
{
    /// <summary>
    /// ML-based regime detector using trained LightGBM model.
    /// Implements IRegimeDetector for integration with trading strategy.
    /// Falls back gracefully when no model is available.
    /// </summary>
    public class MLRegimeDetector : IRegimeDetector
    {
        private readonly MLContext _mlContext;
        private PredictionEngine<RegimeTrainingData, RegimePrediction> _predictionEngine;
        private float _lastConfidence;
        private DateTime _lastModelLoad = DateTime.MinValue;

        public bool IsModelLoaded => _predictionEngine != null;

        public MLRegimeDetector()
        {
            _mlContext = new MLContext(42);
        }

        public MarketRegime Predict(IReadOnlyList<decimal[]> recentFeatures)
        {
            if (!IsModelLoaded || recentFeatures == null || recentFeatures.Count == 0)
            {
                _lastConfidence = 0f;
                return MarketRegime.RangingMarket; // Safe default
            }

            // Use latest feature vector
            var latest = recentFeatures[recentFeatures.Count - 1];
            var input = new RegimeTrainingData
            {
                EmaGradient50 = latest.Length > 0 ? (float)latest[0] : 0f,
                EmaGradient200 = latest.Length > 1 ? (float)latest[1] : 0f,
                NormalizedAtr = latest.Length > 2 ? (float)latest[2] : 0f,
                VolumeRatio = latest.Length > 3 ? (float)latest[3] : 0f,
                Rsi = latest.Length > 4 ? (float)latest[4] : 0f,
                BollingerBandWidth = latest.Length > 5 ? (float)latest[5] : 0f,
                MacdHistogram = latest.Length > 6 ? (float)latest[6] : 0f,
                PriceDistanceFromEma50 = latest.Length > 7 ? (float)latest[7] : 0f
            };

            var prediction = _predictionEngine.Predict(input);

            // Get confidence from score array
            if (prediction.Score != null && prediction.Score.Length > 0)
            {
                _lastConfidence = 0f;
                foreach (var score in prediction.Score)
                {
                    if (score > _lastConfidence)
                        _lastConfidence = score;
                }
            }

            // Map prediction label to MarketRegime
            switch (prediction.PredictedLabel)
            {
                case 0: return MarketRegime.BullMarket;
                case 1: return MarketRegime.BearMarket;
                case 2: return MarketRegime.RangingMarket;
                default: return MarketRegime.RangingMarket;
            }
        }

        public float GetConfidence() => _lastConfidence;

        public void ReloadModel(string modelPath)
        {
            if (!File.Exists(modelPath))
            {
                Console.WriteLine($"Model file not found: {modelPath}");
                return;
            }

            try
            {
                var model = _mlContext.Model.Load(modelPath, out _);
                _predictionEngine = _mlContext.Model.CreatePredictionEngine<RegimeTrainingData, RegimePrediction>(model);
                _lastModelLoad = DateTime.UtcNow;
                Console.WriteLine($"Regime model loaded from: {modelPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load regime model: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if model file has been updated and reload if needed.
        /// Call this periodically (e.g., every 4H candle processing cycle).
        /// </summary>
        public bool CheckAndReload(string modelPath)
        {
            if (!File.Exists(modelPath)) return false;

            var fileTime = File.GetLastWriteTimeUtc(modelPath);
            if (fileTime > _lastModelLoad)
            {
                ReloadModel(modelPath);
                return true;
            }
            return false;
        }
    }
}
