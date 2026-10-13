# Kronos Autonomous Build Environment & Claude Code Agent Workflow

Saved from Ankur's planning conversation. Sections 1-76 are the original text. Section 77 lists review corrections that override the original where they conflict; the original text is kept unedited so the delta is visible.

Purpose: Set up a Windows development machine to build the Kronos Binance forecasting service autonomously with Claude Code, while controlling model usage, preserving work across usage resets, and using local compute wherever practical.

Target system: Windows; existing C# trading system; Microsoft SQL Server candle database; Binance Spot / BTCUSDT initially; Kronos inference at 15m, 1h, and 4h; 1d–5d forecasts; NVIDIA RTX 5080; local Kronos inference and eventual fine-tuning; REST integration between C# and the Python Kronos service.

## 1. Key Design Decision: Use the PC for Most Non-LLM Work

A separate local LLM is not required for the initial system.

The local machine should handle all deterministic and numerical work:

```
LOCAL WINDOWS MACHINE
├── Project orchestrator / scheduler
├── 30-minute watchdog
├── Claude usage-state capture
├── Git / worktrees / commits
├── Task state database
├── Test runner
├── C# builds and tests
├── Python builds and tests
├── SQL Server queries
├── Candle validation
├── Historical replay
├── Backtesting / scoring
├── Kronos inference
├── Kronos GPU benchmarking
└── Kronos fine-tuning
```

Claude models should be used only where reasoning, planning, coding, debugging, or review is useful.

```
REMOTE CLAUDE
├── Fable     → architecture / difficult decisions / critical review
├── Sonnet    → primary coding / implementation / normal review
└── Haiku     → cheap exploration / recovery / ambiguous status checks
```

### 1.1 No model is needed for most of the "nudger"

The 30-minute watchdog should first use ordinary local rules: Is the Claude process alive? Has stdout changed? Has Git changed? Has the task checkpoint changed? Have tests run? Has the task exceeded its maximum runtime? What is the 5-hour Claude usage percentage?

Only if those signals are ambiguous should it invoke the Haiku progress-monitor agent. This makes the watchdog nearly free.

### 1.2 Optional local LLM later

A local LLM can be added later for tasks such as classifying logs, summarizing failed tests, deciding whether a worker appears stuck, preparing recovery summaries. It is not needed for v1.

Running another LLM on the same GPU would also compete with Kronos for VRAM, so do not add one until Kronos GPU usage has been benchmarked.

## 2. Overall Build Architecture

```
                           BUILD_SPEC.md
                                |
                                v
                       LOCAL ORCHESTRATOR
                       Python / no LLM
                                |
              +-----------------+-----------------+
              |                 |                 |
              v                 v                 v
         TASK SCHEDULER    USAGE MANAGER       WATCHDOG
              |                 |                 |
              |                 |          every 30 minutes
              |                 |                 |
              +-----------------+-----------------+
                                |
                                v
                        MODEL / TASK ROUTER
                                |
           +--------------------+--------------------+
           |                    |                    |
           v                    v                    v
         HAIKU                SONNET               FABLE
       utilities             builders             director
           |                    |                    |
           +--------------------+--------------------+
                                |
                                v
                             GIT CODE
                                |
                                v
                              TESTS
                                |
                                v
                            REVIEW GATE
```

Claude is a worker. The local orchestrator owns the workflow. No Claude conversation is the authoritative source of project state.

## 3. Authoritative Project State

The repository should contain:

```
.workflow/
├── runtime/
│   ├── usage.json
│   ├── supervisor.json
│   └── workers/
├── tasks/
│   ├── KRONOS-001.md
│   ├── DATA-001.md
│   └── ...
├── handoffs/
├── reviews/
└── orchestrator.db

docs/
├── BUILD_SPEC.md
├── ARCHITECTURE.md
├── API.md
├── DATA_TIME_SEMANTICS.md
├── DATABASE.md
├── RESEARCH.md
├── FINE_TUNING.md
├── WINDOWS_SETUP.md
└── DECISIONS.md

STATUS.md
KNOWN_ISSUES.md
CHANGELOG.md
CLAUDE.md
```

Durable state is stored in files, SQLite, and Git. A Claude session may be lost without losing the project.

## 4. Windows Prerequisites

Install or verify: Git for Windows, Python 3.11, NVIDIA driver, .NET SDK compatible with the existing C# trading system, Microsoft ODBC Driver for SQL Server, Claude Code, optional Visual Studio / VS Code.

Python 3.11 is recommended because it is compatible with Kronos's Python 3.10+ requirement and with the current supported Windows range for PyTorch.

### 4.1 Verify prerequisites

```
git --version
python --version
dotnet --version
nvidia-smi
```

Do not proceed until nvidia-smi sees the RTX 5080 correctly.

## 5. Install Claude Code

Current recommended native Windows installation:

```
irm https://claude.ai/install.ps1 | iex
```

Alternative: `winget install Anthropic.ClaudeCode`

Verify: `claude --version`. Update before configuring agents: `claude update`. Start once interactively: `claude`, complete the login flow, then run `/status` and `/model`. Confirm that the account exposes the required models.

## 6. Verify Fable Before Automation

Claude Code currently provides the model aliases: fable, opus, sonnet, haiku.

