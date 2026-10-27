# options-research M4 test report (2026-10-26)

Run: `options-research\.venv\Scripts\python.exe -m pytest options-research\tests -q` from the worktree. Sockets blocked; all Yahoo responses from docs-shaped cassettes with FICTIONAL regime-switching bars (`scripts/make_yahoo_docs_cassettes.py`). The live run below was made outside tests and is not committed.

| Behaviour | Status | Evidence |
|---|---|---|
| GET cassette keys byte-identical to M2 (Tradier/EDGAR cassettes still replay) | COVERED | test_yahoo.py::test_m2_get_keys_unchanged; M1-M3 suites unchanged (146 passed) |
| POST keys include the body; `crumb` volatile in keys and redacted on disk | COVERED | test_yahoo.py::test_crumb_is_volatile_in_cassette_keys_and_redacted_on_disk |
| Chart rows parsed, null rows dropped and counted, UTC times, NY session bounds | COVERED | test_yahoo.py::test_chart_rows_parsed_null_rows_dropped, ::test_bar_source_uses_exchange_sessions |
| Chart error -> YahooError / MarketDataUnavailable, never a traceback | COVERED | test_yahoo.py::test_chart_error_is_yahoo_error_not_traceback |
| Screener: cookie -> crumb -> POST, EQUITY only, Yahoo order and total kept, crumb cached, size validated | COVERED | test_yahoo.py::test_screener_uses_crumb_and_returns_quotes_in_order |
| Company profile (sector, industry, country) via quoteSummary; missing symbol -> reason, never raises | COVERED | test_yahoo.py::test_profile_sector_industry_and_missing_symbol; screen rows FIXT/ACME/NOPE |
| Missing cassette raises NoCassette, never a live call | COVERED | test_yahoo.py::test_screener_without_cassette_raises_not_network; conftest blocks sockets |
| No account / portfolio / order / watchlist endpoint in the Yahoo client | COVERED | test_yahoo.py::test_no_account_portfolio_or_order_endpoints |
| HMM recovers planted regimes (> 90% bar accuracy, sticky transitions) | COVERED | test_regime_hmm.py::test_recovers_two_planted_regimes (99%) |
| Deterministic fit for (data, seed); posteriors are distributions; EM monotone; rows of A sum to 1 | COVERED | test_regime_hmm.py::test_fit_is_deterministic_for_data_and_seed, ::test_posteriors_are_distributions_and_loglik_increases |
| Sticky prior raises self-transitions, never beats ML on data likelihood | COVERED | test_regime_hmm.py::test_sticky_prior_raises_self_transition |
| Expected duration and stationary distribution | COVERED | test_regime_hmm.py::test_expected_duration_and_stationary |
| Too few observations / one state / outlier row | COVERED | test_regime_hmm.py::test_too_few_observations_raise, ::test_outlier_does_not_break_numerics |
| Feature warm-up accounting, z-score, winsor, zero volume and inverted range guards | COVERED | test_regime_assessment.py::test_features_window_and_warmup, ::test_zero_volume_and_bad_range_do_not_crash |
| Short history -> UNAVAILABLE, never guessed | COVERED | test_regime_assessment.py::test_short_history_is_unavailable_never_guessed; screen row NEWB |
| Long stable regime -> STABLE/OVERDUE, BEAR-V is the selloff state, unique labels, receipts | COVERED | test_regime_assessment.py::test_long_stable_regime_is_stable_and_labelled |
| Fresh planted switch -> FRESH turning point with last transition date and note | COVERED | test_regime_assessment.py::test_fresh_switch_is_a_turning_point; screen row FIXT |
| Assessment deterministic and ASCII | COVERED | test_regime_assessment.py::test_assessment_is_deterministic_and_ascii |
| Screen rows in screener order; FRESH / STABLE / UNAVAILABLE / ERR outcomes explicit; benchmark first | COVERED | test_regime_screen.py::test_rows_follow_screener_order_and_each_outcome_is_explicit, ::test_benchmark_first_and_disclosures_present |
| Render ASCII, columns incl. MKTCAP and SECTOR / industry, candidate line, no buy/sell/sizing wording in the body | COVERED | test_regime_screen.py::test_render_is_ascii_has_columns_and_no_advice |
| Ledger append-only round trip; JSON output | COVERED | test_regime_screen.py::test_ledger_round_trip_and_json |
| CLI replay exit 0 and ledger written; `--json`; empty replay dir -> exit 5 UNAVAILABLE | COVERED | test_regime_screen.py::test_cli_screen_replay, ::test_cli_screen_missing_cassette_is_unavailable_exit_5 |
| Position sizing, share counts | NOT IN M4 | boundary unchanged: `[risk] account_limits_configured = false`; renderer test forbids the wording |
| Regime -> strategy mapping, backtests | NOT IN M4 | labels are descriptive; no rule is evaluated |
| Chronos/Kronos or fitted GARCH comparison | NOT IN M4 | `VolForecaster` seam from M3 still the entry point |
| Rolling refit / out-of-sample stability of labels | NOT IN M4 | one full-sample fit per symbol per run |
| Cross-symbol comparability of labels | NOT IN M4 | -V/-M/-Q rank within one symbol; the legend says so |

## Totals

177 tests passed, 0 failed, 0 skipped (M1 74 + M2 60 + M3 12 + M4 31, incl. watchlist mode and open-session bar drop added 2026-10-27). Runtime about 15 s (the HMM tests fit several synthetic series).

## Live reading (outside tests, 2026-10-26 00:04Z; real market data, not committed)

`screen --top 50 --detail` against Yahoo: 937 US equities matched 1 < price < 10 on NMS/NYQ/ASE; all 50 rows assessed on 744 usable bars (two newer listings on 259 and 417); run time about 2.5 minutes. The benchmark SPY read BEAR-M for 6 days at P = 0.51 with BULL-Q the runner-up at 0.48 (flag WEAKENING). Twelve rows were turning-point candidates (one WEAKENING, eleven FRESH). Output is in `options-research/data/screen_top50_2026-10-26.txt` and the record in `data/screens.jsonl` (both git-ignored). These are model diagnostics on unofficial data, not recommendations.

## Known limitations

- Yahoo endpoints are unofficial; a shape change breaks the connector until cassettes are re-recorded and the parser adjusted.
- K = 3 is fixed by config; no model selection. Diagonal covariance.
- `days_in_regime` resets on any posterior flip, so a brief wobble inside a long regime shortens the count.
- The sticky prior (20 pseudo-counts) and the 5-day feature window are judgement defaults, not calibrated; the receipt records them.
