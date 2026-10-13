# Offline workflow rehearsal

What the offline rehearsal did and did not test. The harness itself (`workflow.py`, `test_workflow.py`, `demo.py`, `sample-report.*`) was produced in the originating planning environment and has not been copied into this repository; this report is retained as the record of that rehearsal.

## Scope

User requested separate BTC autonomous and options advisory projects, specialised agent contexts, workflow validation before a Claude setup handoff, email reports with data/suggestions, and ad hoc stock/news research requests.

Subsequent scope clarification: crypto-trader supports multiple approved coins with BTC primary, and crypto/options use separate accounts with independent budgets. The setup plan was updated; the original BTC-only, single-book rehearsal was not expanded or rerun for this documentation change. Its 58 checks do not establish multi-coin support or account isolation.

Operator response was subsequently specified as a notification-first placeholder for Ankur's manual intervention. No live alerts, halt/rearm controls or independent health monitor were implemented or tested. Existing email tests cover preview/mock transport only; their pass count is not evidence of incident-notification delivery.

Further plan requirements now include an evidence verifier, an outcome reviewer comparing immutable expectations with realised results, Amazon Chronos forecasting and a point-in-time forecast/history store. Amazon Chronos was explicitly confirmed, distinct from the earlier Kronos discussion; its variant and the proposed database identity are not yet specified. These are documentation changes only: no Chronos inference, data-source corroboration, outcome scoring or database integration was implemented or tested, and the existing 58-test result does not cover them.

Post-entry market/position watching and thesis reassessment were subsequently added to the plan, reusing exit/risk roles. Current response remains notification-first for manual action, with existing protection independent of model/email availability. No monitor, live position import, alert or exit automation was implemented or run; the historical exit-helper tests do not validate this new monitoring loop or whether its recommendations improve returns or losses.

Later supplied specifications fix a native Windows Kronos Binance service using SQL Server, RTX 5080, initial BTCUSDT 15m/1h/4h inputs, 1d-5d forecasts and the existing C# execution system. Build/development and runtime research/monitoring are separate workflows sharing approved infrastructure, with independent permissions/state and coordinated resource use. The setup document now reflects this; SQL Server connection/schema and the distinct Amazon Chronos role remain unresolved. Shared-infrastructure isolation, resource scheduling, billing controls and Windows integrations were not implemented or tested by this rehearsal.

Only an isolated synthetic rehearsal and setup prompt were built there. Existing BTC research directories were inspected read-only; neither contained an agent workflow or email-delivery component suitable for reuse. No dependencies, repositories, services or live integrations were added.

## Executed checks

Environment: Python 3.10.12. Standard-library code only.

```
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest -v test_workflow
PYTHONDONTWRITEBYTECODE=1 python3 demo.py
```

The final full test run passed 58 test methods: the original 49 plus nine ad hoc research-routing checks. These are mocked contract checks, not agent-model performance tests.

- Valid BTC input reserves paper capacity without inventing a fill; valid options input remains advisory with no order.
- Missing, duplicate, unsupported, expired and mismatched assessments block new exposure.
- Stale/future quotes, unavailable historical news, changed account snapshots, nonfinite numbers and absent exit plans fail closed.
- Macro/geopolitical restrictions and unvalidated quant/event findings override a positive strategy assessment.
- Quantities respect the illustrative cash, liquidity, lot, trade, portfolio and existing/pending stress limits.
- Duplicate proposals/events, partial fills, unknown acknowledgements and repeated exits exercise in-memory state transitions.
- Protective exits do not need an LLM. They cap to known spot holdings and refuse to invent holdings when order state is uncertain. Existing venue-native protection is a design requirement, not exercised by this offline code.
- Reports distinguish blocked advice, paper orders and open simulated quantity, and flag stale/expired option suggestions.
- Email MIME/CSV composition, formula escaping, identity conflicts, definite rejection and ambiguous acceptance are checked with mock transports. Actual delivery is not tested.
- Toy correlation reverses across constructed train/holdout observations. This illustrates instability, not evidence about real trading correlations.

## Defects found and reproduced

The first run exposed a fixture constructor ignoring an unknown project value. The fixture was corrected so the intended allowlist rejection was exercised.