Use `/model` and inspect the Fable entry.

**IMPORTANT: Fable billing guard.** Depending on the Claude plan or seat, Fable can display "Requires usage credits". If it does, unattended Fable calls must not be enabled accidentally.

In interactive sessions Claude Code asks before using paid usage credits. In non-interactive `claude -p` calls and Agent SDK workflows, a Fable request that requires usage credits can be billed without showing that interactive consent prompt.

Therefore the orchestrator configuration must default to:

```yaml
billing:
  allow_usage_credits: false
  allow_headless_fable: false
```

Only change this deliberately after checking the account. If Fable is unavailable or requires credits you do not want to use, configure:

```yaml
models:
  director: opus
  critical_reviewer: opus
```

## 7. Create the Project Repository

```
mkdir C:\dev\kronos-trading
cd C:\dev\kronos-trading
git init
mkdir docs src tests scripts config research training migrations third_party
mkdir .workflow .workflow\tasks .workflow\handoffs .workflow\reviews .workflow\runtime
mkdir .claude .claude\agents
```

Copy the existing Kronos build specification into `docs/BUILD_SPEC.md`. Commit the initial structure.

## 8. Download Kronos

```
git clone https://github.com/shiyu-coder/Kronos.git third_party\Kronos
git -C third_party\Kronos rev-parse HEAD
```

Store this hash in `docs/KRONOS_UPSTREAM.md`. Do not casually modify the upstream checkout. Project-specific changes should normally live under `src/kronos/`. If an upstream patch is unavoidable, document it in `docs/KRONOS_PATCHES.md`.

## 9. Create the Python Environment

```
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip setuptools wheel
```

## 10. Install PyTorch With CUDA

Do not blindly rely on the generic `torch>=2.0` line from Kronos for the initial GPU install. Use the official PyTorch installer selector (Windows / Pip / Python / CUDA) and choose the current stable CUDA option compatible with the installed NVIDIA driver. Run the generated command inside `.venv`.

After installation:

```
python -c "import torch; print(torch.__version__); print('CUDA:', torch.cuda.is_available()); print('Device:', torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'NONE'); print('VRAM GB:', torch.cuda.get_device_properties(0).total_memory / 1024**3 if torch.cuda.is_available() else 0)"
```

Required result: `CUDA: True`, `Device: NVIDIA GeForce RTX 5080`. If CUDA is false, stop. Do not continue into Kronos integration until PyTorch can use the GPU.

## 11. Install Kronos Dependencies

```
pip install -r third_party\Kronos\requirements.txt
pip install fastapi "uvicorn[standard]" pydantic-settings sqlalchemy pyodbc pytest pytest-asyncio httpx psutil pyyaml rich
pip freeze > requirements.lock.txt
```

## 12. Download and Cache Kronos-base

Create `scripts/download_kronos.py`:

```python
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
KRONOS = ROOT / "third_party" / "Kronos"
sys.path.insert(0, str(KRONOS))

from model import Kronos, KronosTokenizer

print("Downloading/loading tokenizer...")
tokenizer = KronosTokenizer.from_pretrained("NeoQuasar/Kronos-Tokenizer-base")

print("Downloading/loading Kronos-base...")
model = Kronos.from_pretrained("NeoQuasar/Kronos-base")

print("Kronos-base and tokenizer are available.")
```

Run `python scripts\download_kronos.py`. The Hugging Face cache will retain the downloaded model. Kronos-base currently has a 512-bar context and approximately 102.3M parameters.

## 13. Run the Official Kronos Example

```
Push-Location third_party\Kronos
python examples\prediction_example.py
Pop-Location
```

This is an environment proof only. Do not start building the service until a normal Kronos forecast succeeds.

## 14. Environment Gate 0

The project cannot advance until:

- [ ] Windows Python environment created
- [ ] Git working
- [ ] NVIDIA driver working
- [ ] RTX 5080 visible to nvidia-smi
- [ ] PyTorch sees CUDA
- [ ] Kronos repository downloaded
- [ ] Kronos dependencies installed
- [ ] Kronos tokenizer downloads
- [ ] Kronos-base downloads
- [ ] one official forecast succeeds
- [ ] environment commands documented

Create `.workflow/tasks/ENV-001.md` and record the exact versions used.

## 15. Claude Usage Monitoring — Use Real Rate-Limit Data

Claude Code's local status-line input can expose `rate_limits.five_hour.used_percentage`, `rate_limits.five_hour.resets_at`, `rate_limits.seven_day.used_percentage`, `rate_limits.seven_day.resets_at` for Claude.ai subscribers where those fields are available.

This is subscription allowance usage, not context-window usage. Do not confuse `context_window.used_percentage` with `rate_limits.five_hour.used_percentage`.

The status-line command runs locally and does not consume Claude API/model tokens. This is the preferred source for the 80%/90% usage controls.

## 16. Build a Local Usage Capture Status Line

Create `scripts/claude_status.py`:

