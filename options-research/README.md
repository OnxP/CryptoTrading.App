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
