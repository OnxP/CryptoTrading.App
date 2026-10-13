# M0 baseline — observed 2026-10-13

Planner output for milestone M0 (docs/KRONOS_BUILD_CONTEXT.md). Everything below was observed by running commands on this machine from the worktree at commit 9ae73d8 (master ddfd406 + planning docs). Nothing was installed, modified or deployed.

## Prerequisites (ENV-001 evidence)

| Item | Observed | Spec requirement | Status |
|---|---|---|---|
| Git | 2.51.0.windows.1 | present | OK |
| Python | `py -3.11` → C:\Users\ankur\AppData\Local\Programs\Python\Python311; default `python` is 3.12.7; 3.13 also installed | 3.11 | OK (use `py -3.11`) |
| .NET SDK | 10.0.204 (+10.0.100-preview.7) | compatible with C# system | OK; README says 9.0 but Monitor.Tests already targets net10.0 |
| NVIDIA | RTX 5080, driver 610.88, 16303 MiB | RTX 5080 | OK |
| ODBC Driver 17 | msodbcsql17.dll present; sqlcmd 170 present | ODBC for SQL Server | OK |
| SQL Server | service `MSSQL$APDATASERVICE` (ANKUR-PC\APDATASERVICE), Windows auth works | SQL Server | OK |
| Claude Code | 2.1.267 | installed | OK; model list/billing not verified (ENV-002) |
| Docker | not installed on host | n/a | docker-compose.yml (Postgres services) cannot run here |
| PyTorch CUDA | no torch in 3.11; **torch 2.14.0+cu130, CUDA 13.0, `cuda.is_available()==True`** in two unrelated venvs (C:\Local\venvs\hmr2, sam3db) | CUDA on RTX 5080 | known-good wheel index for ENV-005 |
| Kronos checkpoints | HF cache already has `NeoQuasar/Kronos-base` (snapshot 2b554741…, 391 MB) and `NeoQuasar/Kronos-Tokenizer-base` (0e011738…, 16 MB) | download | ENV-007 reduces to verify-from-cache |
| Kronos repo | no clone found under C:\Local or repo | clone | ENV-006 pending |
| Python venv | none in repo; `.gitignore` has no Python entries | venv | ENV-004 pending |

## Existing C# system — build/test baseline

```
dotnet build Src/CryptoTrading.App.sln          -> 2 errors (exit 0 reported by shell wrapper; MSBuild reports 2 Error(s))
```

- `Src/CryptoTrading.App.Algorthm/DualRegime/DualRegimeExecutionStrategy.cs(104,47)`: CS0246 `ITrade` not found
- `…DualRegimeExecutionStrategy.cs(67,55)`: CS0535 does not implement `IExecutionStrategy.ProcessStrategy(TradeState)`
- Origin: PR #81 (ddfd406) added DualRegime against an `IExecutionStrategy` shape that PR #80 had already changed. **Pre-existing on master, not Kronos scope.**
- Consequence: `CryptoTrading.App.Tests` (net9.0) references Algorithm → test DLL never built, **0 of the README's 117+ tests ran**.

```
dotnet test Src/CryptoTrading.App.Monitor.Tests   -> Failed 5 / Passed 27 (run 1); Failed 3 / Passed 29 (run 2)
```

- Flaky `TradeProcessorTests` (`CurrentMonitors_ReturnsOnlyLiveMonitors`, `Constructor_WithLoggerAndFactory_InitializesCorrectly`, `ClearInactiveTrades_RemovesOnlyInactiveMonitors`, `CompleteAllTransactions_WithNoMonitors_DoesNotThrow`, …): `InvalidOperationException: Operations that change non-concurrent collections must have exclusive access`. Concurrency bug in test or SUT; pre-existing.

## Data stores — two coexisting

1. **SQL Server `CryptoDb`** (legacy, EF6 `System.Data.SqlClient`; registered in Process, Calibration, DatabaseLoad, AlgorthmTesting, MarketDataValidation). Tables: `CandleStickDbs`, `CryptoConfigs`, `TradesDbs`, `HistoricTrades`, `DualRegime_Equity`, `DualRegime_ModelMeta`, `DualRegime_Trades`. 477 symbols.
2. **PostgreSQL `CryptoDb`** (new services split, `CryptoTrading.App.Persistence` + Npgsql, `MarketDataService`/`BrokerService` via docker-compose). Docker is not installed on this host, so it is not runnable here today.

