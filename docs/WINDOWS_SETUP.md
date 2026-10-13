# Windows setup — Kronos service

Observed 2026-10-13 on the build machine (ANKUR-PC). Re-run the commands in each row to re-verify.

## Toolchain (ENV-001)

| Tool | Command | Observed |
|---|---|---|
| Git | `git --version` | 2.51.0.windows.1 |
| Python 3.11 | `py -3.11 --version` | 3.11.9 (C:\Users\ankur\AppData\Local\Programs\Python\Python311) |
| Python on PATH | `python --version` | 3.12.7 — **do not use**; 3.13 is also installed and is the `py` default |
| .NET SDK | `dotnet --version` | 10.0.204 (also 10.0.100-preview.7) |
| GPU | `nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv` | NVIDIA GeForce RTX 5080, 610.88, 16303 MiB |
| ODBC | `C:\Windows\System32\msodbcsql17.dll`; `sqlcmd` (ODBC 170 tools) | present |
| SQL Server | service `MSSQL$APDATASERVICE`; `sqlcmd -S ANKUR-PC\APDATASERVICE -E -d CryptoDb` | reachable with Windows auth |
| Claude Code | `claude --version` | 2.1.267 |
| Docker | — | not installed (docker-compose services unavailable on host) |

## Python environment (ENV-004/005/006)

```powershell
py -3.11 -m venv kronos-service\.venv
kronos-service\.venv\Scripts\python.exe -m pip install --upgrade pip setuptools wheel
kronos-service\.venv\Scripts\python.exe -m pip install -r kronos-service\requirements-torch.txt
kronos-service\.venv\Scripts\python.exe -m pip install -r kronos-service\third_party\Kronos\requirements.txt
kronos-service\.venv\Scripts\python.exe kronos-service\scripts\check_cuda.py
```

Expected `check_cuda.py` output: `2.14.0+cu130` / `CUDA: True` / `Device: NVIDIA GeForce RTX 5080` / `VRAM GB: 15.92`.

Frozen environment: `kronos-service/requirements.lock.txt`. Upstream clone and checkpoint identities: `docs/KRONOS_UPSTREAM.md`.

## Claude Code account (ENV-002)

Pending human step: run `/model` and `/status` in an interactive `claude` terminal and record here the model aliases, default model, effort levels and whether Fable shows "Requires usage credits". Until recorded, `allow_headless_fable: false` and `allow_usage_credits: false` stay in force (ORCHESTRATION §§6, 66, 77.6).

## Usage capture (ENV-003)

`scripts/claude_status.py` is wired as the Claude Code status line (`.claude/settings.json` → `statusLine`) and writes `.workflow/runtime/usage.json` (git-ignored). Whether `rate_limits.*` fields are populated on this account: **not yet observed** — check `.workflow/runtime/usage.json` after one interactive request and record the result here.

## Gate 0 result (ENV-008, 2026-10-13)

`kronos-service\.venv\Scripts\python.exe kronos-service\scripts\run_upstream_example.py` (log: `.workflow/runtime/env-008.log`)

```
device: cuda:0
pred shape: (120, 6)
wall s: 4.65
peak alloc MB: 473
peak reserved MB: 498
```

Inputs: upstream `tests/data/regression_input.csv` (5-minute equity bars), lookback 400, pred_len 120, T=1.0, top_p=0.9, sample_count=1, Kronos-base + Kronos-Tokenizer-base. The upstream `examples/prediction_example.py` was not run as-is: its CSV is absent from the repository, it loads Kronos-small, and it blocks on `plt.show()`. The wrapper changes only those three things; upstream code is untouched.

| ORCHESTRATION §14 item | Result |
|---|---|
| Windows Python environment created | PASS |
| Git working | PASS |
| NVIDIA driver working | PASS |
| RTX 5080 visible to nvidia-smi | PASS |
| PyTorch sees CUDA | PASS |
| Kronos repository downloaded | PASS (67b630e) |
| Kronos dependencies installed | PASS (torch pin preserved) |
| Kronos tokenizer downloads | PASS (from cache, offline) |
| Kronos-base downloads | PASS (from cache, offline) |
| one official forecast succeeds | PASS (wrapper, see note) |
| environment commands documented | PASS (this file) |
| Claude Code works | PASS |
| rate-limit usage snapshot works or absence documented | PENDING (needs one interactive session) |
| Fable billing behaviour known | PENDING (ENV-002 human step) |
