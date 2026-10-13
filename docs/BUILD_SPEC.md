# Kronos Binance Forecasting Service — Agent Build Specification

Saved verbatim from Ankur's planning conversation. Authoritative fixed requirements for the Kronos service. Corrections and acceptance amendments live in KRONOS_BUILD_CONTEXT.md; this file is not edited to apply them.

## 1. Objective

Build a local Windows service that uses Kronos to produce multi-timeframe BTCUSDT spot forecasts for an existing C# trading system.

The initial objective is forecast research and prediction quality, not autonomous trading.

The system must determine whether Kronos can reliably predict:

- Market direction
- Relative size of future moves
- Approximate future price zones
- Approximate time at which those zones are reached
- Forecast confidence and uncertainty

Initial forecasting horizons: 1 day, 2 days, 3 days, 4 days, 5 days.

Analysis timeframes: 15 minutes, 1 hour, 4 hours.

The existing trading system executes using 1-minute market data.

Kronos must expose predictions to that system through REST.

## 2. Fixed Requirements

These requirements must not be changed or guessed by agents.

- Market: Binance Spot
- Initial instrument: BTCUSDT
- Analysis timeframes: 15m, 1h, 4h
- Forecast horizons: 1d, 2d, 3d, 4d, 5d
- Trading/execution data: 1m
- Existing system: C#
- Integration: REST API
- Database: Microsoft SQL Server
- Historical data: approximately 12 months currently; additional 1h/4h history can be obtained
- Machine: Windows, Intel Core Ultra 9-class CPU, ~71 GB RAM, NVIDIA RTX 5080
- Fine-tuning: required as a supported capability
- Initial deployment: single local machine

Anything not specified here must remain configurable rather than being invented by an agent.

## 3. High-Level Architecture

```
                   C# TRADING SYSTEM
                          |
          +---------------+----------------+
          |               |                |
       candles        predictions       config
          |               |                |
          v               v                v
 +----------------------------------------------+
 |             KRONOS REST SERVICE              |
 |                  Python                      |
 |                                              |
 |   API                                        |
 |    |                                         |
 |    +---- Candle Manager                      |
 |    +---- Configuration Manager               |
 |    +---- Forecast Scheduler                  |
 |    +---- Model Manager                       |
 |    +---- Forecast Evaluator                  |
 |    +---- Prediction Store                    |
 |                    |                         |
 |                    v                         |
 |               GPU JOB QUEUE                  |
 |                    |                         |
 |              RTX 5080 / CUDA                 |
 +--------------+-------------------------------+
                |
                v
        MICROSOFT SQL SERVER
```

The C# system remains responsible for: actual trading, entries and exits, execution, order management, 1-minute trading logic, portfolio and risk decisions.

Kronos provides forecast information, not trade commands.

## 4. Windows Deployment

Use native Windows initially.

Recommended stack: Python 3.11 or supported equivalent, PyTorch CUDA, FastAPI, Uvicorn, Pydantic, SQLAlchemy or pyodbc, Microsoft ODBC Driver for SQL Server, pandas and/or polars, NumPy, Kronos fork.

Create an isolated environment:

```
kronos-service/
    .venv/
```

Do not modify the machine-wide Python installation.

## 5. Process Architecture

Initially use one service process and one GPU worker.

Do not allow multiple REST requests to perform GPU inference concurrently.

```
REST requests
     |
     v
Forecast Queue
     |
     v
Single GPU Worker
     |
     v
Kronos
```

This avoids: duplicated models, unnecessary VRAM consumption, CUDA contention, simultaneous 480-step forecasts, unpredictable inference latency.

## 6. Model Architecture

Initially load Kronos-base and Kronos-Tokenizer-base once.

The same zero-shot model handles 15m, 1h, 4h using different inputs and forecast configurations.

Do not initially load three copies of the same checkpoint.

```
                  Kronos-base
                       |
              shared GPU model
                       |
         +-------------+-------------+
         |             |             |
         v             v             v
        15m            1h            4h
```

Separate models become necessary only if research later produces separate fine-tuned checkpoints: BTCUSDT-15m-finetuned, BTCUSDT-1h-finetuned, BTCUSDT-4h-finetuned.

Even then, inference should remain serialized through the GPU queue initially.

## 7. Forecast Lengths

15-minute: 1d = 96 bars, 2d = 192, 3d = 288, 4d = 384, 5d = 480

1-hour: 1d = 24, 2d = 48, 3d = 72, 4d = 96, 5d = 120