```python
import json
import os
import sys
import time
from pathlib import Path

data = json.load(sys.stdin)

workspace = data.get("workspace", {})
project_dir = Path(
    workspace.get("project_dir")
    or workspace.get("current_dir")
    or os.getcwd()
)

runtime_dir = project_dir / ".workflow" / "runtime"
runtime_dir.mkdir(parents=True, exist_ok=True)

rate_limits = data.get("rate_limits") or {}
five = rate_limits.get("five_hour") or {}
seven = rate_limits.get("seven_day") or {}

context = data.get("context_window") or {}
model = data.get("model") or {}

snapshot = {
    "captured_at_unix": int(time.time()),
    "session_id": data.get("session_id"),
    "model_id": model.get("id"),
    "model_name": model.get("display_name"),
    "effort": (data.get("effort") or {}).get("level"),
    "context_used_percent": context.get("used_percentage"),
    "five_hour_used_percent": five.get("used_percentage"),
    "five_hour_resets_at": five.get("resets_at"),
    "seven_day_used_percent": seven.get("used_percentage"),
    "seven_day_resets_at": seven.get("resets_at"),
}

target = runtime_dir / "usage.json"
temp = runtime_dir / "usage.json.tmp"

temp.write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
temp.replace(target)

five_pct = snapshot["five_hour_used_percent"]
seven_pct = snapshot["seven_day_used_percent"]
ctx_pct = snapshot["context_used_percent"]

def fmt(value):
    return "?" if value is None else f"{float(value):.0f}%"

print(
    f"[{snapshot['model_name'] or 'Claude'}] "
    f"5h:{fmt(five_pct)} "
    f"7d:{fmt(seven_pct)} "
    f"ctx:{fmt(ctx_pct)}"
)
```

## 17. Configure the Claude Status Line

Add to `.claude/settings.json`:

```json
{
  "statusLine": {
    "type": "command",
    "command": "python scripts/claude_status.py",
    "refreshInterval": 60
  }
}
```

After starting Claude Code and making at least one request, verify `Get-Content .workflow\runtime\usage.json`. Expected shape:

```json
{
  "five_hour_used_percent": 23.5,
  "five_hour_resets_at": 1738425600,
  "seven_day_used_percent": 41.2
}
```

The rate_limits fields can be absent depending on authentication/account state. The orchestrator must handle null or missing values.

## 18. Usage Policy

```yaml
usage:
  caution_percent: 80
  drain_percent: 90
  watchdog_minutes: 30
  unknown_usage_behavior: conservative
  resume:
    automatic: true
    reset_safety_seconds: 120
```

State machine: 0–79% NORMAL; 80–89% CONSERVATION; 90–99% DRAIN; 100% / limit error WAITING_FOR_CAPACITY.

## 19. NORMAL Mode

Allowed: Haiku, Sonnet medium, Sonnet high, Fable when justified and billing policy allows, new tasks, normal reviews.

## 20. CONSERVATION Mode — >=80%

Do not start Fable unless resolving a critical blocker. Do not start a large new Sonnet-high task if a smaller useful task exists. Finish the active work unit. Run tests. Checkpoint more frequently. Prefer Haiku for exploration. Prefer Sonnet medium for routine implementation.

## 21. DRAIN Mode — >=90%

No new implementation task starts. The active worker must:

1. Finish the smallest safe coherent edit.
2. Stop expanding scope.
3. Run the directly relevant tests.
4. Update its task checkpoint.
5. Record known failures.
6. Commit coherent work if tests permit.
7. Produce a handoff.
8. Exit.

The last ~10% of allowance is treated as a landing reserve.

## 22. WAITING_FOR_CAPACITY

```
RUNNING -> LIMIT DETECTED -> SAVE STATE -> WAITING_FOR_CAPACITY
   (use resets_at if available; otherwise periodic local probe/retry)
-> CAPACITY RETURNS -> RESUME TASK
```

No human "continue" message should be required. If `five_hour_resets_at` is known, schedule automatic resume shortly after it. If the session cannot be resumed, launch a fresh worker using CLAUDE.md, task checkpoint, Git state, last test state, handoff.

## 23. The 30-Minute Watchdog

The watchdog is a local Python process running every 30 minutes. First use deterministic checks: worker process alive? last output timestamp? last Git modification? last task-checkpoint update? last test timestamp? current usage state?

Decision: healthy → do nothing; possibly stalled → ask Haiku progress monitor; clearly stalled → terminate/recover according to policy; task complete → schedule review; usage >= 90% → drain; usage exhausted → wait until reset.

Do not send a Claude "continue" prompt every 30 minutes. That wastes allowance and repeatedly reloads context.

## 24. Stalled Worker Policy

```yaml
watchdog:
  interval_minutes: 30
  stalled:
    no_output_minutes: 30
    no_git_activity_minutes: 60
  max_nudges: 2
  restart_after_failed_nudges: true
```

Flow: 30m inactivity → local checks → ambiguous? → Haiku classification → PROGRESSING (leave alone) | STALLED (nudge/recovery) | COMPLETE (review) | ESCALATE (Sonnet/Fable).

## 25. Agent Model Strategy

Use model aliases rather than hard-coded model version IDs unless exact reproducibility requires pinning.

### 25.1 Build Director

Model: fable. Effort: high. Escalation effort: xhigh. Edits code: normally NO. Frequency: milestone / blocker only.

Responsibilities: architecture; milestone planning; task decomposition; dependency decisions; major technical tradeoffs; resolving contradictory reviewer findings; approving major phase transitions.

