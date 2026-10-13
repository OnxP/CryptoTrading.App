"""ENV-008: one upstream-style Kronos forecast on the GPU. Environment proof only.

Mirrors third_party/Kronos/examples/prediction_example.py but uses the
upstream regression CSV (the example's data file is not in the repo),
Kronos-base (the checkpoint fixed by BUILD_SPEC) and no matplotlib window.
"""
import sys
import time
from pathlib import Path

import pandas as pd
import torch

ROOT = Path(__file__).resolve().parents[1]
KRONOS = ROOT / "third_party" / "Kronos"
sys.path.insert(0, str(KRONOS))

from model import Kronos, KronosPredictor, KronosTokenizer  # noqa: E402

tokenizer = KronosTokenizer.from_pretrained("NeoQuasar/Kronos-Tokenizer-base")
model = Kronos.from_pretrained("NeoQuasar/Kronos-base")
predictor = KronosPredictor(model, tokenizer, device="cuda:0", max_context=512)

df = pd.read_csv(KRONOS / "tests" / "data" / "regression_input.csv", parse_dates=["timestamps"])
lookback, pred_len = 400, 120
x_df = df.loc[: lookback - 1, ["open", "high", "low", "close", "volume", "amount"]]
x_ts = df.loc[: lookback - 1, "timestamps"]
y_ts = df.loc[lookback : lookback + pred_len - 1, "timestamps"]

torch.cuda.reset_peak_memory_stats()
t0 = time.perf_counter()
pred_df = predictor.predict(df=x_df, x_timestamp=x_ts, y_timestamp=y_ts, pred_len=pred_len,
                            T=1.0, top_p=0.9, sample_count=1, verbose=False)
torch.cuda.synchronize()
elapsed = time.perf_counter() - t0

print("device:", next(model.parameters()).device)
print("pred shape:", pred_df.shape)
print(pred_df.head())
print(f"wall s: {elapsed:.2f}")
print(f"peak alloc MB: {torch.cuda.max_memory_allocated() / 2**20:.0f}")
print(f"peak reserved MB: {torch.cuda.max_memory_reserved() / 2**20:.0f}")
