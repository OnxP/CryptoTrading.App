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