Fable should not continuously supervise Sonnet.

## 26. Primary Builder

Model: sonnet. Effort: high. Isolation: worktree.

Use for: Kronos integration; GPU queue; SQL integration; historical replay; forecasting engine; complex REST implementation; fine-tuning implementation.

## 27. Routine Builder

Model: sonnet. Effort: medium. Isolation: worktree.

Use for: DTOs; straightforward endpoints; configuration structures; tests; migrations; serialization; ordinary refactoring.

Use Sonnet low where a task is clearly mechanical and supported by the active Sonnet model.

## 28. Reviewer

Model: sonnet. Effort: high. Read-only.

Every implementation ticket should be reviewed before merge. Review: acceptance criteria; unnecessary changes; test coverage; API contract; errors; maintainability; race conditions; obvious data leakage.

## 29. Critical Reviewer

Model: fable. Effort: xhigh. Read-only.

Use only for: timestamp semantics; future-data leakage; historical replay; training/test split; model checkpoint correctness; stochastic forecast extraction; CUDA architecture; major architecture changes; conclusions that Kronos has predictive value.

If Fable is not permitted by billing policy, use Opus high/xhigh as the fallback.

## 30. Explorer

Model: haiku. Read-only. Max turns: small.

Use for: locating files; identifying implementations; checking configuration; summarizing repository structure; finding references.

Do not use the built-in Explore agent if it would inherit an expensive parent model; define a project Explorer explicitly pinned to Haiku.

## 31. Progress Monitor

Model: haiku. Read-only. maxTurns: 3. Only called when the deterministic watchdog cannot classify progress.

Expected result schema:

```json
{
  "state": "PROGRESSING | STALLED | WAITING | COMPLETE | ESCALATE",
  "reason": "short reason",
  "action": "NONE | NUDGE | RECOVER | REVIEW | ESCALATE"
}
```

## 32. Recovery Agent

Model: haiku. Read-only. maxTurns: 5.

Its only job is to reconstruct concise state after an interrupted worker: What was being done? What files changed? What tests passed? What tests failed? What remains? What is the safest next action?

It does not fix the task. A Sonnet worker receives its recovery summary.

## 33. Research Analyst

Model: sonnet. Effort: high.

Use for: forecast evaluation; 1d–5d accuracy analysis; target/timing analysis; confidence calibration; comparing 15m, 1h, and 4h; interpreting experimental results.

Critical statistical conclusions get a Fable/Opus review.

## 34. Fine-Tuning Planner

Model: fable. Effort: xhigh. Read-only planning.

Use to design: chronological splits; Windows single-GPU adaptation; training dataset rules; checkpoint validation; zero-shot vs fine-tuned experiment. Implementation goes to Sonnet-high.

## 35. Create Claude Project Agents

Store custom agents in `.claude/agents/`: build-director.md, primary-builder.md, routine-builder.md, reviewer.md, critical-reviewer.md, explorer.md, progress-monitor.md, recovery.md, research-analyst.md, finetuning-planner.md.

## 36. Example: Build Director Agent

`.claude/agents/build-director.md`

```markdown
---
name: build-director
description: Project-level architecture, milestone planning, dependency decisions, and hard blocker resolution.
model: fable
effort: high
tools: Read, Glob, Grep
maxTurns: 20
permissionMode: plan
---

You are the principal architect for the Kronos forecasting project.

Do not implement ordinary code.

Read:
- docs/BUILD_SPEC.md
- STATUS.md
- docs/DECISIONS.md
- current milestone/task state

Your job is to:
1. protect the fixed requirements;
2. identify dependencies;
3. create bounded implementation tasks;
4. resolve major architectural blockers;
5. decide whether milestone acceptance evidence is sufficient.

Never silently change a requirement.

If a requirement must change, propose a decision record instead.
```

If automated Fable is disallowed by billing policy, create an Opus version or change `model: opus`.

## 37. Example: Primary Builder

`.claude/agents/primary-builder.md`

```markdown
---
name: primary-builder
description: Implement complex project tickets with tests while staying strictly inside the assigned scope.
model: sonnet
effort: high
tools: Read, Glob, Grep, Edit, Write, Bash
maxTurns: 35
permissionMode: acceptEdits
isolation: worktree
---

Read the assigned task file first.

docs/BUILD_SPEC.md is authoritative.

Implement only the assigned ticket.

Before reporting completion:
1. run relevant tests;
2. update the task checkpoint;
3. record assumptions;
4. record known issues;
5. make no unrelated refactors.

Never introduce future-data leakage.

Never change trading/research assumptions merely to make a test pass.
```

## 38. Example: Routine Builder

`.claude/agents/routine-builder.md`

```markdown
---
name: routine-builder
description: Implement bounded routine code, tests, DTOs, configuration, migrations, and simple endpoints.
model: sonnet
effort: medium
tools: Read, Glob, Grep, Edit, Write, Bash
maxTurns: 20
permissionMode: acceptEdits
isolation: worktree
---

Implement only the assigned bounded task.

Prefer the simplest implementation that satisfies the acceptance tests.

Run relevant tests and update the task checkpoint before stopping.
```

## 39. Example: Reviewer

`.claude/agents/reviewer.md`

