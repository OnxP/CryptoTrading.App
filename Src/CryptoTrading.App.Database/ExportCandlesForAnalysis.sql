-- Export BTCUSDT candlestick data for algorithm analysis
-- Covers Jul 2025 - Feb 2026 on 4H and 15M timeframes
-- CandlestickInterval enum: Minute=0, Minutes_3=1, Minutes_5=2, Minutes_15=3,
--   Minutes_30=4, Hour=5, Hours_2=6, Hours_4=7

-- 4H candles (regime detection)
SELECT
    CloseTime,
    OpenTime,
    [Open],
    High,
    Low,
    [Close],
    Volume,
    QuoteAssetVolume,
    NumberOfTrades,
    Symbol,
    Interval
FROM [dbo].[CandleStickDbs]
WHERE Symbol = 'BTCUSDT'
  AND Interval = 7           -- Hours_4
  AND CloseTime >= '2025-07-01'
  AND CloseTime < '2026-04-01'
ORDER BY CloseTime;

-- 15M candles (setup detection)
SELECT
    CloseTime,
    OpenTime,
    [Open],
    High,
    Low,
    [Close],
    Volume,
    QuoteAssetVolume,
    NumberOfTrades,
    Symbol,
    Interval
FROM [dbo].[CandleStickDbs]
WHERE Symbol = 'BTCUSDT'
  AND Interval = 3           -- Minutes_15
  AND CloseTime >= '2025-07-01'
  AND CloseTime < '2026-04-01'
ORDER BY CloseTime;
