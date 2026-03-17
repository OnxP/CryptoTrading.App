using Microsoft.ML.Data;

namespace CryptoTrading.App.ML
{
    /// <summary>
    /// ML.NET input schema for regime classification training.
    /// </summary>
    public class RegimeTrainingData
    {
        [LoadColumn(0)] public float EmaGradient50 { get; set; }
        [LoadColumn(1)] public float EmaGradient200 { get; set; }
        [LoadColumn(2)] public float NormalizedAtr { get; set; }
        [LoadColumn(3)] public float VolumeRatio { get; set; }
        [LoadColumn(4)] public float Rsi { get; set; }
        [LoadColumn(5)] public float BollingerBandWidth { get; set; }
        [LoadColumn(6)] public float MacdHistogram { get; set; }
        [LoadColumn(7)] public float PriceDistanceFromEma50 { get; set; }

        /// <summary>
        /// Label: 0 = BullMarket, 1 = BearMarket, 2 = RangingMarket
        /// </summary>
        [LoadColumn(8)]
        [ColumnName("Label")]
        public uint Label { get; set; }
    }

    /// <summary>
    /// ML.NET prediction output.
    /// </summary>
    public class RegimePrediction
    {
        [ColumnName("PredictedLabel")]
        public uint PredictedLabel { get; set; }

        [ColumnName("Score")]
        public float[] Score { get; set; }
    }
}