```markdown
---
name: reviewer
description: Review completed implementation tickets for correctness, scope, regressions, and acceptance criteria.
model: sonnet
effort: high
tools: Read, Glob, Grep, Bash
disallowedTools: Edit, Write
maxTurns: 15
permissionMode: plan
---

Do not edit code.

Attempt to invalidate the implementation.

Check:
- task acceptance criteria;
- tests;
- regressions;
- API contracts;
- error handling;
- concurrency;
- data semantics;
- future leakage.

Return PASS or FAIL with concrete evidence.
```

## 40. Example: Critical Reviewer

`.claude/agents/critical-reviewer.md`

```markdown
---
name: critical-reviewer
description: Deep review of high-risk architecture, data leakage, forecast evaluation, training methodology, and GPU/model correctness.
model: fable
effort: xhigh
tools: Read, Glob, Grep, Bash
disallowedTools: Edit, Write
maxTurns: 20
permissionMode: plan
---

Do not edit code.

Your objective is to find reasons the result may be invalid.

Prioritize:
- look-ahead leakage;
- timestamp mistakes;
- overlapping-sample statistical errors;
- test contamination;
- checkpoint/reload failures;
- Kronos sampling errors;
- concurrency errors;
- unsupported assumptions.

PASS only when the evidence supports moving to the next gate.
```

## 41. Example: Explorer

`.claude/agents/explorer.md`

```markdown
---
name: explorer
description: Cheap read-only repository exploration and file discovery.
model: haiku
tools: Read, Glob, Grep
maxTurns: 8
permissionMode: plan
---

Locate the requested information with minimal exploration.

Return paths and a concise summary.

Do not propose architecture unless asked.
```

## 42. Example: Progress Monitor

`.claude/agents/progress-monitor.md`

```markdown
---
name: progress-monitor
description: Cheap read-only classification of whether a worker is progressing, stalled, complete, or needs escalation.
model: haiku
tools: Read, Glob, Grep
maxTurns: 3
permissionMode: plan
---

You receive a task ID and a small status snapshot.

Do not inspect the entire repository unless necessary.

Return only:

{
  "state": "PROGRESSING | STALLED | WAITING | COMPLETE | ESCALATE",
  "reason": "...",
  "action": "NONE | NUDGE | RECOVER | REVIEW | ESCALATE"
}
```

## 43. Example: Recovery Agent

`.claude/agents/recovery.md`

```markdown
---
name: recovery
description: Reconstruct a concise task state after an interrupted or expired Claude session.
model: haiku
tools: Read, Glob, Grep
maxTurns: 5
permissionMode: plan
---

Read:
- assigned task checkpoint;
- handoff;
- current git status supplied to you;
- relevant test summary.

Return:
- completed work;
- incomplete work;
- current failures;
- next safe action.

Do not implement fixes.
```

## 44. Root CLAUDE.md

Keep this short. Example:

```markdown
# Kronos Project Rules

1. Read the assigned task file before acting.
2. `docs/BUILD_SPEC.md` is authoritative.
3. Do not silently change requirements.
4. Never use future candles in historical inference.
5. SQL timestamps must follow `docs/DATA_TIME_SEMANTICS.md`.
6. Do not modify upstream Kronos without documenting the patch.
7. Run relevant tests before reporting success.
8. Update the task checkpoint before ending a work unit.
9. Do not mark a task complete while acceptance tests fail.
10. Avoid unrelated refactoring.
11. New architectural decisions must be recorded in `docs/DECISIONS.md`.
12. At usage drain state, stop new implementation and land the current work safely.
```

Do not turn CLAUDE.md into the entire project specification. Large stable specifications belong under docs/.

## 45. Complexity Routing

Classify every ticket: L0 mechanical, L1 routine, L2 complex, L3 critical.

| Level | Typical model | Effort |
|---|---|---|
| L0 | Haiku or Sonnet | low/none |
| L1 | Sonnet | medium |
| L2 | Sonnet | high |
| L3 | Fable / Opus fallback | high or xhigh |

Examples. L0: locate file, summarize status, small documentation update. L1: health endpoint, DTO, config validation, ordinary test. L2: GPU queue, historical replay, forecast cache, SQL synchronization. L3: timestamp semantics, leakage design, fine-tuning methodology, major architecture, incorrect Kronos sampling.

## 46. Escalation Policy

Do not repeatedly waste the same model on a failed problem.

```
Sonnet medium --(two genuine failures)--> Sonnet high --(unresolved / architectural)--> Fable high --(extremely high-risk decision)--> Fable xhigh
```

max effort should not be automatic. Use it only after an explicit project decision.

## 47. Task Format

```markdown
# API-003 — Candle Update Endpoint

## Objective
Implement POST /api/v1/candles/{symbol}/{timeframe}.

## Complexity
L1

## Assigned Agent
routine-builder

## Allowed Files
- src/api/candles.py
- src/api/models/candle.py
- tests/api/test_candles.py

## Dependencies
- DATA-003

## Acceptance Criteria
- Valid closed candle accepted
- Invalid OHLC rejected
- Unsupported timeframe rejected
- Duplicate handling follows data contract
- Unit tests pass

## Must Not Change
- Kronos model code
- SQL schema
- Prediction output contract

## Status
READY

## Handoff
Not started.
```