4-hour: 1d = 6, 2d = 12, 3d = 18, 4d = 24, 5d = 30

Agents must not assume that the 480-step 15m prediction is reliable. Its accuracy must be measured independently.

## 8. Historical Inputs

Each timeframe owns an independent rolling input buffer.

Lookback must be configurable. Test at least 128, 256, 400, 512 where supported by the selected Kronos model.

Do not assume maximum context produces the best forecast.

## 9. Market Data

SQL Server is the authoritative historical data source.

Expected minimum candle fields: Symbol, Timeframe, OpenTime, Open, High, Low, Close, Volume.

Additional available Binance fields may later be supported.

Internally normalize timestamps to UTC.

## 10. Startup Behaviour

```
START
  |
  v
connect SQL Server
  |
  v
retrieve required historical BTCUSDT candles
  |
  v
validate candles
  |
  v
build 15m / 1h / 4h rolling buffers
  |
  v
load tokenizer
  |
  v
load Kronos
  |
  v
test CUDA
  |
  v
service becomes READY
```

Do not rely on REST candle messages to reconstruct state after restart.

## 11. Candle REST API

Support:

```
POST /api/v1/candles/{symbol}/{timeframe}
```

Example:

```json
{
  "timestamp": "2026-08-12T12:15:00Z",
  "open": 118200.1,
  "high": 118450.5,
  "low": 118100.0,
  "close": 118390.25,
  "volume": 123.456,
  "isClosed": true
}
```

Supported initial timeframe values: 15m, 1h, 4h.

Only a closed candle may automatically trigger prediction processing.

## 12. Candle Validation

Reject invalid candles where: high < open, high < close, low > open, low > close, low > high, open <= 0, high <= 0, low <= 0, close <= 0, volume < 0.

Also detect: duplicates, missing intervals, incorrect timestamps, out-of-order candles, candle revisions.

Never silently corrupt the rolling input buffer.

## 13. Prediction Scheduling

A new closed 15m candle: updates 15m state -> schedules 15m prediction.

A closed hourly boundary: updates 1h state -> schedules 1h prediction.

A closed 4h boundary: updates 4h state -> schedules 4h prediction.

If all three become due simultaneously:

```
GPU QUEUE

1. 15m
2. 1h
3. 4h
```

Ordering must be configurable.

## 14. Prediction Retrieval Endpoints

Provide:

```
GET /api/v1/predictions/{symbol}/15m
GET /api/v1/predictions/{symbol}/1h
GET /api/v1/predictions/{symbol}/4h
```

These endpoints return the latest completed cached prediction. They must not normally block waiting for GPU inference.

## 15. Combined Endpoint

Provide:

```
GET /api/v1/predictions/{symbol}/combined
```

Return all timeframe forecasts together. Do not initially create arbitrary multi-timeframe weights.

Example:

```json
{
  "symbol": "BTCUSDT",
  "15m": { "direction": "up", "confidence": 0.64 },
  "1h":  { "direction": "up", "confidence": 0.74 },
  "4h":  { "direction": "up", "confidence": 0.81 }
}
```

Research should later determine how these should be combined.

## 16. Explicit Inference Endpoint

Provide:

```
POST /api/v1/predict/{symbol}/{timeframe}
```

Options should include: force, horizons, sampleCount override.

The endpoint may return `202 Accepted` and `jobId` when inference is queued.

## 17. Prediction Jobs

Provide:

```
GET /api/v1/jobs/{jobId}
```

Possible states: queued, running, completed, failed, cancelled.

This prevents long inference operations from blocking HTTP requests.

## 18. Forecast Response

For every horizon return at least: predicted direction, expected return %, median return %, probability up, probability down, expected price, upper/lower forecast range, forecast uncertainty, predicted high, predicted low, forecast generation timestamp, input candle timestamp, model name, checkpoint version, configuration version.

Example:

```json
{
  "symbol": "BTCUSDT",
  "timeframe": "1h",
  "forecastTime": "2026-08-12T13:00:00Z",
  "model": { "name": "kronos-base", "checkpoint": "zero-shot" },
  "configVersion": 24,
  "currentPrice": 118390.25,
  "horizons": {
    "1d": { "expectedReturnPct": 1.4, "medianReturnPct": 1.2, "probabilityUp": 0.71, "expectedPrice": 120047.7, "confidence": 0.69 },
    "3d": { "expectedReturnPct": 3.6, "probabilityUp": 0.76, "expectedPrice": 122652.3, "confidence": 0.73 },
    "5d": { "expectedReturnPct": 4.9, "probabilityUp": 0.70, "expectedPrice": 124191.4, "confidence": 0.61 }
  }
}
```

