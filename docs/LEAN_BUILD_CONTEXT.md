# Lean build context

Paste into Claude Code at the repository root. Keep CLAUDE_SETUP.md as the full requirements reference; do not paste it into every session.

## Goal

Build the system described in docs/CLAUDE_SETUP.md and the Binance/Kronos specifications with the fewest total tokens that still produce reviewed, tested, incremental changes. Minimise tokens per accepted change, including retries, repairs and reviews. Do not reduce verification, risk boundaries or evidence standards to save tokens.

## Operating rules

1. Existing code first. Read the repository instructions and trace the relevant existing flow before planning or editing. Reuse existing modules; propose replacements or restructures instead of performing them.
2. One milestone at a time. Plan a milestone once, produce bounded work packets, then implement packets sequentially. Do not re-plan the whole programme every session.
3. Deterministic work stays local. Code runs tests, builds, Git inspection, scheduling, checkpoints and usage checks. Models interpret results; they do not replace them.
4. Context on demand. Load only the packet, the named files, the relevant contracts and current test output. Never paste the whole specification, repository tree or prior transcript into a packet.
5. One builder per packet. Related packets go to the same builder in one session while context remains useful. Start a fresh session when the context is mostly stale or the packet changes subsystem.
6. Review once, where risk exists. Mechanical packets use tests plus a short read-only diff review. High-risk packets use one strong critical review. Do not run two full reviews for the same diff.
7. Escalate instead of looping. After two genuine failed attempts, stop and report the blocker, evidence and options. Do not ask the same model to retry with the same information.
8. Record state compactly. Update the packet checkpoint, test results, changed files and next action. Do not write new architecture documents unless the milestone requires a decision record.
9. Stop signals. Missing architectural decision, unexpected production-file change, new dependency, schema change, live-trading or notification change, failing acceptance checks: stop and report.
10. Honest completion. Mark complete only after acceptance checks pass and results are shown, with runtime limitations stated.

## Roles

Use three roles. Run them as separate bounded sessions/subagents; the same model may serve several roles.

| Role | When | Inputs | Output |
|---|---|---|---|
| Planner | Start of a milestone, or when a builder reports a blocker that changes design | docs/CLAUDE_SETUP.md relevant section, repository map, existing code paths, milestone goal | Ordered packets, decisions needed, risks, acceptance checks |
| Builder | Each packet | Packet, allowed files, contracts, current failing checks | Diff, test output, checkpoint, handoff |
| Reviewer | After builder reports completion | Diff, acceptance checks, test output, packet; read-only | PASS/FAIL with concrete evidence, max 5 findings |

Model guidance: use a capable coding model as builder; use the strongest available reasoning model for planner and high-risk review; use a cheap model only for repository exploration, log classification and recovery summaries. Model names, effort levels, billing behaviour and tool availability must be confirmed on the actual installation before unattended use. Stale or unknown usage/billing data blocks dispatch.

## Packet format

```
# <ID> <title>
Objective: one sentence.
Current behaviour: observed facts or failing check.
Allowed files: exact paths.
Forbidden: files, subsystems, dependencies, schema/live/notification changes.
Acceptance checks: exact commands/tests and expected results.
Risk: L0 mechanical | L1 routine | L2 complex | L3 critical.
Stop conditions: specific signals from Operating rules 9.
Context: only the files/contracts needed.
```

## Handoff format

```
TASK: <ID>
STATUS: COMPLETE | BLOCKED | PARTIAL
FILES CHANGED: paths
CHECKS RUN: commands
RESULTS: pass/fail with decisive line
ASSUMPTIONS: short list
KNOWN ISSUES: short list
NEXT SAFE ACTION: one line
REVIEW NEEDED: NONE | STANDARD | CRITICAL(reason)
```

## Risk routing

- L0/L1: builder plus tests; short read-only review of diff.
- L2: builder plus tests; standard review.
- L3: planner-defined acceptance checks; builder; critical review. L3 includes timestamp/as-of semantics, future-data leakage, replay/evaluation scoring, model checkpoint load/switch, GPU queue concurrency, order/exit accounting, risk limits, notification sending and anything touching live trading.

## Milestone order

1. Baseline: inspect existing repositories, run existing safe build/test commands, record results.
2. Vertical slice: closed-candle intake, validation, persistence, mocked forecast job, cached prediction retrieval, restart recovery. No real model required.
3. Real Kronos inference on Windows: single forecast, raw sample paths persisted, GPU status, serialized queue.
4. Historical replay and scoring with as-of inputs and leakage assertions.
5. Runtime agent integration: evidence verification, thesis monitoring and reports consuming versioned forecasts, notification-first.
6. Fine-tuning and model registry only after the zero-shot baseline is frozen.

Each milestone ends with a short planner review of evidence, not a full re-plan.

## Boundaries

- C# system remains execution authority. Forecast service returns forecasts, not trade commands.
- Build success never deploys, switches active checkpoints, runs production migrations, sends notifications or enables live trading.
- Agents never hold trading credentials or change approved risk limits.
- Options remain advisory; crypto runtime actions remain notification/manual until separately approved.
- Separate accounts keep independent budgets.

## Token budget rules

- Default effort for builders; raise effort only on L2/L3 blockers.
- Builder packets target one behaviour and under 10 files.
- Read files once per packet; grep before opening large files.
- Summaries under 15 lines; no restating the specification.
- Reviewer returns findings only; no rewritten code unless a one-line fix is clearer.
- Measure tokens per accepted packet by role; move packet classes to cheaper models only after they pass the same checks with fewer total tokens.

## Session start prompt

> Read docs/LEAN_BUILD_CONTEXT.md and the repository instructions. Act as planner for milestone <N> only. Inspect existing code paths relevant to <milestone goal>, cite file paths, and produce ordered packets in the packet format. Ask only unresolved blocking questions. Do not implement yet.

## Builder prompt

> Read docs/LEAN_BUILD_CONTEXT.md and packet <ID>. Implement only this packet in the allowed files. Run the acceptance checks and show results. Stop on any stop condition. End with the handoff format.

## Reviewer prompt

> Read docs/LEAN_BUILD_CONTEXT.md and packet <ID>. Review the diff and test output read-only. Return PASS or FAIL with at most 5 concrete findings, each with file:line and the failure it causes. No rewrites.
