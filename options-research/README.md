# options-research

Advisory stock/options research agents. Separate project and account boundary from the C# crypto system (`Src/`) and the Kronos service (`kronos-service/`); imports nothing from either.

**It returns briefings and typed findings. It has no order, broker or execution code path, and tests assert that.**

Plan and packets: `docs/planning/OPTIONS_M1_PLAN.md`, `.workflow/tasks/OPT-0xx.md`.

## Environment (Python 3.11)

```powershell
py -3.11 -m venv options-research\.venv
options-research\.venv\Scripts\python.exe -m pip install -r options-research\requirements.txt
options-research\.venv\Scripts\python.exe -m pytest options-research\tests -q
```

Do not use the `python` on PATH (3.12). Tests block all network access.

## Configuration

`config/default.toml`. `model.enabled = false` by default: the Anthropic client refuses to construct until a model/API budget is approved. Ledgers are append-only JSONL under `data/` (git-ignored).

## Layout

```
src/options_research/   package (envelope, model client, intake, evidence, market, agents, coordinator, cli)
prompts/<role>/vN.md    versioned agent context files
fixtures/               fictional instruments, news and OHLCV; labelled FIXTURE, never real quotes
tests/                  pytest; conftest forbids sockets
```

## Running a research request (M1, fixtures)

```powershell
options-research\.venv\Scripts\python.exe -m pip install -e options-research
options-research\.venv\Scripts\options-research.exe research "Analyse FIXT news from the last seven days" --as-of 2026-10-10T20:00Z
```

Prints a briefing (instrument, scope, material news, market reaction, bullish/bearish evidence, catalysts, uncertainties, invalidation, next investigations) and a `report_id`. Reports append to `data/reports.jsonl`; model usage to `data/usage.jsonl`. `--parent <report_id>` links a follow-up. Ambiguous or unresolved instruments return a clarification (exit 3); a future as-of or non-inline delivery is rejected (exit 2).

Fixture instruments: FIXT (XNAS), ACME (XNYS), ACMA (XNAS), BRIT (XLON). All news, prices and model answers are fictional and labelled FIXTURE.

## Options structures (M2, cassettes)

```powershell
$env:TRADIER_SANDBOX_TOKEN = "replay-only"   # any value replays fixtures/cassettes/tradier; unset -> thesis-only
options-research\.venv\Scripts\options-research.exe research "Compare defined-risk options on FIXT for the next month" --as-of 2026-10-15T20:00Z --window-days 10 --horizon-days 30 --analyses options_structures
options-research\.venv\Scripts\options-research.exe show <report_id> --now 2026-10-15T20:30Z
```

Adds a final `TRADE IDEAS ADVISORY` section (always PARTIAL: account limits not configured, no quantities): six approved defined-risk structures (long call/put, bull call and bear put debit, bull put and bear call credit) with exact legs, bid/ask and quote times, net debit/credit, contractual bounds, a scenario P&L grid against 100 shares and no trade, assignment caveats and an independent risk-review verdict with expiry. Without a chain the section is thesis-only with no legs. `show` prints `STALE ... not actionable` once the report's `valid_until` (earliest leg quote expiry) has passed. Plan: `docs/planning/OPTIONS_M2_PLAN.md`; report: `docs/planning/OPTIONS_M2_TEST_REPORT.md`. M3 adds a descriptive `volatility context` block (realised estimators, non-fitted forecast, ATM IV, skew proxy, implied-minus-realised premium) and a REVIEW reason when implied vol is well above realised; see `docs/planning/OPTIONS_M3_PLAN.md`.

Live data needs `TRADIER_SANDBOX_TOKEN` (chains, bars) in the environment and `scripts/record_cassettes.py --record`; nothing is stored. The live news API (`FINNHUB_API_KEY`) is a deferred future feature; news evidence is fixture/EDGAR-based for now.
