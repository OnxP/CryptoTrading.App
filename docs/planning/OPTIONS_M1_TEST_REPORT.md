# options-research M1 test report (2026-10-15)

Run: `options-research\.venv\Scripts\python.exe -m pytest options-research\tests -q` from the worktree. All tests run with sockets blocked (tests/conftest.py).

## Spec scenarios (CLAUDE_SETUP "Ad hoc tests must cover ...")

| Spec scenario | Status | Test |
|---|---|---|
| stock/news without an account | COVERED | scenarios/test_adhoc_scenarios.py::test_s1 |
| unknown/ambiguous symbols | COVERED | ::test_s2, test_intake.py (deictic, ambiguous) |
| exact window preservation | COVERED | ::test_s3, test_intake.py::test_explicit_window_preserved_exactly |
| optional specialist routing | COVERED | ::test_s4, test_coordinator.py::test_news_summary_only_skips_market_and_verifier |
| missing options horizon | COVERED | ::test_s5 |
| missing options chain | NOT IN M1 | no chain source; `options_structures` analysis is accepted at intake with a horizon but no pricing specialist exists yet (M2) |
| scoped follow-ups | COVERED | ::test_s6, test_coordinator.py::test_follow_up_records_lineage_and_reruns_market |
| failure/partial answers | COVERED | ::test_s7, test_coordinator.py::test_market_fixture_missing_makes_partial_with_blocked_section |
| injected article instructions | COVERED | ::test_s8, test_news_collector.py (code-side detection even when the model misses it) |
| no implicit email | COVERED | ::test_s9, test_intake.py::test_email_delivery_rejected_in_m1 |
| explicit delivery through approved controls | NOT IN M1 | no delivery subsystem exists; intake rejects every non-inline delivery |
| rejection of research records at the order boundary | NOT IN M1 (by construction) | ::test_s10 asserts no order/broker/execute module or import exists, so there is no boundary to reject at yet; when an execution workflow exists elsewhere it must reject `ReportRecord` inputs |
| natural-language parsing / symbol resolution quality | NOT IN M1 | intake is deterministic over explicit params plus a fixture lookup; no model proposes parameters yet |

## Evidence/outcome scenarios touched by M1

| Spec scenario | Status | Test |
|---|---|---|
| repeated articles from one origin | COVERED | test_evidence_store.py::test_syndicated_copies_form_one_cluster..., test_evidence_verifier.py::test_syndicated_only_is_insufficient |
| corrected/retracted evidence, original preserved | COVERED | test_evidence_store.py::test_correction_appends_and_hides_original_only_once_available |
| real extreme observation vs corrupt data | COVERED | test_evidence_verifier.py::test_extreme_consistent_move_not_flagged; test_market_data.py (nonfinite -> None, not smoothed) |
| future/late-available evidence excluded at as-of | COVERED | test_evidence_store.py::test_future_available_record_is_excluded_and_listed; ::test_s6 shows it appearing after the cutoff |
| unit/time-window mismatch, contradictory primary records | PARTIAL | numeric claim vs receipt tolerance only; unit/currency checks not implemented |
| immutable expectations, outcome scoring, hypothetical vs actual | NOT IN M1 | no outcome reviewer yet |

## Totals

74 tests passed, 0 failed, 0 skipped (OPT-001..010). Runtime about 0.5 s.

## Known limitations recorded in handoffs

- Realised vol annualised on a 24-hour calendar (fixture is 24h); venue calendars arrive with real data.
- Free-text symbol detection is upper-case-token based; fine for fixtures, revisit with reference data.
- `anthropic` SDK not installed; live client refuses to construct until `model.enabled = true` and a budget is approved.