Read-only review then identified eight concrete implementation/reporting issues. New tests failed against the previous implementation before fixes:

1. Exit IDs were not bound to the exact original request.
2. Minimum spot stress loss omitted entry costs already represented in the fixture.
3. Option advice outlived quote/review/account validity.
4. A rejected proposal could inherit holdings from another proposal using the same ID.
5. Report reconciliation status ignored unknown order acknowledgements.
6. Outbox IDs silently accepted changed content.
7. Random MIME boundaries broke deduplication of regenerated identical reports.
8. Option advice remained current after its planned exit deadline.

All associated regressions pass in the final 58-test run. No claim of complete defect coverage.

The demo generated a readable .eml preview with CSV/JSON attachments and separate sample-report.json / sample-decisions.csv files. It produced one BTC paper reservation with a supplied partial fill, one options human-review suggestion, and one geopolitical block. Outbox remained PENDING; actual orders sent: 0; emails sent: 0.

## Ad hoc research extension

Nine additional tests exercise a pure planner using structured requests and supplied lookup results:

- Stock/news questions route to relevant specialists without an account or option-trade requirement.
- Ambiguous/unknown symbols request clarification; explicit historical windows and parent request IDs are preserved.
- Options ideas require a horizon and include pricing, sizing, exit and risk tasks. Macro/correlation tasks are optional.
- Research plans cannot enter the paper-order gate; planning leaves book/outbox state unchanged. Email preference produces preview-only routing, not a send.
- Invalid scopes, future cutoffs and execution/email-send job types are rejected.

Review found lookup validation consumed iterators before resolution. A new regression failed before the fix and passed after materialising lookup results once. Actual natural-language parsing, instrument lookup, follow-up authorisation, data fetching and agent answers were not exercised. Capabilities in the plan specify intended permissions; production tool access still needs independent enforcement.

## Synthetic language-model exercises

Three isolated tasks were performed with the originating environment's model, not Claude or a local specialised model. Prompts explicitly stated the safety requirements and desired output contract; these were qualitative rehearsals, not blind evaluation or independent model verification.

- News/event/macro/geopolitical roles received an unconfirmed shipping story copied by a second publisher, a future official release, malicious instructions inside source text, incomplete liquidation data, abnormal spreads and an upcoming rate announcement. Output treated repeated news as one unconfirmed cluster, excluded future evidence, ignored embedded instructions and blocked new BTC exposure because the configured playbook lacked normal liquidity/consolidation. Options advice was withheld without chains/exposure evidence.
- Risk role received the original evidence plus a calculator receipt showing a 2.5-unit capacity ceiling, while a strategy requested 10 units on "99% confidence". Output blocked new exposure, distinguished maximum size from authorisation, retained existing protection and rejected the claim that a single scenario proves tail safety.
- Quant role received reversed toy correlations, 50 tried variants, threshold tuning on holdout, revised news, unknown checkpoint training cutoff, overlapping labels and missing costs. Output rejected promotion and required point-in-time data, purged/embargoed validation, baselines and prospective evidence if contamination remained unresolved.

## Limits

- No actual Claude/Kronos calls, real news, market data, broker orders or emails.
- No complete end-to-end deployment of distinct model agents. Programmatic assessments are fixture values.
- No probability calibration, historical strategy backtest, profitability estimate or 99% VaR/expected-shortfall validation.
- No durable persistence, true concurrency, crash recovery, exchange cancellation races, SMTP/provider status reconciliation or inbox receipt.
- Pricing/size inputs are assumed code-tool receipts in the rehearsal; their real provenance/accuracy still needs integration testing.
- Simplified fictional spot fills, entry costs and one stress scenario. Exit fees, mark-to-market P&L, nonlinear options repricing and real order-book economics are not modelled.
- The source-text test proves text cannot directly override this deterministic fixture engine; it does not prove an LLM resists prompt injection.
- Event suppression lasts for this in-memory book. Production needs persisted event lifecycle/cooldown rules and explicit safe re-entry.

Result: useful evidence about bounded workflow contracts and identified failure cases, not evidence of a deployable or profitable trading system.