BUILD_SPEC §2 fixes **SQL Server** for the Kronos service. That matches store 1. The services split (memory: project-services-architecture) targets Postgres; the Kronos service should read from SQL Server per spec and treat Postgres as out of scope unless a decision record says otherwise.

### BTCUSDT coverage in `CandleStickDbs` (read-only query)

`Interval` is `CandleInterval` (Src/CryptoTrading.App.Core/Exchange/ExchangeEnums.cs:38): 0=1m, 2=5m, 3=15m, 5=1h, 7=4h, 11=1d, 13=1w.

| Interval | Rows | First OpenTime | Last CloseTime |
|---|---|---|---|
| 1m | 450,141 | 2025-02-28 | **2026-05-01 22:01** |
| 15m | 307,950 | 2017-08-17 | **2026-06-04 21:30** |
| 1h | 77,002 | 2017-08-17 | 2026-06-04 22:00 |
| 4h | 19,267 | 2017-08-17 | 2026-06-05 00:00 |

**Data is stale**: no candles since early June 2026 (today 2026-10-13). 1m stops 2026-05-01. The 1m window (needed for evaluation, BUILD_SPEC §29) covers only 2025-02-28 → 2026-05-01. Reloading is a DatabaseLoad/MarketDataService concern, not Kronos; it must be scheduled before M2 (DATA-002/006) and M4 (replay) can be meaningful.

### Time semantics observed (input to DATA-004)

- Row sample (15m): `OpenTime 2026-06-04 21:15`, `CloseTime 2026-06-04 21:30` → **`CloseTime = OpenTime + interval` exactly** (boundary instant, *not* Binance's `openTime + interval − 1 ms`).
- `Src/CryptoTrading.App.Core/Database/DbData.cs:25,41-45` filters and orders by `CloseTime`; `CandleStickDictionary` (Core/CandleStickDictionary.cs:39-52) keys by `OpenTime` and gap-fills via `CandleStickIntervalHelper.NextCandleStickTime`.
- `ExchangeCandlestick` (Core/Exchange/ExchangeCandlestick.cs:14-15) carries both; `ExchangeCandlestickEvent` has an event/availability timestamp (Core/Exchange/ExchangeCandlestickEvent.cs:26-27) that REST-sourced synthetic events fill with `UtcNow` or `CloseTime`.
- `DateTime` columns are unqualified `datetime` (no `Kind`); UTC must be asserted, not assumed.
- `CandleStickTracker.IsFinal` (Core/RequestTracker/CandleStickTracker.cs:45-47) is the existing "closed candle" notion.

Correction 1 (origin-time ≤ availability) and correction 2 (open-vs-close meaning, revisions) in KRONOS_BUILD_CONTEXT must be written against these facts.

## Existing Claude Code configuration

`C:\Local\CryptoTrading.App\.claude\settings.json` exists with a permissions allowlist only (Read/Edit/Write/Glob/Grep, `dotnet *`, `git *`, `npm *`). No `statusLine`, no `.claude/agents/`. `settings.local.json` also present (not read). ENV-003 must **extend**, not replace, this file.

## Decisions needed before ENV-004/006 can run

1. **Where the Kronos service lives.** Recommended: this repository, top-level `kronos-service/` (venv, `third_party/Kronos`, `src/`, `tests/`), sharing REST contracts with the C# tree. Alternative: separate repo. Affects every packet's "Allowed files".
2. **Package-install permission** (ORCHESTRATION §75 — unanswered means denied). ENV-004/005/006/007 install a venv, PyTorch and Kronos requirements and clone a third-party repo. Needs an explicit yes.
3. **Candle data refresh** owner/when (not M0-blocking, M2-blocking).
4. Not blocking M0, carried from the package: Claude model list/billing on this account (ENV-002 is a human step), Chronos as separate research track, email/report config.

## Pre-existing issues outside Kronos scope (flagged, not fixed)

- Master does not build (`DualRegimeExecutionStrategy` vs `IExecutionStrategy` after #80/#81).
- `CryptoTrading.App.Tests` therefore cannot run.
- `TradeProcessorTests` flaky under concurrency.
- README prerequisites say .NET 9.0 / 117 tests; repo now mixes net9.0 and net10.0 targets.
