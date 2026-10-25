# options-research — Milestone M3 plan (planned 2026-10-25; all packets COMPLETE 2026-10-25, see docs/planning/OPTIONS_M3_TEST_REPORT.md)

Builds on M2 (docs/planning/OPTIONS_M2_PLAN.md, all packets complete, PR #83). Process: docs/LEAN_BUILD_CONTEXT.md. Requirements: docs/CLAUDE_SETUP.md "Pricing specialist" (spot/IV/skew/time scenarios), "Risk reviewer" (bad assumptions, regime breaks), "Quant research" (compare simple rules, indicators, separately identified Chronos/Kronos forecasts).

## Decisions (Ankur, 2026-10-25)

- Live news API **deferred**: future feature; the OPT-109 connector stays unconfigured. News evidence remains fixture/EDGAR based.
- Still open: Tradier sandbox token (live recording), options account risk limits (sizing), model/API budget, Chronos keep/drop.

## Milestone goal

Give the advisory section an explicit **volatility context** so a reader can see what the market charges for vol versus what the underlying has realised: realised-vol estimators from daily OHLC, a simple non-fitted forecast with a range, the chain's at-the-money implied vol and a skew proxy, and the premium between them. Code computes every number as a receipt. No probabilities, no forecast of direction, no model calls. The risk reviewer gains one check: an implied-vol premium above a configured threshold is a REVIEW reason ("expensive volatility can make a correct directional thesis lose money").

Done means: the M2 options run (cassettes) shows a `volatility context` block in TRADE IDEAS ADVISORY with close-to-close, Parkinson and Yang-Zhang realised vol, an EWMA and a fixed-weight HAR-style forecast with a low/high range, ATM IV, a put-minus-call skew proxy, the IV-minus-forecast premium in vol points, and "IV rank: unavailable (no IV history store)". Daily bars unavailable -> block marked unavailable, structures still priced.

## Design decisions

1. **Estimators** (stdlib only, annualised by sqrt(252)): close-to-close sample stdev of log returns; Parkinson (high/low); Yang-Zhang (open/high/low/close, k = 0.34/(1.34 + (n+1)/(n-1))); EWMA variance with lambda 0.94 (RiskMetrics) seeded by the sample variance. Each is a CalculationReceipt with inputs hash, units and assumptions.
2. **Forecast**: HAR-style fixed weights on 1-, 5- and 22-day Yang-Zhang realised vol: 0.30 / 0.40 / 0.30. These are literature-typical weights, NOT fitted (22 fixture days cannot fit anything); the receipt says so. Range = [min, max] over the estimators and the forecast. A fitted HAR/GARCH, or a Chronos forecast, plugs in later behind the same `VolForecaster` interface as a labelled research comparison.
3. **Implied side**: ATM IV = mean of the implied vols (OPT-105 solver, from mid) of the usable call and put nearest spot; skew proxy = IV(put at or below spot x 0.95) minus IV(call at or above spot x 1.05); single expiry (the one the structures use). IV rank/percentile needs a stored IV history that does not exist: reported as unavailable, never estimated.
4. **Premium**: ATM IV minus forecast, in vol points. Reviewer threshold `[risk] iv_premium_review_points = 10`.
5. **Daily bars**: Tradier `history` (daily) through the existing TradierBarSource and cassette, 30 calendar days ending at as_of; MarketDataSpecialist 1d snapshot provides complete-bar and quality handling. The 1h fixture CSV is not resampled (sessions unknown).
6. No change to strike selection, scenario grids or structure set. No sizing.

## Ordered packets

| ID | Title | Risk | Depends on |
|---|---|---|---|
| OPT-201 | Realised-vol estimators and fixed-weight forecast with receipts (`options/vol.py`) | L3 (numbers) | M2 |
| OPT-202 | Implied-vol summary from the chain: ATM IV, skew proxy, premium vs forecast, IV rank unavailable | L2 | 201, OPT-105 |
| OPT-203 | Vol specialist step in the coordinator (deterministic, model none), `VolAssessment` finding, advisory "volatility context" block, reviewer premium check | L2 | 201, 202 |
| OPT-204 | M3 tests and OPTIONS_M3_TEST_REPORT.md | L2 | 203 |

## Human steps (Ankur)

- Tradier sandbox token (unchanged from M2). Risk limits before any sizing milestone.

## Out of M3

Fitted GARCH/HAR, Chronos inference, IV history store and IV rank, vol-surface fitting, multi-expiry term structure, sizing, news API.
