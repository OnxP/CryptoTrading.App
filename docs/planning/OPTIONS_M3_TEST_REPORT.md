# options-research M3 test report (2026-10-25)

Run: `options-research\.venv\Scripts\python.exe -m pytest options-research\tests -q` from the worktree. Sockets blocked; all external data from docs-shaped cassettes (no Tradier token yet).

## M3 scope: volatility context (OPT-201..204)

| Item | Status | Test |
|---|---|---|
| realised-vol estimators recover a known constant vol (close-to-close, Parkinson, Yang-Zhang within 15% over 250 GBM bars) | COVERED | test_vol.py::test_estimators_recover_constant_vol |
| Yang-Zhang more efficient than close-to-close on 60-bar drifting windows | COVERED | test_vol.py::test_yang_zhang_is_more_efficient_than_close_to_close_on_short_windows |
| too few bars -> every receipt None with a reason; short window disclosed in the forecast receipt | COVERED | test_vol.py::test_too_few_bars_gives_none_with_reason_and_short_window_is_disclosed |
| forecast is a fixed-weight blend, NOT fitted, and says so; deterministic receipts; `VolForecaster` seam | COVERED | test_vol.py::test_summary_is_deterministic_and_forecaster_seam_matches |
| ATM IV and skew proxy from the fixture chain, legs cited in receipts; IV rank unavailable with reason | COVERED | test_vol.py::test_fixture_daily_bars_and_chain_give_a_full_vol_context |
| flat-vol synthetic chain: ATM IV round-trips, skew proxy ~0 | COVERED | test_vol.py::test_synthetic_flat_vol_chain_round_trips_atm_and_zero_skew |
| unsolvable leg IV (intrinsic floor) -> None, never substituted | COVERED | test_vol.py::test_unsolvable_leg_is_none_not_substituted |
| vol_specialist step (model none) emits VolAssessment; "volatility context" block precedes the first structure | COVERED | test_coordinator_options.py::test_vol_context_block_precedes_structures_and_feeds_risk_review; scenarios ::test_o9 |
| daily bars unavailable -> realised unavailable, structures still priced; no chain -> both sides unavailable, thesis-only | COVERED | test_coordinator_options.py::test_vol_context_unavailable_without_daily_bars_structures_still_priced; scenarios ::test_o9b |
| implied-minus-realised premium >= threshold -> REVIEW reason on every structure; below -> check line only | COVERED | test_coordinator_options.py (two tests), scenarios ::test_o9 |
| no buy/sell-premium wording in code output | COVERED | scenarios ::test_o9 (regex over the section) |
| M1 content hash unchanged | COVERED | test_coordinator_options.py::test_m1_fixture_run_hash_unchanged |
| fitted GARCH/HAR, Chronos inference | NOT IN M3 | `VolForecaster` seam exists; nothing fitted (22 fixture bars) |
| IV rank / percentile | NOT IN M3 | needs a stored IV history; reported as unavailable, never estimated |
| term structure across expiries, delta-matched skew | NOT IN M3 | single expiry; skew proxy is strike-distance based and labelled so |
| using the vol forecast inside scenario grids | NOT IN M3 | grids still use each leg's entry IV with -30/0/+30% shifts (OPT-105); the context is descriptive |

## Totals

146 tests passed, 0 failed, 0 skipped (M1 74 + M2 60 + M3 12). Runtime about 1.3 s.

## Fixture reading (for orientation only; FIXTURE data)

On the FIXT cassettes at 2026-10-15: realised close-to-close 33.0%, Parkinson 24.5%, Yang-Zhang 18.7%, EWMA 35.0%; fixed-weight forecast 18.6% (range 18.6 to 35.0); ATM IV 55.0% for 20 Nov; skew proxy +0.5%; implied minus forecast +36.4 vol points, so every structure carries the "expensive volatility" REVIEW reason. These are fictional numbers.

## Known limitations

- Weights 0.30/0.40/0.30 are literature-typical, not fitted; the 22-day component is the whole window when fewer than 23 bars exist.
- Daily bars come from the Tradier history cassette (30 calendar days ending at as_of); the 1h fixture CSV is not resampled.
- Premium threshold (10 vol points) is a config default, not calibrated.
