# kronos-service

Python Kronos forecasting service (see `docs/KRONOS_BUILD_CONTEXT.md`).

Environment (Python 3.11, created with `py -3.11 -m venv .venv`):

```powershell
.\kronos-service\.venv\Scripts\Activate.ps1
python kronos-service\scripts\check_cuda.py
```

Do not use the `python` on PATH (3.12). Upstream Kronos is cloned (untracked) at `kronos-service/third_party/Kronos`; see `docs/KRONOS_UPSTREAM.md`.
