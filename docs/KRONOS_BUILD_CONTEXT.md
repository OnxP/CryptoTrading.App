# Kronos build context

Lean Claude Code context for the Kronos Binance forecasting service. Use with docs/LEAN_BUILD_CONTEXT.md (process) and docs/BUILD_SPEC.md (your full Kronos specification, saved from the planning conversation). Load spec sections by number only when a packet needs them.

## Fixed requirements

Do not change or guess these:

- Binance Spot; initial instrument BTCUSDT.
- Analysis timeframes 15m, 1h, 4h; forecast horizons 1d-5d; execution data 1m.
- Existing C# trading system owns trading, entries/exits, orders, portfolio and risk decisions.
- Python service, REST integration, Microsoft SQL Server as authoritative historical store.
- Windows, Intel Core Ultra 9-class CPU, ~71 GB RAM, NVIDIA RTX 5080; single local machine.
- Kronos-base and Kronos-Tokenizer-base loaded once; one service process, one serialized GPU worker.
- Fine-tuning supported later as single-process, single-GPU CUDA.
- Everything unspecified stays configurable; agents do not pick research answers (BUILD_SPEC §57).

The service answers "what does Kronos believe BTCUSDT will do over 1-5 days, and how uncertain is it", never "should I buy".

## Corrections to apply

These amend BUILD_SPEC where it is incomplete. Apply them as acceptance checks.

1. **Leakage assertion (§53).** `input timestamp <= creation timestamp` is insufficient for replay. Require every input candle's close time and availability time <= the forecast origin time, store origin time separately from generation time, and apply this in both live and historical paths.
2. **Candle semantics (§§9-12).** Fix open-vs-close timestamp meaning in docs/DATA_TIME_SEMANTICS.md before writing loaders. Store candle revisions; never overwrite history.
3. **1-minute scoring (§§28-30).** 1m candles locate a containing minute, not an exact touch. When high and low fall in the same minute, order is unknown: use the predeclared conservative convention or report bounds.
4. **Probabilities (§§18-19).** Return raw sample fractions, sample count and sampling settings. Label calibrated confidence unavailable until evaluation produces it. Changing temperature/top-p changes these numbers; version them.
5. **Target zones and timing (§§22, 28).** Define zone construction, arrival windows, early/late and never-reached before scoring runs.
6. **Job binding (§§13-24, 47).** Freeze candle snapshot hash, config version, checkpoint/tokenizer identity and sampling settings at enqueue. Config changes do not alter queued jobs. A late replay result never replaces the latest live-origin cached prediction.
7. **Pretraining contamination.** Record checkpoint provenance and known training cutoff. Historical replay over periods the checkpoint may have seen is evidence only with that caveat; prospective evaluation is required before any predictive-value claim.
8. **Overlapping forecasts (§31).** Report blocked/non-overlapping statistics alongside full-sample results; never present one as the other.
9. **Live-trading gate (§55).** "Only then should Kronos output influence live trading decisions" means eligible for consideration, not approval. Live influence additionally requires the runtime risk-policy approval, operational recovery tests and explicit confirmation in docs/CLAUDE_SETUP.md; the C# system remains execution authority throughout.
10. **External references (ORCHESTRATION §74).** Treat them as references to recheck on the installed versions, not verified facts. Model aliases, effort levels, status-line fields and billing behaviour were not verified from the planning environment.

## Milestones and packets

Each milestone: planner plans once, builder implements packets sequentially, reviewer checks per risk level. Critical review (L3) for timestamps, leakage, replay, scoring, checkpoint load/switch, GPU queue.

Numbering follows ORCHESTRATION §§54-61 (M0-M6). BUILD_SPEC §55 uses Milestones 1-5: its 1 = M0+M1 here, 2 = M2+M3, 3 = M4, 4 = M5, 5 = M6. Packet IDs match ORCHESTRATION §§55-61 exactly.

### M0 Environment (BUILD_SPEC §4; ORCHESTRATION §§4-14, 55)

- ENV-001 verify Windows prerequisites: git, Python 3.11, dotnet, nvidia-smi; record versions.
- ENV-002 install Claude Code; confirm models and billing behaviour on the account.
- ENV-003 configure usage capture; record whether rate-limit fields are present.
- ENV-004 create Python venv.
- ENV-005 install CUDA PyTorch; `torch.cuda.is_available()` true on RTX 5080.
- ENV-006 clone Kronos into third_party/, record upstream commit in docs/KRONOS_UPSTREAM.md.
- ENV-007 download Kronos-base and tokenizer.
- ENV-008 run official forecast once.

Gate: all checks pass or the failure is recorded. No service code before this gate.

### M1 Kronos core (BUILD_SPEC §§5-8, 19; ORCHESTRATION §56)

- KRONOS-001 model manager: load once, expose checkpoint identity.
- KRONOS-002 predictor abstraction.
- KRONOS-003 timeframe input adapter with configurable lookback.
- KRONOS-004 raw stochastic path extraction (not only aggregates).
- KRONOS-005 serialized GPU worker and job queue; job states queued/running/completed/failed/cancelled.
- KRONOS-006 forecast metadata record with origin time, generation time, snapshot hash, config version, checkpoint.
- KRONOS-007 model-loading tests.

