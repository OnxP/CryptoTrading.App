"""Market-data specialist: deterministic, code-only, no model.

Reads fixture OHLCV, keeps only bars that COMPLETED at or before as-of,
validates them, and computes metrics as CalculationReceipts (inputs hash,
units, assumptions, result). Missing data stays missing (None), never 0.
"""
from __future__ import annotations

import hashlib
import json
import math
import statistics
from datetime import date, datetime, timedelta
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator

from options_research.intake.models import Instrument
from options_research.market.bars import INTERVAL_SECONDS as _INTERVALS
from options_research.market.bars import Bar
from options_research.market.sources import BarBatch, BarSource, FixtureBarSource, MarketDataUnavailable
from options_research.timeutil import require_utc

RECEIPT_VERSION = "market-metrics/v1"


class QualityFailure(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    kind: Literal["gap", "nonfinite", "ordering", "ohlc_inconsistent", "insufficient_bars"]
    detail: str
    at: datetime | None = None

    _utc = field_validator("at")(lambda v: require_utc(v) if v is not None else v)


class CalculationReceipt(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    receipt_id: str
    name: str
    version: str
    inputs_hash: str
    units: str
    assumptions: list[str]
    result: float | None
    unavailable_reason: str | None = None

    @staticmethod
    def make(name: str, inputs: dict, units: str, assumptions: list[str], result: float | None,
             unavailable_reason: str | None = None) -> "CalculationReceipt":
        ih = hashlib.sha256(json.dumps(inputs, sort_keys=True, default=str).encode("utf-8")).hexdigest()
        rid = hashlib.sha256(f"{name}|{RECEIPT_VERSION}|{ih}".encode("utf-8")).hexdigest()[:24]
        return CalculationReceipt(receipt_id=f"rcpt:{rid}", name=name, version=RECEIPT_VERSION,
                                  inputs_hash=ih, units=units, assumptions=assumptions,
                                  result=result, unavailable_reason=unavailable_reason)


class MarketSnapshot(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    snapshot_id: str
    instrument: str
    interval: str
    as_of: datetime
    window_start: datetime
    bars_used: int
    last_complete_bar: datetime | None
    freshness_seconds: float | None = Field(description="as_of minus last complete bar close")
    quality: Literal["OK", "DEGRADED"]
    quality_failures: list[QualityFailure]
    receipts: dict[str, CalculationReceipt]
    source: str
    delay_disclosure: str | None = None

    _utc = field_validator("as_of", "window_start", "last_complete_bar")(
        lambda v: require_utc(v) if v is not None else v
    )

    def metric(self, name: str) -> float | None:
        r = self.receipts.get(name)
        return None if r is None else r.result


class MarketDataSpecialist:
    def __init__(self, source: "BarSource | Path"):
        self._source: BarSource = FixtureBarSource(source) if isinstance(source, (str, Path)) else source

    # ---- snapshot ----------------------------------------------------------
    def snapshot(self, instrument: Instrument, as_of: datetime, interval: str = "1h",
                 window_start: datetime | None = None) -> MarketSnapshot:
        as_of = require_utc(as_of)
        if interval not in _INTERVALS:
            raise ValueError(f"unsupported interval {interval}")
        secs = _INTERVALS[interval]
        window_start = require_utc(window_start) if window_start else as_of - timedelta(days=7)
        batch: BarBatch = self._source.load(instrument, interval, window_start, as_of)
        all_bars = batch.bars

        # Completed bars only: close_time <= as_of. The bar containing as_of is NOT complete.
        complete = [b for b in all_bars if b.close_time <= as_of and b.open_time >= window_start]
        complete.sort(key=lambda b: b.open_time)

        failures: list[QualityFailure] = []
        if interval == "1d":
            have = {b.open_time.date() for b in complete}
            if complete:
                expected = batch.sessions if batch.sessions is not None else _weekdays(complete[0].open_time.date(),
                                                                                        complete[-1].open_time.date())
                for d in expected:
                    if complete[0].open_time.date() <= d <= complete[-1].open_time.date() and d not in have:
                        failures.append(QualityFailure(kind="gap", detail=f"missing session {d.isoformat()}"
                                                       + ("" if batch.sessions is not None else " (weekday heuristic; no calendar)"),
                                                       at=datetime.combine(d, datetime.min.time(), tzinfo=complete[0].open_time.tzinfo)))
            for prev, cur in zip(complete, complete[1:]):
                if cur.open_time <= prev.open_time:
                    failures.append(QualityFailure(kind="ordering", detail="non-increasing open_time", at=cur.open_time))
        else:
            for prev, cur in zip(complete, complete[1:]):
                delta = (cur.open_time - prev.open_time).total_seconds()
                if delta <= 0:
                    failures.append(QualityFailure(kind="ordering", detail="non-increasing open_time", at=cur.open_time))
                elif delta > secs:
                    missing = int(delta // secs) - 1
                    failures.append(QualityFailure(kind="gap", detail=f"{missing} missing bar(s) before", at=cur.open_time))
        nonfinite_times = []
        for b in complete:
            if not b.finite():
                failures.append(QualityFailure(kind="nonfinite", detail="NaN/inf in bar", at=b.open_time))
                nonfinite_times.append(b.open_time)
            elif not (b.low <= min(b.open, b.close) and b.high >= max(b.open, b.close)):
                failures.append(QualityFailure(kind="ohlc_inconsistent", detail="high/low do not bound open/close", at=b.open_time))
        clean = [b for b in complete if b.finite()]
        if len(clean) < 2:
            failures.append(QualityFailure(kind="insufficient_bars", detail=f"{len(clean)} finite complete bars"))

        receipts = self._receipts(instrument, interval, clean, bool(nonfinite_times))
        last = complete[-1].close_time if complete else None
        sid_src = f"{instrument.canonical}|{interval}|{as_of.isoformat()}|{window_start.isoformat()}|" + \
                  "|".join(f"{b.open_time.isoformat()}:{b.close}:{b.volume}" for b in complete)
        return MarketSnapshot(
            snapshot_id="snap:" + hashlib.sha256(sid_src.encode("utf-8")).hexdigest()[:24],
            instrument=instrument.canonical,
            interval=interval,
            as_of=as_of,
            window_start=window_start,
            bars_used=len(complete),
            last_complete_bar=last,
            freshness_seconds=(as_of - last).total_seconds() if last else None,
            quality="OK" if not failures else "DEGRADED",
            quality_failures=failures,
            receipts=receipts,
            source=batch.source_label,
            delay_disclosure=batch.delay_disclosure,
        )

    # ---- metrics (pure functions of the clean bars) ------------------------
    @staticmethod
    def _receipts(instrument: Instrument, interval: str, bars: list[Bar], had_nonfinite: bool
                  ) -> dict[str, CalculationReceipt]:
        base_inputs = {
            "instrument": instrument.canonical,
            "interval": interval,
            "bars": [(b.open_time.isoformat(), b.open, b.high, b.low, b.close, b.volume) for b in bars],
        }
        out: dict[str, CalculationReceipt] = {}
        nf_reason = "non-finite values present in window" if had_nonfinite else None

        # window_return: close[-1]/close[0] - 1
        if len(bars) >= 2 and not had_nonfinite:
            r = bars[-1].close / bars[0].close - 1.0
            out["window_return"] = CalculationReceipt.make(
                "window_return", base_inputs, "fraction",
                ["first complete bar close to last complete bar close", "no dividends/adjustments"], r)
        else:
            out["window_return"] = CalculationReceipt.make(
                "window_return", base_inputs, "fraction", [], None,
                nf_reason or "fewer than 2 bars")

        # realised_vol: stdev of log returns per bar, annualised assuming 24h trading of this interval
        if len(bars) >= 3 and not had_nonfinite:
            lr = [math.log(b2.close / b1.close) for b1, b2 in zip(bars, bars[1:]) if b1.close > 0 and b2.close > 0]
            per_year = 365 * 86400 / _INTERVALS[interval]
            vol = statistics.stdev(lr) * math.sqrt(per_year) if len(lr) >= 2 else None
            out["realised_vol"] = CalculationReceipt.make(
                "realised_vol", base_inputs, "annualised fraction",
                ["sample stdev of per-bar log close returns", f"annualised by sqrt({int(per_year)}) bars/year",
                 "gaps ignored (bars treated as consecutive)"], vol)
        else:
            out["realised_vol"] = CalculationReceipt.make(
                "realised_vol", base_inputs, "annualised fraction", [], None, nf_reason or "fewer than 3 bars")

        # volume_ratio: last bar volume / mean of prior bars
        if len(bars) >= 2 and not had_nonfinite:
            prior = [b.volume for b in bars[:-1]]
            mean_prior = sum(prior) / len(prior)
            ratio = bars[-1].volume / mean_prior if mean_prior > 0 else None
            out["volume_ratio"] = CalculationReceipt.make(
                "volume_ratio", base_inputs, "ratio", ["last complete bar volume / mean of prior bars in window"], ratio)
        else:
            out["volume_ratio"] = CalculationReceipt.make(
                "volume_ratio", base_inputs, "ratio", [], None, nf_reason or "fewer than 2 bars")

        # largest single-bar move in window (for verifier rechecks of "fell X%")
        if len(bars) >= 1 and not had_nonfinite:
            moves = [(b.close / b.open - 1.0, b.open_time.isoformat()) for b in bars if b.open > 0]
            worst = min(moves, key=lambda m: m[0]) if moves else (None, None)
            out["largest_single_bar_drop"] = CalculationReceipt.make(
                "largest_single_bar_drop", base_inputs, "fraction",
                ["min over bars of close/open - 1", f"at {worst[1]}"], worst[0])
        else:
            out["largest_single_bar_drop"] = CalculationReceipt.make(
                "largest_single_bar_drop", base_inputs, "fraction", [], None, nf_reason or "no bars")
        return out


def _weekdays(start: date, end: date) -> list[date]:
    out, d = [], start
    while d <= end:
        if d.weekday() < 5:
            out.append(d)
        d += timedelta(days=1)
    return out
