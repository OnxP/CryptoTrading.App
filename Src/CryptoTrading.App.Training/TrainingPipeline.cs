using CryptoTrading.App.Algorithm.DualRegime;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Persistence;
using CryptoTrading.App.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;
using System.Text.Json;

using CandleInterval = CryptoTrading.App.Core.Exchange.CandleInterval;

namespace CryptoTrading.App.Training
{
    public class TrainingPipeline
    {
        private readonly CryptoDbContextPg _db;
        private readonly ILogger<TrainingPipeline> _logger;
        private readonly MLContext _mlContext;

        private const int MinBarsRequired = 1500;
        private const double MinR2_1H = 0.05;
        private const double MinR2_4H = 0.15;
        private const int FwdHorizon1H = 1;
        private const int FwdHorizon4H = 4;

        public TrainingPipeline(CryptoDbContextPg db, ILogger<TrainingPipeline> logger)
        {
            _db = db;
            _logger = logger;
            _mlContext = new MLContext(seed: 42);
        }

        public async Task RunAsync(string symbol = "BTCUSDT", string exchangeId = "Binance", int cacheDays = 90)
        {
            _logger.LogInformation("=== DualRegime Training Pipeline ===");

            var candles = await FetchCandlesAsync(exchangeId, symbol, "Minute_15", cacheDays);
            if (candles.Count < MinBarsRequired)
            {
                _logger.LogError($"Only {candles.Count} candles available, need {MinBarsRequired}. Aborting.");
                return;
            }
            _logger.LogInformation($"Fetched {candles.Count} 15M candles ({cacheDays} days)");

            var sigmas = ComputeSigmas(candles);
            _logger.LogInformation($"Sigmas: 15m={sigmas.sigma15M:F6}, 1h={sigmas.sigma1H:F6}, 4h={sigmas.sigma4H:F6}");

            var artifact = BuildBootstrapArtifact(sigmas);
            var aggregator = new DualRegimeCandleAggregator(artifact);
            var featureEngine = new FeatureEngine(artifact);

            var bars1H = AggregateCandles(candles, aggregator);
            _logger.LogInformation($"Aggregated to {bars1H.Count} 1H bars");

            ComputeVolumeNormalization(bars1H, featureEngine, out var expectedLogVol, out var classResidStats);

            var (features1H, labels1H) = BuildDataset(bars1H, featureEngine, FwdHorizon1H);
            var (features4H, labels4H) = BuildDataset(bars1H, featureEngine, FwdHorizon4H);
            _logger.LogInformation($"Dataset: {features1H.Count} samples for 1H, {features4H.Count} samples for 4H");

            var (model1H, r2_1H) = TrainModel(features1H, labels1H, "1H");
            var (model4H, r2_4H) = TrainModel(features4H, labels4H, "4H");
            _logger.LogInformation($"R2: 1H={r2_1H:F4}, 4H={r2_4H:F4}");

            var onnx1H = ExportToOnnx(model1H, features1H, "model_1h");
            var onnx4H = ExportToOnnx(model4H, features4H, "model_4h");
            _logger.LogInformation($"ONNX exported: 1H={onnx1H.Length} bytes, 4H={onnx4H.Length} bytes");

            var thresholds = ComputeThresholds(model1H, model4H, features1H, features4H);

            string[] featureCols = {
                "rolling_sigma_24", "price_pct_in_range_24", "momentum_4", "momentum_12",
                "regime_12h_proxy", "abs_ret_ratio", "bars_in_regime", "vol_z_adj",
                "prev_class", "class_combined", "regime_4h_3class", "vol_bucket_adj",
                "last_15m_class", "sum_15m_classes", "range_15m_classes",
                "last_two_15m_ret", "first_two_15m_ret"
            };

            var manifest = BuildManifest(sigmas, r2_1H, r2_4H, thresholds, bars1H.Count,
                expectedLogVol, classResidStats, featureCols);

            bool passes = r2_1H >= MinR2_1H && r2_4H >= MinR2_4H && bars1H.Count >= MinBarsRequired;
            _logger.LogInformation($"Quality gates: {(passes ? "PASS" : "FAIL")}");

            var version = await GetNextVersionAsync("DualRegime");
            await SaveToDbAsync("DualRegime", version, manifest, onnx1H, onnx4H, r2_1H, r2_4H, passes);

            _logger.LogInformation($"=== Pipeline complete. Version {version}, promoted={passes} ===");
        }