## 19. Preserve Stochastic Forecast Paths

Do not discard individual Kronos samples.

Store forecast path 1 ... forecast path N.

Then derive: mean, median, standard deviation, 10th/25th/75th/90th percentile, probability price increases, probability target reached, directional consensus, forecast dispersion.

The standard Kronos predictor may need modification or wrapping to expose these paths rather than only returning the aggregated prediction.

## 20. Runtime Configuration API

Provide:

```
GET /api/v1/config
GET /api/v1/config/15m
GET /api/v1/config/1h
GET /api/v1/config/4h
PUT /api/v1/config/15m
PUT /api/v1/config/1h
PUT /api/v1/config/4h
```

Configuration changes must not require restarting the service unless the changed property genuinely requires model reinitialization.

## 21. Timeframe Configuration

Each timeframe independently controls: enabled, model checkpoint, lookback, forecast horizons, sample count, sample batch size, temperature, top-p, price tolerances, time tolerances, automatic prediction, prediction priority.

Example:

```json
{
  "enabled": true,
  "lookback": 512,
  "forecastDays": [1, 2, 3, 4, 5],
  "sampling": {
    "temperature": 1.0,
    "topP": 0.9,
    "sampleCount": 8,
    "sampleBatchSize": 4
  }
}
```

Sampling values must remain experimental and configurable.

## 22. Tolerances

Tolerances are not global. They must be configurable by TIMEFRAME x FORECAST HORIZON.

Each combination contains: price tolerance %, early time tolerance, late time tolerance.

Example only:

```json
{
  "horizon": "3d",
  "priceTolerancePct": 1.0,
  "timeTolerance": { "earlyMinutes": 720, "lateMinutes": 720 }
}
```

Do not hard-code the example value.

## 23. Configuration Versioning

Every configuration update creates a new ConfigVersion. Every generated forecast stores that version. Never overwrite historical configurations.

```
Prediction -> ConfigVersion -> exact configuration used
```

## 24. Configuration Safety

Changes fall into two classes.

Hot-change: tolerances, enabled, sampling count, forecast horizon, temperature, top-p.

Model reload required: checkpoint, model architecture, tokenizer.

The API must tell the caller whether a reload occurred.

## 25. Prediction Persistence

Store predictions persistently.

Suggested tables: KronosPrediction, KronosPredictionHorizon, KronosForecastPath, KronosEvaluation, KronosConfig, KronosModel, KronosInferenceRun.

Do not rely only on log files.

## 26. Prediction Evaluation

The primary goal is not exact candle reconstruction.

Evaluate direction separately for 1D, 2D, 3D, 4D, 5D.

Compare predicted future return sign versus actual future return sign.

## 27. Relative Move

Store: predicted return %, actual return %, absolute difference, relative difference.

Also measure correlation between predicted magnitude and actual magnitude.

## 28. Target-Zone Scoring

For every prediction: target price +/- configured price tolerance.

Use later market data to determine whether the zone was reached.

Store: target reached = true/false, first timestamp reached, time error.

## 29. Use 1-Minute Data for Evaluation

Use 1-minute candle data to determine: exact first target-zone touch, maximum favourable excursion, maximum adverse excursion, highest price achieved, lowest price achieved, high before low?, low before high?, time to target, time to maximum favourable excursion.

This is separate from the Kronos inference timeframe.

## 30. Path-Quality Evaluation

For every forecast determine: Was direction ultimately correct? How far did price initially move against forecast? How far did it eventually move with forecast? Was predicted target reached? Was it reached early? Was it reached late? Did direction reverse before target? Was Kronos magnitude approximately correct?

## 31. Overlapping Forecast Warning

A new 15m prediction every 15 minutes creates heavily overlapping 1-5 day outcomes.

Agents must not treat each forecast as an independent statistical observation.

Evaluation must include all forecasts AND non-overlapping/blocked samples.

Use time-series-aware confidence intervals.

## 32. Zero-Shot Phase

Before fine-tuning: run Kronos-base zero-shot, generate historical forecasts, score all 1-5D horizons, compare 15m, 1h and 4h, evaluate stochastic confidence, identify failure regimes.

Save and freeze this baseline before fine-tuning.

## 33. Historical Forecast Runner

Provide an offline process capable of recreating what would have happened historically.

```
for timestamp in historical_test:
    retrieve candles available AT THAT TIME
    generate Kronos forecast
    save prediction
    move forward
```

