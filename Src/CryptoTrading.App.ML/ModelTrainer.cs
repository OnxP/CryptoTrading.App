using System;
using System.IO;
using System.Linq;
using Microsoft.ML;

namespace CryptoTrading.App.ML
{
    /// <summary>
    /// Trains a LightGBM multiclass classifier for market regime detection.
    /// Uses features extracted from candlestick data and labels from rule-based classification.
    /// </summary>
    public class ModelTrainer
    {
        private readonly MLContext _mlContext;

        public ModelTrainer(int? seed = null)
        {
            _mlContext = new MLContext(seed ?? 42);
        }

        /// <summary>
        /// Train a regime classification model from feature/label data.
        /// </summary>
        /// <param name="trainingDataPath">CSV file with features + label column</param>
        /// <param name="outputModelPath">Where to save the trained model (.zip)</param>
        /// <returns>Cross-validation accuracy</returns>
        public double Train(string trainingDataPath, string outputModelPath)
        {
            Console.WriteLine("Loading training data...");
            var data = _mlContext.Data.LoadFromTextFile<RegimeTrainingData>(
                trainingDataPath,
                separatorChar: ',',
                hasHeader: true);

            var featureColumns = new[]
            {
                nameof(RegimeTrainingData.EmaGradient50),
                nameof(RegimeTrainingData.EmaGradient200),
                nameof(RegimeTrainingData.NormalizedAtr),
                nameof(RegimeTrainingData.VolumeRatio),
                nameof(RegimeTrainingData.Rsi),
                nameof(RegimeTrainingData.BollingerBandWidth),
                nameof(RegimeTrainingData.MacdHistogram),
                nameof(RegimeTrainingData.PriceDistanceFromEma50)
            };

            var pipeline = _mlContext.Transforms.Conversion
                .MapValueToKey("Label")
                .Append(_mlContext.Transforms.Concatenate("Features", featureColumns))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.MulticlassClassification.Trainers.LightGbm(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfLeaves: 31,
                    numberOfIterations: 100,
                    learningRate: 0.1))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            // Cross-validate
            Console.WriteLine("Cross-validating (5-fold)...");
            var cvResults = _mlContext.MulticlassClassification.CrossValidate(data, pipeline, numberOfFolds: 5);
            var avgAccuracy = cvResults.Average(r => r.Metrics.MacroAccuracy);
            Console.WriteLine($"Average cross-validation accuracy: {avgAccuracy:P2}");

            // Train final model on all data
            Console.WriteLine("Training final model...");
            var model = pipeline.Fit(data);

            // Save
            _mlContext.Model.Save(model, data.Schema, outputModelPath);
            Console.WriteLine($"Model saved to: {outputModelPath}");

            return avgAccuracy;
        }
    }
}
