# options-research

Advisory stock/options research agents. Separate project and account boundary from the C# crypto system (`Src/`) and the Kronos service (`kronos-service/`); imports nothing from either.

**It returns briefings and typed findings. It has no order, broker or execution code path, and tests assert that.**

Plan and packets: `docs/planning/OPTIONS_M{1,2,3,4}_PLAN.md`, `.workflow/tasks/OPT-*.md`.

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
src/options_research/   package (envelope, model client, intake, evidence, market, agents, options, regime, coordinator, cli)
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

## Regime screen (M4, Yahoo Finance, live)

```powershell
$env:PYTHONPATH = "options-research\src"   # or pip install -e options-research
options-research\.venv\Scripts\python.exe -m options_research.cli screen --top 50 --detail
options-research\.venv\Scripts\python.exe -m options_research.cli screen --max-price 5 --min-price 1 --top 20 --benchmark SPY --range 5y
options-research\.venv\Scripts\python.exe -m options_research.cli screen --symbols IREN,DELL,NOK --detail   # watchlist, no price filter
```

Pulls the top-N US equities (Nasdaq, NYSE, NYSE American) with `min_price < price < max_price` sorted by day volume from Yahoo's public screener, three years of daily candles per symbol plus the benchmark, fits a 3-state Gaussian HMM (standard library) on 5-day trailing log return / relative volume / log range, and prints one row per symbol: price, day volume, 3-month average volume, market cap, sector and industry (Yahoo company profile), regime label (BULL/BEAR/FLAT by mean return, -V/-M/-Q by range rank within that symbol), days in regime, P(current), one-step switch hazard, posterior drift, runner-up regime and a turning-point flag (FRESH, WEAKENING, OVERDUE, STABLE). The market benchmark is shown first. Rows keep the screener's volume order; `--symbols` runs an explicit watchlist instead (quote fields then come from the company profile, which has no 3-month average volume). A session still open at run time is dropped, so the last bar is always complete. Records append to `data/screens.jsonl`. Defaults live in `config/default.toml` `[yahoo] [regime] [screen]`; `--replay-dir options-research\fixtures\cassettes\yahoo` runs the fictional fixture universe with no network. Yahoo's endpoints are unofficial and keyless; the cookie and crumb they need live only in memory for the run. The screen computes no position size. Plan: `docs/planning/OPTIONS_M4_PLAN.md`; report: `docs/planning/OPTIONS_M4_TEST_REPORT.md`.