This keeps context small and reduces Claude usage.

## 48. Agent Handoff Format

Every worker ends with:

```
TASK:
STATUS:
FILES CHANGED:
TESTS RUN:
TEST RESULTS:
COMMITS:
ASSUMPTIONS:
KNOWN ISSUES:
NEXT SAFE ACTION:
REQUIRES REVIEW:
```

Save this under `.workflow/handoffs/<TASK-ID>.md`.

## 49. Git Checkpoint Policy

AI context is never the only record of unfinished work. After each coherent work unit: implement → test → checkpoint → commit.

Suggested commit format:

```
API-003: implement candle request validation
KRONOS-004: expose raw forecast path structure
DATA-004: enforce candle timestamp semantics
```

Do not create giant multi-feature commits.

## 50. Orchestrator Configuration

Create `config/orchestrator.yaml`:

```yaml
models:
  director: fable
  director_effort: high
  primary_builder: sonnet
  primary_builder_effort: high
  routine_builder: sonnet
  routine_builder_effort: medium
  reviewer: sonnet
  reviewer_effort: high
  critical_reviewer: fable
  critical_reviewer_effort: xhigh
  explorer: haiku
  progress_monitor: haiku
  recovery: haiku

billing:
  allow_usage_credits: false
  allow_headless_fable: false

usage:
  caution_percent: 80
  drain_percent: 90
  watchdog_minutes: 30
  reset_safety_seconds: 120

workers:
  max_heavy_concurrency: 1
  max_light_concurrency: 1
  checkpoint_after_work_unit: true
  commit_after_work_unit: true

stalled:
  no_output_minutes: 30
  no_git_activity_minutes: 60
  max_nudges: 2

gpu:
  concurrent_kronos_jobs: 1
```

## 51. Why Heavy Claude Concurrency Starts at 1

The Claude allowance is shared. Multiple simultaneous coding agents can consume usage extremely quickly. Initially: 1 heavy builder + optional 1 cheap utility/reviewer is safer than 6 continuously active agents. Parallelism should increase only after measuring actual usage.

## 52. Worktrees

Builders should operate in isolated Git worktrees. Claude custom subagents support `isolation: worktree` for appropriate agents. Reviewers should normally remain read-only and do not need a worktree. Do not allow multiple builders to edit the same subsystem simultaneously.

## 53. Build Workflow

```
FABLE DIRECTOR -> define milestone -> LOCAL ORCHESTRATOR -> select READY task
-> route by complexity (Haiku | Sonnet | Fable) -> implementation -> tests
-> checkpoint / commit -> SONNET REVIEW (fail -> builder)
-> critical? no -> merge | yes -> FABLE CRITICAL REVIEW -> merge
```

## 54. Milestone Workflow

```
M0 ENVIRONMENT -> M1 KRONOS CORE -> M2 DATA LAYER -> M3 REST SERVICE
   -> M4A REPLAY + M4B GPU BENCHMARK -> M5 EVALUATION
   -> ZERO-SHOT DECISION GATE (evidence useful? no -> investigate | yes -> M6 FINE-TUNE -> ZERO-SHOT vs FT)
```

## 55. Milestone 0 — Environment

Tasks: ENV-001 Verify Windows prerequisites; ENV-002 Install Claude Code; ENV-003 Configure usage capture; ENV-004 Create Python venv; ENV-005 Install CUDA-enabled PyTorch; ENV-006 Clone Kronos; ENV-007 Download Kronos-base; ENV-008 Run official forecast.

Gate:

- [ ] Claude Code works
- [ ] rate-limit usage snapshot works or absence is documented
- [ ] Python venv works
- [ ] CUDA works
- [ ] Kronos-base loads
- [ ] official prediction works

## 56. Milestone 1 — Kronos Core

Tasks: KRONOS-001 model manager; KRONOS-002 predictor abstraction; KRONOS-003 timeframe input adapter; KRONOS-004 raw stochastic path extraction; KRONOS-005 GPU serial worker; KRONOS-006 forecast metadata; KRONOS-007 model-loading tests.

Critical review: Kronos sampling correctness; timestamps; no duplicated GPU models unless intended.

## 57. Milestone 2 — Data

Tasks: DATA-001 SQL Server configuration; DATA-002 candle loader; DATA-003 candle validation; DATA-004 timestamp semantics; DATA-005 rolling buffers; DATA-006 1m evaluation loader; DATA-007 restart/reconciliation.

Critical review required for: DATA-004, historical as-of behavior, future-data prevention.

## 58. Milestone 3 — REST Service

Tasks: API-001 FastAPI app; API-002 health/readiness; API-003 candle update; API-004 prediction retrieval; API-005 explicit prediction jobs; API-006 job status; API-007 runtime configuration; API-008 GPU/model status; API-009 persistence; API-010 C# integration test client.

## 59. Milestone 4 — Research Infrastructure

Tasks: RESEARCH-001 historical replay; RESEARCH-002 replay resume; RESEARCH-003 forecast cache; RESEARCH-004 stochastic persistence; RESEARCH-005 GPU benchmark; RESEARCH-006 1m target evaluator.

## 60. Milestone 5 — Evaluation

