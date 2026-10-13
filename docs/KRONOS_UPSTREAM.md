# Kronos upstream

| Field | Value |
|---|---|
| Repository | https://github.com/shiyu-coder/Kronos |
| Clone location | `kronos-service/third_party/Kronos` (untracked; `.gitignore`) |
| Commit | `67b630e67f6a18c9e9be918d9b4337c960db1e9a` (2026-04-13, "Merge pull request #243 … fix/batch-dimension-training") |
| Cloned | 2026-10-13 |
| Python | 3.11.9 (`kronos-service/.venv`) |
| PyTorch | see `kronos-service/requirements-torch.txt` |

Upstream `requirements.txt` at that commit: numpy, pandas==2.2.2, torch>=2.0.0, einops==0.8.1, huggingface_hub==0.33.1, matplotlib==3.9.3, tqdm==4.67.1, safetensors==0.6.2.

The checkout is not modified. Project code lives under `kronos-service/src/`. Any unavoidable upstream patch must be recorded in `docs/KRONOS_PATCHES.md` (none yet).

## Checkpoints

Filled by ENV-007.

| Repo id | Revision | model.safetensors bytes | sha256 |
|---|---|---|---|
| NeoQuasar/Kronos-Tokenizer-base | `0e0117387f39004a9016484a186a908917e22426` | 15,842,368 | `59d85f6af76a2c3b8240ea06cb21db4213b4eeca053f246b23e29cf832fc6bee` |
| NeoQuasar/Kronos-base | `2b554741eca47781b64468546e77fef3e85130e6` | 409,264,008 | `abff193acab6db1a0368e9773e75799d11403b6d054ee6d5f0a11aeabc5f4b83` |

Kronos-base parameters: 102.31 M. Loaded from the Hugging Face cache with `HF_HUB_OFFLINE=1` on 2026-10-13 (`kronos-service/scripts/download_kronos.py`); no download occurred.

Known pretraining cutoff: **UNKNOWN** — not stated in the cached config; must be sourced from the model card before any historical-replay claim (KRONOS_BUILD_CONTEXT correction 7). Note the upstream regression test pins Kronos-small revision `901c26c1…`, not Kronos-base.