Future candles must never enter the model input.

## 34. Longer History

Obtain as much historical BTCUSDT data as practical for 1h and 4h.

Longer history is particularly valuable for evaluating 3D, 4D, 5D predictions over multiple crypto market regimes.

Do not use future test data during parameter selection or fine-tuning.

## 35. GPU Benchmarking

Before choosing production sampleCount, benchmark the actual RTX 5080.

Test 15m: 480-step forecast; 1h: 120-step forecast; 4h: 30-step forecast.

For sampleCount = 1, 2, 4, 8, 16, 32.

Record: latency, peak CUDA allocated memory, peak CUDA reserved memory, CPU memory, success/failure, forecast throughput.

Do not estimate VRAM requirements from checkpoint size alone.

## 36. GPU Status Endpoint

Provide:

```
GET /api/v1/system/gpu
```

Return: GPU name, CUDA available, total VRAM, allocated VRAM, reserved VRAM, peak allocation, loaded checkpoint, active inference, queued jobs.

## 37. Model Status Endpoint

Provide:

```
GET /api/v1/models/status
```

Return status independently for 15m, 1h, 4h including: checkpoint, loaded, last prediction timestamp, last inference duration, last error, configuration version.

## 38. Health Endpoints

Provide:

```
GET /health
GET /ready
```

/health means: service process alive.

/ready means: SQL available, required historical context loaded, CUDA working, Kronos loaded, GPU worker running.

## 39. Logging

Structured logs must include: timestamp, request ID, forecast ID, symbol, timeframe, config version, model checkpoint, GPU execution time, errors.

No important experimental information should exist only in console output.

## 40. Fine-Tuning on Windows

The Windows implementation must use a single-process, single-GPU CUDA path rather than depending on multi-GPU DistributedDataParallel.

Required design: single Python process, single CUDA device, no required DistributedDataParallel, no required NCCL process group, ordinary `model.to("cuda")`, ordinary optimizer, mixed precision where validated, gradient accumulation, checkpoint save/reload.

## 41. Fine-Tuning Stages

Support two stages separately: tokenizer fine-tuning, predictor fine-tuning.

Never overwrite zero-shot checkpoints.

```
models/
  upstream/
    kronos-base/
  finetuned/
    btcusdt/
      15m/
      1h/
      4h/
```

## 42. Initial Fine-Tuning Experiments

Eventually compare:

- A: zero-shot Kronos-base
- B: BTCUSDT-specific Kronos-base
- C: Binance-USDT-general Kronos-base, if sufficient additional Binance data becomes available.

Do not assume BTC-only fine-tuning is superior.

## 43. Fine-Tuning by Timeframe

Evaluate whether separate training helps: BTCUSDT 15m checkpoint, BTCUSDT 1h checkpoint, BTCUSDT 4h checkpoint.

Then compare against one shared crypto-fine-tuned model.

The final architecture must support either configuration.

## 44. Training Validation

A completed training process is not enough.

Mandatory acceptance test:

```
train -> save checkpoint -> terminate Python -> start fresh process
      -> load saved checkpoint -> run fixed validation input -> verify expected output
```

## 45. Fine-Tuning Resource Strategy

With one GPU: never train 15m + 1h + 4h simultaneously.

Instead: train 15m, finish; train 1h, finish; train 4h, finish.

Use mixed precision, gradient accumulation, checkpointing where benchmarking demonstrates that they work correctly.

## 46. Model Registry

Each checkpoint gets metadata: model ID, base checkpoint, training timeframe, symbols, training start/end, validation start/end, hyperparameters, dataset hash, Git commit, training loss, validation loss, creation date.

REST configuration should refer to model ID rather than arbitrary file paths.

## 47. Hot Model Switching

Eventually support:

```
PUT /api/v1/config/1h
{ "modelId": "btcusdt-1h-v3" }
```

Model manager then safely: drains GPU queue, loads checkpoint, runs health inference, marks checkpoint active.

If loading fails, automatically revert to the previous model.

## 48. Comparison Framework

For every timeframe and horizon compare zero-shot vs fine-tuned using the identical untouched evaluation period.

Report: direction accuracy, return correlation, magnitude error, target-zone hit rate, time-to-target error, forecast uncertainty calibration.

Fine-tuning is accepted only if improvement survives multiple periods.

## 49. Research Dashboard Data