Tasks: EVAL-001 1d–5d direction; EVAL-002 magnitude error; EVAL-003 price-zone hit; EVAL-004 timing tolerance; EVAL-005 MFE/MAE; EVAL-006 confidence calibration; EVAL-007 overlapping-sample controls; EVAL-008 15m vs 1h vs 4h.

Fable/Opus critical review must approve any statement that Kronos has meaningful predictive value.

## 61. Milestone 6 — Fine-Tuning

Only start after the zero-shot baseline is frozen.

Tasks: FT-001 dataset snapshot and hash; FT-002 chronological split; FT-003 Windows single-GPU tokenizer training; FT-004 Windows single-GPU predictor training; FT-005 checkpoint save/reload validation; FT-006 model registry; FT-007 zero-shot vs fine-tuned comparison.

No final-test data may enter training or parameter selection.

## 62. Local GPU Strategy

The RTX 5080 should initially be used for Kronos inference, Kronos benchmark, Kronos fine-tuning. Do not add a local LLM to the GPU at first.

The RTX 5080 has to support long Kronos workloads such as 15m × 480 forecast steps, 1h × 120, 4h × 30, with stochastic sampling. Measure real peak VRAM before allocating GPU capacity to anything else.

## 63. GPU Queue

Only one Kronos CUDA inference/training workload initially: 15m inference → 1h inference → 4h inference.

Training is exclusive: training active → no competing local LLM → no concurrent Kronos training job.

## 64. Optional Local LLM Phase

Only investigate after GPU benchmarks. Possible uses: test failure classification, log summarization, watchdog ambiguity, recovery summaries.

Do not use it for architecture, financial research conclusions, critical leakage review, major code generation unless independently validated. This local model is optional. The v1 system does not need it.

## 65. Claude Session Recovery

Claude Code supports session IDs and session resumption. For automation, store: task ID, Claude session ID, branch/worktree, last commit, checkpoint, last test result.

When a worker stops unexpectedly: attempt resume → succeeds → continue; fails → Haiku recovery → new Sonnet worker. The new worker must not need the old conversation.

## 66. Important Rule for Headless Fable

If the orchestrator eventually uses `claude -p` or the Agent SDK for unattended work, enforce the billing guard before selecting Fable. Default: headless Fable = disabled unless the user has explicitly approved the billing behavior. Sonnet should be the normal unattended coding worker.

## 67. Automated Usage Recovery

usage < 80 → normal dispatch; 80 <= usage < 90 → conservation; usage >= 90 → drain active worker, stop dispatch; limit reached → WAITING_FOR_CAPACITY; resets_at known → schedule wake at reset + safety delay; capacity available → recover/resume highest-priority incomplete task.

The supervisor itself keeps running locally even while Claude is unavailable.

## 68. Supervisor Is Not an AI Agent

`supervisor.py` should perform deterministic operations: read task DB, read usage JSON, inspect process, inspect timestamps, inspect Git state, launch worker, kill irrecoverably stalled worker, schedule retry, write logs.

Only delegate an ambiguous judgement to an LLM. This reduces cost and makes the workflow predictable.

## 69. Supervisor Safety Rules

The supervisor must never automatically: `git reset --hard`, `git clean`, delete uncommitted work, force-push, merge a failed review, alter BUILD_SPEC.md, enable paid Claude usage, start live trading, write trading credentials.

These are outside normal autonomous authority.

## 70. Initial Orchestrator Build Backlog

Before building Kronos features, create: ORCH-001 task-state model; ORCH-002 SQLite task store; ORCH-003 local process runner; ORCH-004 usage.json reader; ORCH-005 usage state machine; ORCH-006 30-minute watchdog; ORCH-007 Git activity tracker; ORCH-008 session ID persistence; ORCH-009 worker resume/recovery; ORCH-010 model router; ORCH-011 billing guard; ORCH-012 structured logs.

Then run a harmless dummy project through the workflow before allowing it to build Kronos.

## 71. Orchestrator Acceptance Test

1. Create TEST-001.
2. Scheduler assigns Sonnet.
3. Worker starts.
4. Worker changes a test file.
5. Worker checkpoints.
6. Reviewer runs.
7. Task completes.
8. Simulate usage = 85%.
9. Scheduler enters CONSERVATION.
10. Simulate usage = 91%.
11. Scheduler enters DRAIN.
12. No new task starts.
13. Worker writes handoff.
14. Simulate limit reached.
15. State becomes WAITING_FOR_CAPACITY.
16. Simulate reset.
17. Task automatically resumes.
18. Complete and review.

Do this before entrusting the workflow with the real implementation.

## 72. Recommended Starting Order

1. Set up Windows prerequisites.
2. Install and verify Claude Code.
3. Verify Fable availability and billing behavior.
4. Configure rate-limit status capture.
5. Create repository and task-state structure.
6. Create Claude custom agents.
7. Build the local orchestrator/watchdog.
8. Test pause/drain/reset/resume using dummy tasks.
9. Create Python CUDA environment.
10. Clone/download Kronos.
11. Verify Kronos-base locally.
12. Start Kronos Milestone 1.

Do not start autonomous Kronos development until Steps 1–11 are proven.

## 73. Final Operating Model

