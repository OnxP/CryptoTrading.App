using System;
using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.ML
{
    /// <summary>
    /// Transforms candlestick data into feature vectors for ML regime classification.
    /// </summary>
    public class FeatureExtractor
    {
        /// <summary>
        /// Extract features from a window of candlestick data.
        /// Returns: [EMA50Gradient, EMA200Gradient, NormalizedATR, VolumeRatio,
        ///           RSI, BBWidth, MACDHistogram, PriceDistanceFromEMA50]
        /// </summary>
        public float[] ExtractFeatures(IReadOnlyList<ExchangeCandlestick> candles, int index)
        {
            if (candles == null || index < 200 || index >= candles.Count)
                return new float[8];

            var closes = candles.Select(c => (double)c.Close).ToArray();
            var highs = candles.Select(c => (double)c.High).ToArray();
            var lows = candles.Select(c => (double)c.Low).ToArray();
            var volumes = candles.Select(c => (double)c.Volume).ToArray();

            // EMA-50 gradient (slope over last 5 periods)
            var ema50 = CalculateEMA(closes, 50, index);
            var ema50Prev = CalculateEMA(closes, 50, index - 5);
            var ema50Gradient = (float)((ema50 - ema50Prev) / ema50Prev);

            // EMA-200 gradient
            var ema200 = CalculateEMA(closes, 200, index);
            var ema200Prev = CalculateEMA(closes, 200, index - 5);
            var ema200Gradient = (float)((ema200 - ema200Prev) / ema200Prev);

            // Normalized ATR (ATR / price)
            var atr = CalculateATR(highs, lows, closes, 14, index);
            var normalizedATR = (float)(atr / closes[index]);

            // Volume ratio (current / 20-bar average)
            var avgVolume = volumes.Skip(index - 20).Take(20).Average();
            var volumeRatio = avgVolume > 0 ? (float)(volumes[index] / avgVolume) : 1f;

            // RSI
            var rsi = (float)CalculateRSI(closes, 14, index);

            // Bollinger Band width (normalized)
            var (_, upper, lower) = CalculateBollingerBands(closes, 20, 2, index);
            var bbWidth = closes[index] > 0 ? (float)((upper - lower) / closes[index]) : 0f;

            // MACD histogram
            var macdHist = (float)CalculateMACDHistogram(closes, 12, 26, 9, index);

            // Price distance from EMA-50 (normalized)
            var priceDistEma50 = (float)((closes[index] - ema50) / ema50);

            return new[]
            {
                ema50Gradient, ema200Gradient, normalizedATR, volumeRatio,
                rsi, bbWidth, macdHist, priceDistEma50
            };
        }

        #region Technical Indicator Calculations

        private double CalculateEMA(double[] data, int period, int index)
        {
            if (index < period) return data[index];
            var multiplier = 2.0 / (period + 1);
            var ema = data[index - period];
            for (int i = index - period + 1; i <= index; i++)
            {
                ema = (data[i] - ema) * multiplier + ema;
            }
            return ema;
        }

        private double CalculateATR(double[] highs, double[] lows, double[] closes, int period, int index)
        {
            if (index < period) return 0;
            double atr = 0;
            for (int i = index - period + 1; i <= index; i++)
            {
                var tr = Math.Max(highs[i] - lows[i],
                    Math.Max(Math.Abs(highs[i] - closes[i - 1]),
                             Math.Abs(lows[i] - closes[i - 1])));
                atr += tr;
            }
            return atr / period;
        }

        private double CalculateRSI(double[] closes, int period, int index)
        {
            if (index < period + 1) return 50;
            double avgGain = 0, avgLoss = 0;
            for (int i = index - period + 1; i <= index; i++)
            {
                var change = closes[i] - closes[i - 1];
                if (change > 0) avgGain += change;
                else avgLoss -= change;
            }
            avgGain /= period;
            avgLoss /= period;
            if (avgLoss == 0) return 100;
            var rs = avgGain / avgLoss;
            return 100 - (100 / (1 + rs));
        }

        private (double middle, double upper, double lower) CalculateBollingerBands(
            double[] data, int period, double stdDevMultiplier, int index)
        {
            if (index < period) return (data[index], data[index], data[index]);
            var window = data.Skip(index - period + 1).Take(period).ToArray();
            var middle = window.Average();
            var stdDev = Math.Sqrt(window.Sum(x => (x - middle) * (x - middle)) / period);
            return (middle, middle + stdDevMultiplier * stdDev, middle - stdDevMultiplier * stdDev);
        }

        private double CalculateMACDHistogram(double[] closes, int fast, int slow, int signal, int index)
        {
            var emaFast = CalculateEMA(closes, fast, index);
            var emaSlow = CalculateEMA(closes, slow, index);
            var macd = emaFast - emaSlow;
            // Simplified signal line approximation
            var prevMacd = CalculateEMA(closes, fast, index - 1) - CalculateEMA(closes, slow, index - 1);
            return macd - prevMacd;
        }

        #endregion
    }
}