Even if no UI is built initially, persist enough data to later plot: prediction vs actual price, predicted direction accuracy, accuracy by horizon, accuracy by timeframe, accuracy by confidence, accuracy by volatility regime, target-zone hit rate, target timing distribution, forecast uncertainty, zero-shot vs fine-tuned.

## 50. Security

Initially bind to localhost by default. Make external network binding an explicit configuration option.

The Kronos service does not require Binance API keys because market data is supplied through SQL and REST.

Do not place trading credentials inside this service.

## 51. Failure Handling

If Kronos fails: do not fabricate a prediction.

Return stale-state metadata or a clear failure.

```json
{
  "status": "stale",
  "lastSuccessfulPrediction": "...",
  "ageSeconds": 1143
}
```

The C# system decides whether stale information remains usable.

## 52. Required Test Cases

Automated tests must include: valid candle accepted, invalid candle rejected, duplicate candle handled, missing candle detected, out-of-order candle handled, REST serialization, SQL reload, configuration update, configuration version increment, 15m scheduling, 1h scheduling, 4h scheduling, GPU queue serialization, prediction caching, model reload, checkpoint rollback, no future data leakage, target-zone scoring, time-tolerance scoring, 1m path evaluation.

## 53. Critical Leakage Test

For every historical prediction:

```
latest model input timestamp <= forecast creation timestamp
```

This must be an automated assertion. No exceptions.

## 54. Agent Decomposition

Agent 1 — Kronos / Windows: Windows environment, CUDA verification, Kronos loading, single-GPU inference, stochastic-path extraction, VRAM benchmark, model manager.

Agent 2 — SQL / Data: SQL Server adapter, historical candle loading, validation, rolling buffers, 1m evaluation data, timeframe consistency.

Agent 3 — REST Service: FastAPI service, candle endpoints, prediction endpoints, config endpoints, job endpoints, health/status endpoints.

Agent 4 — Forecasting Research: historical replay, direction scoring, magnitude scoring, price tolerance, time tolerance, target-zone analysis, forecast confidence, multi-timeframe comparison.

Agent 5 — Fine-Tuning: Windows single-GPU training adaptation, dataset preparation, tokenizer training, predictor training, checkpointing, model registry.

Agent 6 — Independent Validation: responsible for trying to break the project. Inspect: future leakage, timestamp mistakes, overlapping forecast statistics, incorrect timeframe construction, incorrect target evaluation, config/version errors, GPU concurrency problems, checkpoint corruption, overfitting. This agent should not optimize forecast performance.

## 55. Implementation Order

Milestone 1: Windows environment works, CUDA works, Kronos-base inference works, BTCUSDT SQL data loads, single historical prediction works.

Milestone 2: 15m / 1h / 4h inference, REST API, candle push, prediction cache, runtime config, GPU queue.

Milestone 3: historical forecast generator, 1-5D scoring, 1m target analysis, stochastic forecasts, GPU benchmark.

Milestone 4: multi-timeframe comparison, confidence analysis, target timing analysis, parameter sensitivity.

Milestone 5: Windows single-GPU fine-tuning, checkpoint registry, zero-shot vs fine-tuned comparison.

Only then should Kronos output influence live trading decisions.

## 56. First Acceptance Test

The first operational demonstration should be:

1. Start Windows Kronos service.
2. Connect SQL Server.
3. Load BTCUSDT history.
4. Load Kronos-base onto RTX 5080.
5. C# sends a completed 15m candle.
6. API accepts it.
7. Prediction job enters queue.
8. Kronos produces 1-5D forecast.
9. Result is persisted.
10. C# requests GET /api/v1/predictions/BTCUSDT/15m.
11. Valid JSON prediction is returned.
12. Configuration is modified through REST.
13. New prediction records new ConfigVersion.
14. Service restarts.
15. State is reconstructed from SQL.
16. Latest prediction remains available.

If all sixteen steps work, the service architecture is functioning correctly.

## 57. Decisions Agents Must NOT Make Themselves

Agents must not arbitrarily decide: best tolerance, best forecast horizon, best lookback, best sample count, best temperature, best top-p, best timeframe weighting, whether 15m is better than 1h, whether fine-tuning improves Kronos, whether separate timeframe models are required, whether a forecast is good enough for live trading.

Those are research questions. Implement them as configurable experiments.

## 58. Core Project Principle

The service should answer:

> What does Kronos currently believe BTCUSDT is likely to do over the next 1–5 days, at several market scales, and how uncertain is that forecast?

It should not answer:

> Should I buy BTC now?

The existing C# trading system remains responsible for converting forecast information into trading decisions.
