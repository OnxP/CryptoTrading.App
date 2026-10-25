# options-research — Milestone M1 plan (planned 2026-10-15)

Planner output for the first options-research milestone. Process: docs/LEAN_BUILD_CONTEXT.md. Requirements: docs/CLAUDE_SETUP.md (§"options-research", §"Ad hoc research requests", §"Common context for every agent", §"Agent contexts"). Nothing here authorises trading, email sending, data purchases or live model spend.

## Milestone goal

A **request handler / CLI usable from Claude** that turns one ad hoc stock/news question into a cited, offline-reproducible briefing, using the agent roles the spec defines, entirely against **fixtures** (no live news, quotes, broker, email or model spend). This is the options-research analogue of the Kronos "vertical slice" milestone.

Done means: `options-research research "Analyse NVDA news from the last seven days" --as-of 2026-10-10T20:00Z` produces an immutable report record containing identified instrument, exact scope/as-of, material news clusters with sources, market reaction with freshness, bullish/bearish evidence, catalysts, uncertainties, invalidation conditions, next investigations — with every agent output wrapped in the required envelope, every number from code receipts, and partial/blocked sections shown when a specialist fails. No trade idea, no options legs (those need real chains: M2).

## Existing-code-first findings

- No options/equities code exists anywhere in the repository (`grep -ri option Src/ kronos-service/` finds only C# `Options` config classes). Nothing to reuse for instruments, news or chains.
- Reusable from the repo: Python 3.11 toolchain and venv pattern (`kronos-service/README.md`), packet/handoff workflow (`.workflow/`), SQL Server as the eventual record store (deferred: M1 uses local JSONL ledgers, see decision 5).
- The C# system is **not** touched. Options-research is a separate project directory and account boundary (CLAUDE_SETUP §"Working approach").

## Decisions (planner; reversible, flag disagreement)

1. **Location** `options-research/` at repo root, own venv, own config, own tests. Can be split into its own repository later; nothing imports from `Src/` or `kronos-service/`.
2. **Language** Python 3.11 (`py -3.11`), same as kronos-service and the planned orchestrator. The agents are model calls plus deterministic tools; no reason to put them in the C# execution system.
3. **Model access** behind one `ModelClient` interface. M1 ships `MockModelClient` (fixture responses keyed by role + prompt version) and an `AnthropicModelClient` that is **disabled by config** until the model/API budget question is answered. Default model ids when enabled: `claude-sonnet-5-5` for specialists, `claude-haiku-5-5` for cheap classification; configurable, never hard-coded in agents.
4. **Schemas** `pydantic` v2 for the envelope, request, evidence and finding types; `pytest` for tests. Two dependencies only.
5. **Records** append-only JSONL ledgers under `options-research/data/` (git-ignored) with content hashes. SQL Server tables come when the connection/schema question is answered; the ledger interface is the seam.
6. **Fixtures** clearly fictional: ticker universe `fixtures/instruments.json`, news feed `fixtures/news/*.json`, OHLCV `fixtures/market/*.csv`. Fictional limits/quotes labelled `FIXTURE`. No real quotes are ever typed into fixtures from memory.
7. **Out of M1**: options chains and the pricing specialist, quantity manager, risk reviewer, position watcher, thesis monitor, outcome reviewer, reporting/email, scheduled runs, Chronos. Each is a later milestone once the data questions are answered.

## Ordered packets

| ID | Title | Risk | Depends on |
|---|---|---|---|
| OPT-001 | Project skeleton, venv, config, test harness | L0 | — |
| OPT-002 | Agent output envelope + typed findings + contract tests | L1 | 001 |
| OPT-003 | ModelClient interface, MockModelClient, disabled Anthropic client, usage accounting | L1 | 002 |
| OPT-004 | Research request intake: schema, instrument lookup (fixture), window/as-of rules | L3 (as-of) | 002 |
| OPT-005 | Point-in-time evidence store + as-of query + origin clustering | L3 (leakage) | 002 |
| OPT-006 | Market-data specialist (equities fixture): validated snapshot, receipts, no LLM | L2 | 002, 005 |
| OPT-007 | News collector agent: context file, connector→evidence store, dedupe, injection test | L2 | 003, 005 |
| OPT-008 | Evidence verifier agent: claim-level statuses, code rechecks, quarantine policy | L2 | 003, 005, 006 |
| OPT-009 | Coordinator + CLI: route jobs, assemble briefing, immutable report record, partial results | L2 | 004–008 |
| OPT-010 | M1 scenario tests from CLAUDE_SETUP §"Ad hoc tests" that M1 can cover | L2 | 009 |

Packet files: `.workflow/tasks/OPT-0xx.md`. Handoffs: `.workflow/handoffs/OPT-0xx.md`.

## Open configuration questions (one batch; none block M1)

Answers change M2+, not M1, because M1 is fixture-only.

1. **Options market, broker, universe**: which exchange(s)/country, which broker (for chains and later manual execution), initial ticker universe or sector list.
2. **Data/news providers and licences**: equities quotes and option chains source; news source(s); whether any paid feed is already licensed. Nothing is bought without approval.
3. **Options account capital and risk limits**: needed before any personalised quantity; absent → thesis-only answers.
4. **Model/API budget and billing route**: Anthropic API key for runtime agents (separate from Claude Code usage) and a monthly cap; or confirm runtime must also go through Claude Code sessions.
5. **Email**: sender/provider, recipient allowlist, timezone, cadence. Not needed until the reporting milestone.
6. **Chronos**: keep as a separate research comparison for options underlyings, or drop. Not needed for M1.

## Status (2026-10-15)

All ten packets COMPLETE; 74 tests green; see docs/planning/OPTIONS_M1_TEST_REPORT.md. Awaiting planner review and the configuration answers below before M2.

## Acceptance for the milestone

- `pytest options-research/tests` green.
- CLI run above completes against fixtures with `MockModelClient`, writes one report record with run id, as-of, input hashes, agent envelopes and findings; a second identical run produces a new record with identical content hash.
- Negative runs: ambiguous name → one clarification, no guessed ticker; article containing instructions → ignored and flagged; evidence after as-of → excluded and listed as excluded; market-data fixture missing → briefing marked PARTIAL with the market section BLOCKED.
- No file under `Src/` changed; no network calls in tests (enforced by a test fixture that fails on socket use).