        private async Task<List<ExchangeCandlestick>> FetchCandlesAsync(
            string exchangeId, string symbol, string intervalStr, int days)
        {
            int interval = (int)CandleInterval.Minute_15;
            var cutoff = DateTime.UtcNow.AddDays(-days);
            var entities = await _db.Candlesticks
                .Where(c => c.ExchangeId == exchangeId
                    && c.Symbol == symbol
                    && c.Interval == interval
                    && c.OpenTime >= cutoff)
                .OrderBy(c => c.OpenTime)
                .ToListAsync();

            return entities.Select(e => e.ToExchangeCandlestick()).ToList();
        }

        private (double sigma15M, double sigma1H, double sigma4H) ComputeSigmas(
            List<ExchangeCandlestick> candles)
        {
            var rets15M = new List<double>();
            for (int i = 1; i < candles.Count; i++)
            {
                if (candles[i - 1].Close > 0)
                    rets15M.Add(Math.Log((double)candles[i].Close / (double)candles[i - 1].Close));
            }

            double sigma15M = StdDev(rets15M);

            var rets1H = new List<double>();
            for (int i = 4; i < candles.Count; i += 4)
            {
                int start = i - 4;
                double sum = 0;
                for (int j = start + 1; j <= i && j < candles.Count; j++)
                {
                    if (candles[j - 1].Close > 0)
                        sum += Math.Log((double)candles[j].Close / (double)candles[j - 1].Close);
                }
                rets1H.Add(sum);
            }

            var rets4H = new List<double>();
            for (int i = 16; i < candles.Count; i += 16)
            {
                int start = i - 16;
                double sum = 0;
                for (int j = start + 1; j <= i && j < candles.Count; j++)
                {
                    if (candles[j - 1].Close > 0)
                        sum += Math.Log((double)candles[j].Close / (double)candles[j - 1].Close);
                }
                rets4H.Add(sum);
            }

            return (sigma15M, StdDev(rets1H), StdDev(rets4H));
        }

        private DualRegimeModelArtifact BuildBootstrapArtifact(
            (double sigma15M, double sigma1H, double sigma4H) sigmas)
        {
            return DualRegimeModelArtifact.CreateForTraining(
                sigmas.sigma15M, sigmas.sigma1H, sigmas.sigma4H);
        }

        private List<AggBar> AggregateCandles(
            List<ExchangeCandlestick> candles, DualRegimeCandleAggregator aggregator)
        {
            var bars = new List<AggBar>();
            foreach (var candle in candles)
            {
                var bar = aggregator.AddBar15M(candle);
                if (bar != null)
                    bars.Add(bar);
            }
            return bars;
        }

