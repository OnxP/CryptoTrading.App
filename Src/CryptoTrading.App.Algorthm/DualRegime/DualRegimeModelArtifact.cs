using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CryptoTrading.App.Algorithm.DualRegime
{
    public sealed class DualRegimeModelArtifact : IDisposable
    {
        private InferenceSession _model1H;
        private InferenceSession _model4H;

        public int SchemaVersion { get; private set; }
        public DateTime TrainedAt { get; private set; }
        public int TrainNBars { get; private set; }

        // Sigma thresholds (from training data)
        public double Sigma15M { get; private set; }
        public double Sigma1H { get; private set; }
        public double Sigma4H { get; private set; }
        public double Thr1H { get; private set; }
        public double Thr4H { get; private set; }

        // Volume normalization tables
        public Dictionary<(int hour, int isWeekend), double> ExpectedLogVol { get; private set; }
        public Dictionary<int, (double mean, double std)> ClassResidStats { get; private set; }

        // Validation metrics
        public double ValR2_1H { get; private set; }
        public double ValR2_4H { get; private set; }

        public string[] FeatureCols { get; private set; }

        public static DualRegimeModelArtifact CreateForTraining(
            double sigma15M, double sigma1H, double sigma4H)
        {
            return new DualRegimeModelArtifact
            {
                SchemaVersion = 2,
                TrainedAt = DateTime.UtcNow,
                Sigma15M = sigma15M,
                Sigma1H = sigma1H,
                Sigma4H = sigma4H,
                ExpectedLogVol = new Dictionary<(int, int), double>(),
                ClassResidStats = new Dictionary<int, (double, double)>(),
                FeatureCols = Array.Empty<string>(),
            };
        }

        public static DualRegimeModelArtifact LoadFromDb(
            string manifestJson, byte[] onnx1H, byte[] onnx4H)
        {
            var artifact = new DualRegimeModelArtifact();

            var doc = JsonDocument.Parse(manifestJson);
            var root = doc.RootElement;

            artifact.SchemaVersion = root.GetProperty("schema_version").GetInt32();
            artifact.TrainedAt = root.GetProperty("trained_at").GetDateTime();
            artifact.TrainNBars = root.GetProperty("train_n_bars").GetInt32();

            artifact.Sigma15M = root.GetProperty("sigma_15m").GetDouble();
            artifact.Sigma1H = root.GetProperty("sigma_1h").GetDouble();
            artifact.Sigma4H = root.GetProperty("sigma_4h").GetDouble();
            artifact.Thr1H = root.GetProperty("thr_1h").GetDouble();
            artifact.Thr4H = root.GetProperty("thr_4h").GetDouble();

            artifact.ValR2_1H = root.GetProperty("val_r2_1h").GetDouble();
            artifact.ValR2_4H = root.GetProperty("val_r2_4h").GetDouble();

            var featArr = root.GetProperty("feat_cols");
            artifact.FeatureCols = new string[featArr.GetArrayLength()];
            for (int i = 0; i < artifact.FeatureCols.Length; i++)
                artifact.FeatureCols[i] = featArr[i].GetString();

            artifact.ExpectedLogVol = new Dictionary<(int, int), double>();
            foreach (var entry in root.GetProperty("expected_log_vol").EnumerateObject())
            {
                var parts = entry.Name.Split('_');
                int hour = int.Parse(parts[0]);
                int isWknd = int.Parse(parts[1]);
                artifact.ExpectedLogVol[(hour, isWknd)] = entry.Value.GetDouble();
            }

            artifact.ClassResidStats = new Dictionary<int, (double, double)>();
            foreach (var entry in root.GetProperty("class_resid_stats").EnumerateObject())
            {
                int cls = int.Parse(entry.Name);
                double mean = entry.Value.GetProperty("mean").GetDouble();
                double std = entry.Value.GetProperty("std").GetDouble();
                artifact.ClassResidStats[cls] = (mean, std);
            }

            var opts = new SessionOptions { InterOpNumThreads = 1, IntraOpNumThreads = 1 };
            artifact._model1H = new InferenceSession(onnx1H, opts);
            artifact._model4H = new InferenceSession(onnx4H, opts);

            return artifact;
        }

        public static DualRegimeModelArtifact Load(string artifactDir)
        {
            var artifact = new DualRegimeModelArtifact();

            var manifestPath = Path.Combine(artifactDir, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException($"manifest.json not found in {artifactDir}");

            var json = File.ReadAllText(manifestPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            artifact.SchemaVersion = root.GetProperty("schema_version").GetInt32();
            artifact.TrainedAt = root.GetProperty("trained_at").GetDateTime();
            artifact.TrainNBars = root.GetProperty("train_n_bars").GetInt32();

            artifact.Sigma15M = root.GetProperty("sigma_15m").GetDouble();
            artifact.Sigma1H = root.GetProperty("sigma_1h").GetDouble();
            artifact.Sigma4H = root.GetProperty("sigma_4h").GetDouble();
            artifact.Thr1H = root.GetProperty("thr_1h").GetDouble();
            artifact.Thr4H = root.GetProperty("thr_4h").GetDouble();

            artifact.ValR2_1H = root.GetProperty("val_r2_1h").GetDouble();
            artifact.ValR2_4H = root.GetProperty("val_r2_4h").GetDouble();

            // Feature columns
            var featArr = root.GetProperty("feat_cols");
            artifact.FeatureCols = new string[featArr.GetArrayLength()];
            for (int i = 0; i < artifact.FeatureCols.Length; i++)
                artifact.FeatureCols[i] = featArr[i].GetString();

            // Volume normalization: expected_log_vol
            artifact.ExpectedLogVol = new Dictionary<(int, int), double>();
            foreach (var entry in root.GetProperty("expected_log_vol").EnumerateObject())
            {
                var parts = entry.Name.Split('_');
                int hour = int.Parse(parts[0]);
                int isWknd = int.Parse(parts[1]);
                artifact.ExpectedLogVol[(hour, isWknd)] = entry.Value.GetDouble();
            }

            // Volume normalization: class_resid_stats
            artifact.ClassResidStats = new Dictionary<int, (double, double)>();
            foreach (var entry in root.GetProperty("class_resid_stats").EnumerateObject())
            {
                int cls = int.Parse(entry.Name);
                double mean = entry.Value.GetProperty("mean").GetDouble();
                double std = entry.Value.GetProperty("std").GetDouble();
                artifact.ClassResidStats[cls] = (mean, std);
            }

            // Load ONNX models
            var model1hPath = Path.Combine(artifactDir, "model_1h.onnx");
            var model4hPath = Path.Combine(artifactDir, "model_4h.onnx");
            if (!File.Exists(model1hPath))
                throw new FileNotFoundException($"model_1h.onnx not found in {artifactDir}");
            if (!File.Exists(model4hPath))
                throw new FileNotFoundException($"model_4h.onnx not found in {artifactDir}");

            var opts = new SessionOptions();
            opts.InterOpNumThreads = 1;
            opts.IntraOpNumThreads = 1;
            artifact._model1H = new InferenceSession(model1hPath, opts);
            artifact._model4H = new InferenceSession(model4hPath, opts);

            return artifact;
        }

        public bool Validate()
        {
            if (ValR2_4H < 0.15) return false;
            if (ValR2_1H < 0.05) return false;
            if (TrainNBars < 1500) return false;
            if (FeatureCols == null || FeatureCols.Length != 17) return false;
            if ((DateTime.UtcNow - TrainedAt).TotalDays > 14) return false;
            return true;
        }

        public double Predict1H(float[] features)
        {
            return RunInference(_model1H, features);
        }

        public double Predict4H(float[] features)
        {
            return RunInference(_model4H, features);
        }

        private static double RunInference(InferenceSession session, float[] features)
        {
            var tensor = new DenseTensor<float>(features, new[] { 1, features.Length });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", tensor)
            };
            using var results = session.Run(inputs);
            var output = results.First().AsTensor<float>();
            return output[0];
        }

        public int ClassifyPred(double prediction, double threshold)
        {
            if (prediction > threshold) return 1;
            if (prediction < -threshold) return -1;
            return 0;
        }

        public void Dispose()
        {
            _model1H?.Dispose();
            _model4H?.Dispose();
        }
    }
}
