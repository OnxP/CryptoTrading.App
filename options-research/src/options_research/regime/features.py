"""Daily regime features from complete bars: log return, relative volume, log range.

Relative volume is log(volume / median of the previous N volumes), which is
what made the original R&D model work (bear regimes are heavy-volume). Each
daily feature is averaged over a short trailing `window` so a state describes
a stretch of days rather than one day, then z-scored over the sample and
winsorised so one halt or squeeze day cannot own a state.
"""
from __future__ import annotations

import hashlib
import json
import math
import statistics
from dataclasses import dataclass
from datetime import datetime

from options_research.market.bars import Bar

FEATURE_NAMES = ("log_return", "rel_volume", "log_range")


@dataclass(frozen=True)
class FeatureMatrix:
    times: list[datetime]            # close time of the bar each row describes
    raw: list[list[float]]           # unscaled features
    rows: list[list[float]]          # z-scored, winsorised; what the HMM sees
    means: list[float]
    stds: list[float]
    winsor: float
    window: int
    bars_in: int
    rows_skipped: int                # bars dropped for lacking a prior close / volume baseline / finite values
    inputs_hash: str

    @property
    def n(self) -> int:
        return len(self.rows)


def build_features(bars: list[Bar], volume_median_days: int = 20, winsor: float = 5.0,
                   window: int = 5) -> FeatureMatrix:
    if volume_median_days < 2:
        raise ValueError("volume_median_days must be >= 2")
    if window < 1:
        raise ValueError("window must be >= 1")
    times: list[datetime] = []
    daily: list[list[float]] = []
    skipped = 0
    for i in range(1, len(bars)):
        b, prev = bars[i], bars[i - 1]
        if not (b.finite() and prev.finite()) or prev.close <= 0 or b.close <= 0:
            skipped += 1
            continue
        ret = math.log(b.close / prev.close)
        base_vols = [max(x.volume, 1.0) for x in bars[max(0, i - volume_median_days):i]]
        if len(base_vols) < min(5, volume_median_days):
            skipped += 1
            continue
        rel_vol = math.log(max(b.volume, 1.0) / statistics.median(base_vols))
        if b.high > 0 and b.low > 0 and b.high >= b.low:
            rng = math.log(b.high / b.low)
        else:
            rng = abs(ret)
        if not all(math.isfinite(v) for v in (ret, rel_vol, rng)):
            skipped += 1
            continue
        times.append(b.close_time)
        daily.append([ret, rel_vol, rng])
    # trailing-window means: row i describes bars i-window+1..i
    raw: list[list[float]] = []
    for i in range(window - 1, len(daily)):
        chunk = daily[i - window + 1:i + 1]
        raw.append([sum(c[j] for c in chunk) / window for j in range(3)])
    times = times[window - 1:] if len(times) >= window - 1 else []
    skipped += min(len(daily), window - 1)
    if len(raw) < 2:
        return FeatureMatrix(times=times, raw=raw, rows=[], means=[0.0] * 3, stds=[1.0] * 3, winsor=winsor,
                             window=window, bars_in=len(bars), rows_skipped=skipped, inputs_hash=_hash(bars))
    means = [statistics.fmean(c) for c in zip(*raw)]
    stds = [max(statistics.pstdev(c), 1e-9) for c in zip(*raw)]
    rows = [[max(-winsor, min(winsor, (v - m) / s)) for v, m, s in zip(r, means, stds)] for r in raw]
    return FeatureMatrix(times=times, raw=raw, rows=rows, means=means, stds=stds, winsor=winsor, window=window,
                         bars_in=len(bars), rows_skipped=skipped, inputs_hash=_hash(bars))


def _hash(bars: list[Bar]) -> str:
    payload = [[b.close_time.isoformat(), b.open, b.high, b.low, b.close, b.volume] for b in bars]
    return hashlib.sha256(json.dumps(payload, separators=(",", ":")).encode("utf-8")).hexdigest()[:16]