        private void ComputeVolumeNormalization(List<AggBar> bars, FeatureEngine engine,
            out Dictionary<string, double> expectedLogVol,
            out Dictionary<int, (double mean, double std)> classResidStats)
        {
            var logVolByKey = new Dictionary<string, List<double>>();
            foreach (var bar in bars)
            {
                bar.LogVol = Math.Log((double)bar.Volume + 1e-9);
                int hour = bar.Timestamp.Hour;
                int isWknd = (bar.Timestamp.DayOfWeek == DayOfWeek.Saturday
                           || bar.Timestamp.DayOfWeek == DayOfWeek.Sunday) ? 1 : 0;
                string key = $"{hour}_{isWknd}";
                if (!logVolByKey.ContainsKey(key))
                    logVolByKey[key] = new List<double>();
                logVolByKey[key].Add(bar.LogVol);
            }

            expectedLogVol = new Dictionary<string, double>();
            foreach (var kv in logVolByKey)
                expectedLogVol[kv.Key] = kv.Value.Average();

            var residByClass = new Dictionary<int, List<double>>();
            foreach (var bar in bars)
            {
                int hour = bar.Timestamp.Hour;
                int isWknd = (bar.Timestamp.DayOfWeek == DayOfWeek.Saturday
                           || bar.Timestamp.DayOfWeek == DayOfWeek.Sunday) ? 1 : 0;
                string key = $"{hour}_{isWknd}";
                double resid = bar.LogVol - expectedLogVol[key];

                if (!residByClass.ContainsKey(bar.ClassCombined))
                    residByClass[bar.ClassCombined] = new List<double>();
                residByClass[bar.ClassCombined].Add(resid);
            }

            classResidStats = new Dictionary<int, (double, double)>();
            foreach (var kv in residByClass)
            {
                double mean = kv.Value.Average();
                double std = StdDev(kv.Value);
                classResidStats[kv.Key] = (mean, std);
            }

            foreach (var bar in bars)
            {
                int hour = bar.Timestamp.Hour;
                int isWknd = (bar.Timestamp.DayOfWeek == DayOfWeek.Saturday
                           || bar.Timestamp.DayOfWeek == DayOfWeek.Sunday) ? 1 : 0;
                string key = $"{hour}_{isWknd}";
                double resid = bar.LogVol - expectedLogVol[key];

                if (classResidStats.TryGetValue(bar.ClassCombined, out var stats) && stats.std > 0)
                    bar.VolZAdj = (resid - stats.mean) / stats.std;
                else
                    bar.VolZAdj = 0;

                if (bar.VolZAdj < -0.5) bar.VolBucketAdj = 0;
                else if (bar.VolZAdj > 0.5) bar.VolBucketAdj = 2;
                else bar.VolBucketAdj = 1;
            }
        }

        private (List<float[]> features, List<float> labels) BuildDataset(
            List<AggBar> bars, FeatureEngine engine, int fwdHorizon)
        {
            var features = new List<float[]>();
            var labels = new List<float>();
            int lookback = 25;

            for (int i = lookback; i < bars.Count - fwdHorizon; i++)
            {
                var feat = engine.ComputeFeatures(bars, i);
                if (feat == null) continue;

                double fwdRet = 0;
                for (int j = 1; j <= fwdHorizon && i + j < bars.Count; j++)
                    fwdRet += bars[i + j].Ret;

                features.Add(feat);
                labels.Add((float)fwdRet);
            }

            return (features, labels);
        }

        private (ITransformer model, double r2) TrainModel(
            List<float[]> features, List<float> labels, string name)
        {
            var data = features.Zip(labels, (f, l) => new FeatureDataRow { Features = f, Label = l });
            var dataView = _mlContext.Data.LoadFromEnumerable(data);

            var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2, seed: 42);

            var pipeline = _mlContext.Transforms.CopyColumns("Features", "Features")
                .Append(_mlContext.Regression.Trainers.FastTree(new FastTreeRegressionTrainer.Options
                {
                    LabelColumnName = "Label",
                    FeatureColumnName = "Features",
                    NumberOfLeaves = 31,
                    NumberOfTrees = 200,
                    MinimumExampleCountPerLeaf = 10,
                    LearningRate = 0.05,
                }));

            _logger.LogInformation($"Training {name} model on {features.Count} samples...");
            var model = pipeline.Fit(split.TrainSet);

            var predictions = model.Transform(split.TestSet);
            var metrics = _mlContext.Regression.Evaluate(predictions);
            _logger.LogInformation($"{name}: R2={metrics.RSquared:F4}, MAE={metrics.MeanAbsoluteError:F6}, RMSE={metrics.RootMeanSquaredError:F6}");

            return (model, metrics.RSquared);
        }

