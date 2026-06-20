using Microsoft.ML.Data;

namespace CryptoTrading.App.Training
{
    public class FeatureDataRow
    {
        [VectorType(17)]
        public float[] Features { get; set; }

        [ColumnName("Label")]
        public float Label { get; set; }
    }

    public class PredictionResult
    {
        [ColumnName("Score")]
        public float Score { get; set; }
    }
}
