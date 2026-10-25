# options-research M2 test report (2026-10-25)

Run: `options-research\.venv\Scripts\python.exe -m pytest options-research\tests -q` from the worktree. All tests run with sockets blocked (tests/conftest.py). Every external response comes from a cassette under `options-research/fixtures/cassettes/`; all cassettes are still `source: "docs"` (hand-written from provider documentation) because no Tradier or Finnhub token has been configured yet.

## Options scenarios (CLAUDE_SETUP "Required scenarios" and "Ad hoc tests must cover ...")

| Spec scenario | Status | Test |
|---|---|---|
| options advice without orders | COVERED | scenarios/test_options_scenarios.py::test_o1; test_coordinator_options.py::test_full_options_run_adds_labelled_advisory_section_last (six structures, one contract set each, no order/quantity/fill/approved fields anywhere in the record) |
| missing options chain -> thesis-only, no fabricated legs | COVERED | ::test_o2; test_pricing_specialist.py::test_chain_unavailable_abstains_thesis_only_with_zero_legs; test_coordinator_options.py::test_chain_unavailable_gives_thesis_only_section_and_no_legs |
| missing option quotes (zero bid, crossed, stale) never priced | COVERED | ::test_o1 asserts the three unusable fixture legs appear nowhere in the report; test_pricing.py (unusable leg raises at entry) |
| expired quotes -> BLOCK | COVERED | ::test_o3; test_risk_reviewer.py::test_stale_leg_blocks_naming_the_symbol |
| stale report advice not labelled actionable; STALE on reprint | COVERED | ::test_o3, ::test_o5; test_coordinator_options.py::test_reprinting_after_valid_until_prints_stale_not_actionable (`show --now` past valid_until prints the STALE banner and "[STALE, not actionable]") |
| earnings/catalyst inside horizon -> REVIEW with the date | COVERED | ::test_o4; test_risk_reviewer.py::test_earnings_inside_horizon_is_review_with_date (date parsed from the verified news catalyst "full results on 28 October") |
| option leg/expiry risks: expiry before horizon end -> BLOCK | COVERED | ::test_o6; test_risk_reviewer.py::test_expiry_before_horizon_end_blocks; coordinator falls back to thesis-only when no chain exists for a later expiration (no fabrication) |
| losses/costs despite correct direction (expensive IV) visible | COVERED | ::test_o7; test_pricing_specialist.py::test_expensive_iv_correct_direction_still_loses (synthetic 120%-vol chain: +5% cells negative, spec caveat sentence present, rendered in the section) |
| assignment/exercise caveats on every structure | COVERED | test_pricing_specialist.py (caveats list), test_coordinator_options.py (section text) |
| risk reviewer cannot PASS while limits unknown; agent output cannot loosen limits | COVERED | test_risk_reviewer.py::test_all_clean_is_review_for_unconfigured_limits_never_pass (code raises on PASS); ::test_reviewer_never_changes_pricing_findings; model narrative stating a different verdict is rejected |
| quarantined (contradicted) evidence blocks new entry in that direction | COVERED | test_risk_reviewer.py::test_quarantined_evidence_behind_direction_blocks_only_that_direction |
| verification missing/ERROR -> REVIEW | COVERED | test_risk_reviewer.py::test_verification_missing_or_error_is_review |
| model output naming a leg not in the input -> ERROR, nothing accepted | COVERED | test_pricing_specialist.py::test_model_naming_unknown_leg_or_order_wording_is_error |
| M1 behaviour unchanged (content hash) | COVERED | test_coordinator_options.py::test_m1_fixture_run_hash_unchanged (hash recorded before any M2 coordinator change) |
| report/position identity mismatch | NOT IN M2 | ::test_o8 asserts no position/order/broker module or import exists; there are no positions to mismatch against yet |
| expired reviews / exit deadlines | NOT IN M2 | risk verdicts carry `expires_at` but nothing consumes it after the report (no monitoring loop) |
| sizing, quantity manager, account limits | NOT IN M2 | every advisory section is PARTIAL with "account limits not configured"; no quantity is computed |
| live chain / live news recording | NOT IN M2 | cassettes are docs-shaped; `scripts/record_cassettes.py --record` replaces them once `TRADIER_SANDBOX_TOKEN` / `FINNHUB_API_KEY` exist |

## Evidence scenarios added in M2

| Spec scenario | Status | Test |
|---|---|---|
| filings with acceptance time as availability (EDGAR) | COVERED | test_edgar.py (OPT-102) |
| backfilled history excluded from as-of research unless disclosed | COVERED | test_news_api.py::test_backfill_marked_and_hidden_unless_allowed, ::test_everything_is_backfill_without_live_since |
| same canonical URL = one origin; same title elsewhere is not corroboration | COVERED | test_news_api.py::test_same_canonical_url_clusters_same_title_elsewhere_does_not |
| missing API key -> connector unavailable, collector abstains | COVERED | test_news_api.py::test_missing_key_is_unavailable_and_collector_abstains |
| secrets never stored | COVERED | test_cassettes.py (header values scrubbed), test_news_api.py (key absent from the ledger) |

## Totals

134 tests passed, 0 failed, 0 skipped (OPT-001..010, OPT-101..110). Runtime about 1 s.

## What the CLI does now

```
options-research research "Compare defined-risk options on FIXT for the next month" --as-of 2026-10-15T20:00Z --window-days 10 --horizon-days 30 --analyses options_structures
options-research show <report_id> [--now 2026-10-15T20:30Z]
```

With `TRADIER_SANDBOX_TOKEN` set the first command prints the M1 briefing followed by TRADE IDEAS ADVISORY (PARTIAL): six structures with legs, quote times, net debit/credit, bounds, scenario grid against 100 shares and no trade, caveats, and a REVIEW verdict with expiry. Without the token it prints a THESIS-ONLY section with no legs. `show` prints a STALE banner once `valid_until` (earliest leg quote expiry) has passed.

## Known limitations recorded in handoffs

- Direction is taken from the question text; none stated -> both directions compared. No forecast is made.
- Strike selection is mechanical (ATM long legs, first usable strike beyond spot +/- 5% for short legs); no vol forecast or IV-rank input yet.
- BSM European approximation flagged on every receipt; American early exercise and dividends are caveats, not modelled.
- Catalyst dates come from regex parsing of catalyst strings in news findings (English month names); EDGAR gives no forward earnings calendar.
- Live news API DEFERRED (Ankur, 2026-10-25): a future feature. The Finnhub connector is built and tested on a docs-shaped cassette but no key will be configured in this milestone; news evidence stays fixture/EDGAR-based.
- Backfill handling exists at the evidence-store level; the request/collector disclosure wiring is not yet connected, so research still runs on fixture evidence.