L3 review: sampling correctness, no duplicate GPU models, job binding.

### M2 Data (BUILD_SPEC §§9-12, 29; ORCHESTRATION §57)

- DATA-001 SQL Server configuration and read-only connection test.
- DATA-002 candle loader with UTC normalisation.
- DATA-003 validation rules from §12 plus duplicates, gaps, out-of-order, revisions.
- DATA-004 time semantics document and tests; L3.
- DATA-005 rolling buffers per timeframe rebuilt from SQL at startup.
- DATA-006 1m evaluation loader.
- DATA-007 restart reconciliation.

### M3 REST (BUILD_SPEC §§11, 14-18, 20-25, 36-38, 50-51; ORCHESTRATION §58)

- API-001 FastAPI app.
- API-002 /health, /ready.
- API-003 POST /api/v1/candles/{symbol}/{timeframe}; only closed candles schedule prediction.
- API-004 GET /api/v1/predictions/{symbol}/{timeframe} cached, non-blocking; stale metadata on failure; combined endpoint without invented weights.
- API-005 POST /api/v1/predict/... returning 202 and job ID.
- API-006 GET /api/v1/jobs/{jobId}.
- API-007 runtime config GET/PUT per timeframe; config versioning; hot-change vs reload classification.
- API-008 GPU and model status endpoints.
- API-009 persistence tables (§25).
- API-010 C# integration test client against the running service.

Bind to localhost by default; no Binance keys or trading credentials in this service.

### M4 Research infrastructure (BUILD_SPEC §§19, 29, 31-35; ORCHESTRATION §59)

- RESEARCH-001 historical replay runner with as-of candle retrieval; L3.
- RESEARCH-002 replay resume and progress persistence.
- RESEARCH-003 forecast cache.
- RESEARCH-004 stochastic path persistence.
- RESEARCH-005 GPU benchmark for sampleCount 1-32 across 480/120/30 steps; record latency and peak VRAM.
- RESEARCH-006 1m target evaluator with ambiguity handling.

### M5 Evaluation (BUILD_SPEC §§26-31, 48-49; ORCHESTRATION §60)

- EVAL-001 direction accuracy per horizon.
- EVAL-002 magnitude error and correlation.
- EVAL-003 price-zone hit.
- EVAL-004 timing tolerance.
- EVAL-005 MFE/MAE.
- EVAL-006 confidence calibration from sample fractions.
- EVAL-007 overlapping-sample controls.
- EVAL-008 15m vs 1h vs 4h comparison.

Gate: freeze zero-shot baseline. Any claim of predictive value needs critical review and the contamination caveat.

### M6 Fine-tuning (BUILD_SPEC §§40-48; ORCHESTRATION §61)

Only after M5 gate. FT-001 dataset snapshot/hash, FT-002 chronological split with embargo, FT-003/004 single-GPU tokenizer then predictor training, FT-005 save/terminate/reload/verify test (§44), FT-006 model registry, FT-007 zero-shot vs fine-tuned on identical untouched period. Never overwrite upstream checkpoints. Training is exclusive on the GPU.

## First acceptance test

BUILD_SPEC §56, sixteen steps from service start to restart recovery. Run with mocked inference first (M2-M3), then real Kronos (after M1 passes on hardware).

## Agent routing

| Packet class | Builder | Review |
|---|---|---|
| ENV, API DTOs/config, tests | capable coding model, default effort | short read-only |
| GPU worker, SQL, replay, evaluation | capable coding model, raised effort on blockers | standard, L3 where listed |
| Time semantics, leakage, scoring design, fine-tuning method | planner defines; builder implements | critical |

Independent validation (BUILD_SPEC §54 Agent 6) is the critical reviewer role, run per L3 packet, not a standing agent.

## Boundaries

- Forecasts are information; C# decides trades.
- No trading credentials, order code or risk-limit changes in this service.
- No deployment, checkpoint switch or production migration from build success.
- Local orchestrator rules from docs/ORCHESTRATION.md apply (usage limits, billing guard, worktrees, handoffs); confirm Claude Code model names, effort levels and billing behaviour on the actual installation before unattended runs.

## Token rules

- Load BUILD_SPEC sections by number per packet; never the whole file.
- One packet, one behaviour, under 10 files.
- Tests and benchmarks run locally; paste only the decisive output lines.
- No re-planning of completed milestones.

## Prompts

Planner:

> Read docs/KRONOS_BUILD_CONTEXT.md and docs/LEAN_BUILD_CONTEXT.md. Plan milestone <N> only. Inspect existing code relevant to it, cite paths, and produce packets in the packet format with acceptance checks that include the applicable corrections. Ask only blocking questions.

Builder:

> Read docs/KRONOS_BUILD_CONTEXT.md and packet <ID>. Load only the BUILD_SPEC sections it names. Implement within allowed files, run acceptance checks, show decisive output, stop on stop conditions, end with the handoff format.

Critical reviewer:

> Read docs/KRONOS_BUILD_CONTEXT.md corrections and packet <ID>. Review the diff read-only for leakage, timestamp semantics, job binding, sampling correctness and queue concurrency. Return PASS or FAIL with at most 5 findings, each with file:line and failure caused.
