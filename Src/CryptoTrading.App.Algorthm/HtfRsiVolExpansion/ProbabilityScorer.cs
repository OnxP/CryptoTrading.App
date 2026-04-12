using CryptoTrading.App.Algorithm.RegimeBased;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Computes a 0-100 probability score for each trade at entry.
    /// Used for monitoring/logging only - does not affect position sizing.
    /// </summary>
    public static class ProbabilityScorer
    {
        /// <summary>
        /// Score a trade setup based on 7 factors.
        /// </summary>
        public static int Score(
            double htfRsi,
            double volExpansionRatio,
            double rsi15M,
            TradeDirection direction,
            decimal candleOpen,
            decimal candleClose,
            decimal candleVolume,
            decimal avgVolume20,
            double? ema4H8,
            double? ema4H21,
            double recentWinRate)
        {
            int score = 0;

            // 1. 4H RSI strength (distance from 50) - max 25 pts
            double rsiDistance = Math.Abs(htfRsi - 50);
            if (rsiDistance >= 25) score += 25;
            else if (rsiDistance >= 20) score += 20;
            else if (rsiDistance >= 15) score += 15;
            else score += 5;

            // 2. Vol expansion ratio (ATR now / ATR 20 ago) - max 20 pts
            if (volExpansionRatio >= 1.5) score += 20;
            else if (volExpansionRatio >= 1.3) score += 15;
            else if (volExpansionRatio >= 1.2) score += 10;

            // 3. 15M RSI alignment with direction - max 15 pts
            // Counter-trend RSI = RSI opposing the trade direction
            // For longs: counter-trend distance = 50 - RSI (positive when oversold)
            // For shorts: counter-trend distance = RSI - 50 (positive when overbought)
            double counterTrendDistance = direction == TradeDirection.Long
                ? 50 - rsi15M
                : rsi15M - 50;
            if (counterTrendDistance > 10) score += 15;
            else if (counterTrendDistance > 0) score += 10;
            else if (counterTrendDistance > -5) score += 5;

            // 4. Candle direction matches bias - max 10 pts
            bool candleBullish = candleClose > candleOpen;
            bool matchesBias = (direction == TradeDirection.Long && candleBullish)
                            || (direction == TradeDirection.Short && !candleBullish);
            if (matchesBias) score += 10;

            // 5. Volume vs 20-period average - max 10 pts
            if (avgVolume20 > 0)
            {
                decimal volRatio = candleVolume / avgVolume20;
                if (volRatio >= 1.5m) score += 10;
                else if (volRatio >= 1.0m) score += 7;
                else score += 3;
            }
            else
            {
                score += 3;
            }

            // 6. 4H EMA8 vs EMA21 alignment - max 10 pts
            if (ema4H8.HasValue && ema4H21.HasValue)
            {
                bool emaAligned = (direction == TradeDirection.Long && ema4H8.Value > ema4H21.Value)
                              || (direction == TradeDirection.Short && ema4H8.Value < ema4H21.Value);
                if (emaAligned) score += 10;
            }

            // 7. Recent win rate (last 5 trades) - max 10 pts
            if (recentWinRate >= 0.8) score += 10;
            else if (recentWinRate >= 0.6) score += 7;
            else if (recentWinRate >= 0.4) score += 4;

            return Math.Min(score, 100);
        }
    }
}