```
USER -> sets requirements -> BUILD_SPEC.md -> LOCAL ORCHESTRATOR (no LLM tokens used)
  -> USAGE STATE | WATCHDOG | TASK STORE -> TASK ROUTER
  -> HAIKU utilities | SONNET coding | FABLE architecture
  -> GIT -> TEST -> REVIEW -> NEXT BUILD GATE
```

Use the Windows machine for orchestration, state, tests, SQL, Kronos inference, evaluation, and training. Use Claude only for the reasoning work that benefits from an LLM. Do not add a separate local LLM until there is evidence that it solves a real bottleneck.

## 74. Verified Upstream References

The setup should be rechecked against these sources when implementation begins because Claude Code and PyTorch change frequently.

- Claude Code Quickstart / Windows installation: https://code.claude.com/docs/en/quickstart
- Claude Code model aliases, Fable, effort and billing behavior: https://code.claude.com/docs/en/model-config
- Claude Code custom subagents: https://code.claude.com/docs/en/sub-agents
- Claude Code status line and rate-limit fields: https://code.claude.com/docs/en/statusline
- Claude Code CLI/headless session continuation: https://code.claude.com/docs/en/headless
- Claude usage-limit information: https://support.claude.com/en/articles/11145838-use-claude-code-with-your-pro-or-max-plan
- Official Kronos repository: https://github.com/shiyu-coder/Kronos
- PyTorch Windows/CUDA installer: https://pytorch.org/get-started/locally/

## 75. First Human Decisions Before Full Autonomy

Before starting an unattended run, explicitly decide:

- [ ] Is Fable included in the account allowance?
- [ ] If not, are usage credits allowed?
- [ ] What maximum paid usage, if any, is acceptable?
- [ ] Is headless Fable allowed?
- [ ] Are agents permitted to commit automatically?
- [ ] Are agents permitted to create Git worktrees?
- [ ] Are agents permitted to install Python packages?
- [ ] Are agents allowed read-only SQL access?
- [ ] What directories are agents forbidden to touch?

Until answered, safe defaults are:

```yaml
billing:
  allow_usage_credits: false
  allow_headless_fable: false

git:
  auto_commit: true
  force_push: false
  destructive_reset: false

sql:
  agent_write_access: false

trading:
  live_execution_access: false
```

## 76. Definition of a Successful Setup

- [ ] Claude Code installed and authenticated
- [ ] Fable billing behavior known
- [ ] custom agents visible to Claude Code
- [ ] usage.json receives real rate-limit data
- [ ] 80% conservation works
- [ ] 90% drain works
- [ ] reset time is persisted
- [ ] supervisor can resume work automatically
- [ ] Git/task checkpoints survive session loss
- [ ] Python venv works
- [ ] PyTorch CUDA works on RTX 5080
- [ ] official Kronos-base loads
- [ ] official Kronos forecast succeeds
- [ ] dummy autonomous task completes through review

At that point the environment is ready to begin building the real Kronos service.

## 77. Review Corrections (override the above where they conflict)

Added after read-only review. Apply these before unattended operation.

1. **§75 default contradicts its own question.** `auto_commit: true` grants commit permission before the human has answered whether commits are allowed. Default `auto_commit: false`; persist explicit grants per permission; unanswered means denied.
2. **§§39-40 reviewers are not read-only.** Bash remains allowed, so a reviewer can mutate code or evidence. Remove Bash from reviewer agents; the controller runs tests and supplies captured output. Enforce builder path and Git restrictions outside prompts; permissionMode enforcement is unverified.
3. **§§49-53 approvals are not bound to a commit.** Record every test result and review verdict against candidate and base commit SHAs. Later edits invalidate the approval. Missing or stale required verdicts block merge; director milestone approval does not substitute for reviewer approval.
4. **§§3, 65 recovery is unbounded and racy.** Make one controller-owned SQLite store authoritative; Markdown/JSON are projections. Before restarting a worker, confirm the previous worker and any child GPU process have stopped. Preserve dirty worktrees. Add a finite per-task recovery count; exceeding it notifies Ankur and stops.
5. **§§15-22, 67 usage telemetry is not admission control until proven.** Verify that the headless launch mode actually produces the status-line fields on the installed version. Stale or missing snapshot means no dispatch, not "conservative". Check seven-day as well as five-hour limits. Reset time triggers a recheck, not automatic resume. Serialise usage.json writes and reject older observations.
6. **§§6, 66 billing flags are policy, not enforcement.** Confirm on the actual account/version that the selected models, fallbacks and retries cannot incur paid usage before any headless run. Unknown billing behaviour blocks dispatch. Model aliases, effort levels and frontmatter fields in this document are unverified from the planning environment.
7. **§23 watchdog scope.** The watchdog manages only build processes it launched. It never stops runtime services, Kronos inference serving the C# system, or anything holding a position. Quiet GPU training is not a stall.
8. **§54 Agent 6** is the critical-reviewer role invoked per L3 packet, not a standing agent.
9. **Starting order (§72).** Acceptable to prove Steps 9-11 (CUDA, Kronos load, one forecast) before Steps 7-8 (orchestrator) when the goal is to validate the forecasting stack early. Do not grant unattended write permissions until Steps 7-8 pass with dummy tasks either way.
