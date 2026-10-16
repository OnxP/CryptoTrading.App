# options-research — Milestone M2 plan (planned 2026-10-16)

Builds on M1 (docs/planning/OPTIONS_M1_PLAN.md, all packets complete). Process: docs/LEAN_BUILD_CONTEXT.md. Requirements: docs/CLAUDE_SETUP.md ("Pricing specialist", "Risk reviewer", "Market-data specialist", options bullets under reports/risk).

## Decisions confirmed by Ankur (2026-10-16)

- Market: **US listed equity options**. Trading account: Robinhood, **manual execution only, no connector** (no official API; credentials are trading credentials; ToS risk).
- Chains, quotes, bars: **Tradier developer sandbox** (official REST, free, delayed data). Ankur creates the sandbox account and token himself; the token lives in an environment variable named by him, never in the repo.
- Evidence: **SEC EDGAR** (official, free, timestamped) plus **one free news API** (provider still to be chosen: Finnhub or Marketaux proposed).
- TradingView stays what it is today: Pine scripts and outbound alerts. Not a data source.
- Still open: options account capital and risk limits (so **no sizing in M2**; quantity manager deferred), runtime model key/budget (agents stay on `MockModelClient` until enabled), email, Chronos.

## Milestone goal

Given a resolved instrument, a horizon and a cutoff, produce an **advisory comparison of a small approved set of defined-risk option structures against plain underlying exposure and no trade**, priced from an actual chain snapshot with timestamped bid/ask, using code for every number, reviewed by an independent risk reviewer, and reported in a section clearly separated from factual findings. No quantities, no orders, no email.

Done means: `options-research research "Compare defined-risk options on <TICKER> for the next month" --horizon-days 30 --as-of ...` returns the M1 briefing plus a "trade ideas (advisory)" section listing exact legs, quote times, scenario P&L grid, contractual payoff bounds, liquidity and assignment caveats, and a risk-review verdict; or a clearly labelled thesis-only answer when the chain is unavailable. Everything runs from recorded fixtures in tests; live calls happen only outside tests with the token present.

## Approved structure set (v1, configurable, code-enforced)

long call, long put, bull call debit spread, bear put debit spread, bull put credit spread, bear call credit spread. Defined risk only. Naked short options, ratio spreads and calendars are rejected by the structure validator in M2.

## Design decisions

1. **Connector pattern**: every external source sits behind an interface with a `RecordingHttpClient`. Tests replay JSON cassettes under `options-research/fixtures/cassettes/<provider>/`. Recording requires the token env var AND `--record` on a dedicated script; the test suite still blocks sockets.
2. **Response shapes** are taken from the public Tradier and EDGAR documentation and verified against one live call per endpoint on first recording. Until the token exists, cassettes are hand-written from the docs and labelled `source: "docs"`; the recording script replaces them and relabels `source: "live"`. The handoff records which is which.
3. **Timestamps**: Tradier quote fields `bid_date`, `ask_date`, `trade_date` (epoch ms) become the quote time; sandbox data is delayed, so `freshness_seconds` will be large and is reported, never hidden. EDGAR `acceptanceDateTime` is the first-available time for a filing.
4. **Pricing**: Black-Scholes-Merton for European approximation with explicit assumption flags; American early-exercise and dividends are listed as caveats, not modelled, in M2. Implied vol is solved from mid; if bid is 0 or the market is crossed the quote is marked unusable and the leg is excluded.
5. **Scenarios**: grid over spot (−20%…+20% in 5% steps), IV (−30%, 0, +30% relative), time (now, mid-horizon, expiry). Scenario P&L is per 1 contract at the contract multiplier, before any sizing; fees from a configurable per-contract constant labelled FIXTURE until Ankur supplies Robinhood's actual schedule (currently 0 commission, exchange/regulatory fees apply).
6. **No probabilities or expected value** are emitted: no calibrated inputs exist. Only conditional payoff bounds and scenario tables.
7. **Risk reviewer** is a code-first gate with a model-drafted narrative, like the verifier: BLOCK on unusable quotes, expiry inside/at the horizon end mismatch, earnings date inside the horizon unless acknowledged, chain older than a configured maximum age; REVIEW when account limits are unknown (always, in M2).

## Ordered packets

| ID | Title | Risk | Depends on |
|---|---|---|---|
| OPT-101 | Connector interfaces, RecordingHttpClient, cassette harness, recording script | L1 | M1 |
| OPT-102 | EDGAR connector: company_tickers → real InstrumentLookup; filings → EvidenceRecords with acceptance time | L3 (as-of) | 101 |
| OPT-103 | Tradier market data: quotes + history/timesales → MarketDataSpecialist source; venue calendar for freshness | L2 | 101 |
| OPT-104 | Tradier option chain snapshot: expirations, chains with greeks, OptionQuote model, quote-quality checks | L2 | 101, 103 |
| OPT-105 | Pricing library: BSM price/greeks/implied vol, structure payoff and scenario grid, receipts | L3 (numbers) | 104 |
| OPT-106 | Pricing specialist agent: approved structures vs underlying vs no trade; thesis-only fallback | L2 | 105, M1 agents |
| OPT-107 | Risk reviewer agent: PASS/BLOCK/REVIEW with expiry; code checks first | L2 | 106 |
| OPT-108 | Coordinator + briefing integration: `options_structures` path, advisory section separated, CLI | L2 | 106, 107 |
| OPT-109 | News API connector (provider chosen) → evidence store, behind the same cassette harness | L2 | 101 |
| OPT-110 | M2 scenario tests + OPTIONS_M2_TEST_REPORT.md: advice without orders, missing chain, expired quotes, stale advice label, earnings inside horizon | L2 | 108, 109 |

## Human steps (Ankur)

- Create the Tradier developer sandbox account; set the token in an environment variable; tell me the variable name. (needed before OPT-103 can be recorded live; docs-based cassettes let 101–108 proceed regardless)
- Choose the news API (Finnhub or Marketaux) and set its key the same way. (OPT-109)
- Later: account limits for sizing; model key for live agents.

## Out of M2

Sizing and quantity manager; position watcher and thesis monitor; outcome reviewer; email; any Robinhood connectivity; American exercise modelling; dividend modelling beyond a caveat; multi-leg execution guidance beyond liquidity notes.