        private byte[] ExportToOnnx(ITransformer model, List<float[]> sampleFeatures, string modelName)
        {
            var sampleData = sampleFeatures.Take(10)
                .Select(f => new FeatureDataRow { Features = f, Label = 0 });
            var sampleView = _mlContext.Data.LoadFromEnumerable(sampleData);

            var tempPath = Path.Combine(Path.GetTempPath(), $"{modelName}_{Guid.NewGuid()}.onnx");
            try
            {
                using var stream = File.Create(tempPath);
                _mlContext.Model.ConvertToOnnx(model, sampleView, stream);
                stream.Flush();
                stream.Position = 0;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private (double thr1H, double thr4H) ComputeThresholds(
            ITransformer model1H, ITransformer model4H,
            List<float[]> features1H, List<float[]> features4H)
        {
            var preds1H = PredictAll(model1H, features1H);
            var preds4H = PredictAll(model4H, features4H);

            double thr1H = Percentile(preds1H.Select(Math.Abs).ToList(), 0.33);
            double thr4H = Percentile(preds4H.Select(Math.Abs).ToList(), 0.33);

            return (thr1H, thr4H);
        }

        private List<double> PredictAll(ITransformer model, List<float[]> features)
        {
            var data = features.Select(f => new FeatureDataRow { Features = f, Label = 0 });
            var dataView = _mlContext.Data.LoadFromEnumerable(data);
            var predictions = model.Transform(dataView);
            var scores = _mlContext.Data.CreateEnumerable<PredictionResult>(predictions, false);
            return scores.Select(p => (double)p.Score).ToList();
        }

        private string BuildManifest(
            (double sigma15M, double sigma1H, double sigma4H) sigmas,
            double r2_1H, double r2_4H,
            (double thr1H, double thr4H) thresholds,
            int trainNBars,
            Dictionary<string, double> expectedLogVol,
            Dictionary<int, (double mean, double std)> classResidStats,
            string[] featureCols)
        {
            var manifest = new Dictionary<string, object>
            {
                ["schema_version"] = 2,
                ["trained_at"] = DateTime.UtcNow.ToString("o"),
                ["train_n_bars"] = trainNBars,
                ["sigma_15m"] = sigmas.sigma15M,
                ["sigma_1h"] = sigmas.sigma1H,
                ["sigma_4h"] = sigmas.sigma4H,
                ["thr_1h"] = thresholds.thr1H,
                ["thr_4h"] = thresholds.thr4H,
                ["val_r2_1h"] = r2_1H,
                ["val_r2_4h"] = r2_4H,
                ["feat_cols"] = featureCols,
                ["expected_log_vol"] = expectedLogVol,
                ["class_resid_stats"] = classResidStats.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => new { mean = kv.Value.mean, std = kv.Value.std }),
            };

            return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        }

        private async Task<int> GetNextVersionAsync(string strategyName)
        {
            var maxVersion = await _db.ModelArtifacts
                .Where(m => m.StrategyName == strategyName)
                .MaxAsync(m => (int?)m.Version) ?? 0;
            return maxVersion + 1;
        }

        private async Task SaveToDbAsync(string strategyName, int version, string manifestJson,
            byte[] onnx1H, byte[] onnx4H, double r2_1H, double r2_4H, bool promote)
        {
            var entity = new ModelArtifactEntity
            {
                StrategyName = strategyName,
                Version = version,
                TrainedAt = DateTime.UtcNow,
                ManifestJson = manifestJson,
                Model1hOnnx = onnx1H,
                Model4hOnnx = onnx4H,
                ValR2_1h = r2_1H,
                ValR2_4h = r2_4H,
                IsActive = promote,
            };

            if (promote)
            {
                entity.PromotedAt = DateTime.UtcNow;
                var previousActive = await _db.ModelArtifacts
                    .Where(m => m.StrategyName == strategyName && m.IsActive)
                    .ToListAsync();
                foreach (var prev in previousActive)
                    prev.IsActive = false;
            }

            _db.ModelArtifacts.Add(entity);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Saved model artifact v{version} (promoted={promote})");
        }

        private static double StdDev(List<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = values.Average();
            double sumSq = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sumSq / (values.Count - 1));
        }

        private static double Percentile(List<double> values, double p)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int idx = (int)(sorted.Count * p);
            return sorted[Math.Min(idx, sorted.Count - 1)];
        }
    }
}
