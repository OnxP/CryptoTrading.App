# options-research — Milestone M4 plan (planned 2026-10-25; all packets COMPLETE 2026-10-26, see docs/planning/OPTIONS_M4_TEST_REPORT.md)

Builds on M3 (docs/planning/OPTIONS_M3_PLAN.md, PR #84). Process: docs/LEAN_BUILD_CONTEXT.md. Requirements: docs/CLAUDE_SETUP.md "Quant research" (compare simple rules, separately identified model outputs); the regime R&D finding that volume is what makes a regime model work (markov-regime/markov_regime.py, OnxP/ClaudePrompts RESULTS.md).

## Decisions (Ankur, 2026-10-25)

- Data source: **Yahoo Finance public endpoints** (chart v8 for daily candles, the crumb-gated equity screener for the universe). Unofficial, no key, no account. Used through the existing `HttpClient` seam; tests replay docs-shaped cassettes with FICTIONAL bars.
- Universe: **low-priced US equities sorted by day volume**, top 50. Working capital is about $200, so "low priced" defaults to `1 < price < 10` (configurable). **No sizing** (unchanged boundary: `[risk] account_limits_configured = false`); the screen never computes shares to buy.
- Model: **Gaussian HMM in the standard library** (numpy/pandas hang on the build machine; the M3 vol work is stdlib too). Features per day: log return, log relative volume (vs 20-day median), log high/low range; trailing 5-day means; z-scored, winsorised at 5 sd. Three states, sticky Dirichlet prior on self-transitions so regimes persist for weeks, seeded restarts, deterministic.
- Turning points are **model diagnostics, not forecasts**: a fresh switch (FRESH), a weakening posterior (WEAKENING), or a run far past its typical duration (OVERDUE). The market benchmark (SPY) is shown first.
- Still open from M2/M3: Tradier sandbox token, risk limits, model/API budget, Chronos keep/drop; live news API deferred.

## Milestone goal

`options-research screen` pulls three years of daily candles for the top-50 low-priced, high-volume US equities plus SPY, fits a regime model per symbol, and prints one table: price, volume, market cap, sector/industry (company profile), current regime, days in regime, P(current), one-step switch hazard, posterior drift, runner-up regime, turning-point flag. Every number from code; an append-only `data/screens.jsonl` ledger; ASCII output.

Done means: the live run completes for 50 symbols in a few minutes, the replay run on docs cassettes is deterministic under test with sockets blocked, a symbol with too little history is UNAVAILABLE (never guessed), a delisted symbol is an ERR row (never aborts the screen), and the text contains no buy/sell wording.

## Design decisions

1. **HTTP seam gains POST** (`HttpClient.post`) for the screener; the crumb-gated `quoteSummary` profile supplies sector, industry and country per symbol. Cassette keys for GET are byte-identical to M2 (asserted) so Tradier/EDGAR cassettes still replay; POST keys include a body hash. The Yahoo `crumb` query param is volatile: excluded from keys and stored redacted. Cookies live only in the httpx jar for the run.
2. **Features** (`regime/features.py`): daily log return on split-adjusted close; log(volume / median of the previous 20 volumes); log(high/low) with a guard for inverted or zero ranges; trailing `feature_window` (5) means; z-score over the sample; winsor 5 sd. Warm-up rows are dropped and counted.
3. **HMM** (`regime/hmm.py`): diagonal Gaussian emissions, scaled forward-backward (per-step shift so no underflow), Baum-Welch with `sticky_prior` pseudo-count (20) on the diagonal (MAP), k-means++ init from a seeded RNG, best of `restarts` (3) by data log-likelihood, variance floor. ~0.2 s per 750-bar fit in pure Python.
4. **Labels** (`regime/assessment.py`): trend by the state's posterior-weighted mean daily return against `trend_threshold_pct` (0.05%/day): BULL / BEAR / FLAT; then the state's rank by mean daily range among the symbol's own states: -V widest, -M middle, -Q narrowest. Labels are per symbol, descriptive, never cross-symbol comparable.
5. **Turning point**: current regime = argmax smoothed posterior at the last bar; run length from the argmax path; `p_switch_next_day = sum_k P_k (1 - A_kk)`; drift = change in P(current) over `drift_days` (5); FRESH if run <= `fresh_days` (3), WEAKENING if P(current) < `weak_posterior` (0.70) or drift < `drift_threshold` (-0.25), OVERDUE if run > `overdue_ratio` (1.5) x expected duration with a firm posterior, else STABLE. `turning_point` = FRESH or WEAKENING. Six or more switches in 60 bars adds a "choppy" note.
6. **Screen** (`regime/screen.py`, CLI `screen`): Yahoo screener (`intradayprice` band, region us, exchanges NMS/NYQ/ASE, sort `dayvolume` desc, size top) -> chart per symbol -> assess; rows stay in the screener's order (no ranking by attractiveness). Benchmark first. `ScreenRecord` appended to `data/screens.jsonl`. `--replay-dir` serves cassettes (tests), `--record-dir` records live responses. Exit 5 when Yahoo is unavailable.
7. Not in M4: sizing, backtests of regime->strategy rules, Chronos/Kronos comparison, rolling refits, cross-symbol regime comparability, fitted covariance (diagonal only), intraday bars.

## Ordered packets

| ID | Title | Risk | Depends on |
|---|---|---|---|
| OPT-301 | HTTP seam POST + volatile params; Yahoo connector (chart, crumb, screener); `YahooBarSource`; docs cassettes generator | L2 | M3 |
| OPT-302 | Stdlib Gaussian HMM (`regime/hmm.py`) and features (`regime/features.py`) | L3 (numbers) | - |
| OPT-303 | Regime assessment, labels and turning-point diagnostics (`regime/assessment.py`) with receipts | L3 (numbers) | 302 |
| OPT-304 | Screen orchestration, renderer, ledger and `screen` CLI command; config `[yahoo] [regime] [screen]` | L2 | 301, 303 |
| OPT-305 | M4 tests, docs cassettes, OPTIONS_M4_TEST_REPORT.md, README | L2 | 304 |

## Human steps (Ankur)

- None required for the screen (no key). Yahoo may change its unofficial endpoints; `scripts/record_cassettes.py --provider yahoo --record` refreshes cassettes when that happens.
- Unchanged: Tradier sandbox token; risk limits before any sizing milestone.

## Out of M4

Position sizing, regime-to-strategy mapping and backtests, fitted HAR/GARCH or Chronos comparison, IV history, news API, intraday regimes.
