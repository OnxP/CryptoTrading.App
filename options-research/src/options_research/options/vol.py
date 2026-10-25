"""Volatility context: realised-vol estimators, a non-fitted forecast, and the
chain's implied side (OPT-201/202). Stdlib only; every number is a receipt.

Nothing here predicts direction or assigns a probability. The forecast is a
fixed-weight HAR-style blend whose weights are literature-typical, NOT fitted:
with a few weeks of fixture bars nothing can be fitted honestly. A fitted
GARCH/HAR or a Chronos forecast plugs in behind `VolForecaster` later as a
separately labelled comparison.
"""
from __future__ import annotations

import math
import statistics
from dataclasses import dataclass
from datetime import datetime
from typing import Protocol

from pydantic import BaseModel, ConfigDict, Field

from options_research.market.bars import Bar
from options_research.market.snapshot import CalculationReceipt
from options_research.options.chain import OptionChainSnapshot, OptionQuote
from options_research.options.pricing import AMERICAN_AS_EUROPEAN, ImpliedVolUnavailable, implied_vol, year_fraction

TRADING_DAYS = 252
EWMA_LAMBDA = 0.94
HAR_WEIGHTS = {"1d": 0.30, "5d": 0.40, "22d": 0.30}   # fixed, not fitted
HAR_NOT_FITTED = "HAR-style weights 0.30/0.40/0.30 on 1/5/22-day Yang-Zhang vol are literature-typical and NOT fitted"
UNITS = "annualised fraction"


# ---- estimators -----------------------------------------------------------------
def _log_returns(bars: list[Bar]) -> list[float]:
    return [math.log(b2.close / b1.close) for b1, b2 in zip(bars, bars[1:]) if b1.close > 0 and b2.close > 0]


def close_to_close(bars: list[Bar]) -> float | None:
    lr = _log_returns(bars)
    if len(lr) < 2:
        return None
    return statistics.stdev(lr) * math.sqrt(TRADING_DAYS)


def parkinson(bars: list[Bar]) -> float | None:
    hl = [math.log(b.high / b.low) ** 2 for b in bars if b.high > 0 and b.low > 0 and b.high >= b.low]
    if len(hl) < 2:
        return None
    return math.sqrt(sum(hl) / (4.0 * math.log(2.0) * len(hl))) * math.sqrt(TRADING_DAYS)


def yang_zhang(bars: list[Bar]) -> float | None:
    """Yang & Zhang (2000): drift-independent OHLC estimator with Rogers-Satchell term."""
    n = len(bars)
    if n < 2:
        return None
    o = [math.log(b.open / p.close) for p, b in zip(bars, bars[1:])]          # overnight
    c = [math.log(b.close / b.open) for b in bars[1:]]                        # open-to-close
    rs = [math.log(b.high / b.close) * math.log(b.high / b.open) + math.log(b.low / b.close) * math.log(b.low / b.open)
          for b in bars[1:]]
    m = n - 1
    if m < 2:
        return None
    var_o = statistics.variance(o)
    var_c = statistics.variance(c)
    var_rs = sum(rs) / m
    k = 0.34 / (1.34 + (m + 1) / (m - 1))
    var = var_o + k * var_c + (1 - k) * var_rs
    return math.sqrt(max(var, 0.0) * TRADING_DAYS)


def ewma(bars: list[Bar], lam: float = EWMA_LAMBDA) -> float | None:
    lr = _log_returns(bars)
    if len(lr) < 2:
        return None
    var = statistics.variance(lr)  # seed with the sample variance
    for r in lr:
        var = lam * var + (1 - lam) * r * r
    return math.sqrt(var * TRADING_DAYS)


# ---- summary objects --------------------------------------------------------------
class RealisedVolSummary(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    bars_used: int
    window_start: datetime | None
    window_end: datetime | None
    close_to_close: float | None
    parkinson: float | None
    yang_zhang: float | None
    ewma: float | None
    forecast: float | None                 # HAR-style fixed-weight blend
    forecast_low: float | None
    forecast_high: float | None
    receipts: dict[str, CalculationReceipt]
    unavailable_reason: str | None = None

    @property
    def receipt_ids(self) -> list[str]:
        return [r.receipt_id for r in self.receipts.values()]


class ImpliedVolSummary(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    expiry: str
    atm_iv: float | None
    atm_legs: list[str] = Field(default_factory=list)
    skew_proxy: float | None               # IV(put ~5% below) - IV(call ~5% above), vol fraction
    skew_legs: list[str] = Field(default_factory=list)
    iv_rank: float | None = None
    iv_rank_reason: str = "unavailable: no IV history store"
    receipts: dict[str, CalculationReceipt]
    notes: list[str] = Field(default_factory=list)

    @property
    def receipt_ids(self) -> list[str]:
        return [r.receipt_id for r in self.receipts.values()]


class VolForecaster(Protocol):
    """Seam for a fitted or external (e.g. Chronos) forecaster; must return a receipt-backed summary."""

    def forecast(self, bars: list[Bar]) -> RealisedVolSummary: ...


@dataclass(frozen=True)
class FixedWeightHarForecaster:
    weights: dict[str, float] = None  # type: ignore[assignment]

    def forecast(self, bars: list[Bar]) -> RealisedVolSummary:
        return realised_summary(bars, self.weights or HAR_WEIGHTS)


def realised_summary(bars: list[Bar], weights: dict[str, float] | None = None) -> RealisedVolSummary:
    weights = weights or HAR_WEIGHTS
    clean = sorted([b for b in bars if b.finite()], key=lambda b: b.open_time)
    base = {"bars": [(b.open_time.isoformat(), b.open, b.high, b.low, b.close) for b in clean], "trading_days": TRADING_DAYS}
    rc: dict[str, CalculationReceipt] = {}
    reason = None if len(clean) >= 2 else f"{len(clean)} complete daily bar(s); at least 2 needed"

    def put(name, value, assumptions):
        rc[name] = CalculationReceipt.make(name, {**base, "estimator": name}, UNITS, assumptions, value,
                                           None if value is not None else (reason or "not computable from these bars"))

    cc, pk, yz, ew = (None,) * 4
    if reason is None:
        cc, pk, yz, ew = close_to_close(clean), parkinson(clean), yang_zhang(clean), ewma(clean)
    put("close_to_close", cc, ["sample stdev of daily log close returns", f"annualised by sqrt({TRADING_DAYS})"])
    put("parkinson", pk, ["high/low range estimator (Parkinson 1980)", "assumes continuous trading, no jumps/gaps"])
    put("yang_zhang", yz, ["OHLC estimator (Yang-Zhang 2000), drift-independent", "overnight gaps included"])
    put("ewma", ew, [f"RiskMetrics EWMA lambda {EWMA_LAMBDA} on daily log returns, seeded by sample variance"])

    forecast = lo = hi = None
    f_assumptions = [HAR_NOT_FITTED]
    if reason is None:
        comps = {}
        for label, n in (("1d", 2), ("5d", 6), ("22d", 23)):
            window = clean[-n:]
            v = yang_zhang(window) if len(window) >= 2 else None
            if v is None:  # too few bars for this component: fall back to the whole window and say so
                v = yz
                f_assumptions.append(f"{label} component used all {len(clean)} bars (window shorter than {n - 1} days)")
            comps[label] = v
        if len(clean) < 23:
            f_assumptions.append(f"only {len(clean)} bars available; 22-day component is the full window")
        if all(v is not None for v in comps.values()):
            forecast = sum(weights[k] * comps[k] for k in weights)
            cands = [x for x in (cc, pk, yz, ew, forecast) if x is not None]
            lo, hi = min(cands), max(cands)
    rc["vol_forecast"] = CalculationReceipt.make("vol_forecast", {**base, "weights": weights}, UNITS, f_assumptions, forecast,
                                                 None if forecast is not None else (reason or "components unavailable"))
    rc["vol_forecast_range"] = CalculationReceipt.make("vol_forecast_range", {**base, "components": ["close_to_close", "parkinson",
                                                       "yang_zhang", "ewma", "vol_forecast"]}, UNITS,
                                                       ["range = min..max over the estimators and the forecast; not a confidence interval"],
                                                       None if hi is None else hi - lo,
                                                       None if hi is not None else (reason or "components unavailable"))
    return RealisedVolSummary(bars_used=len(clean), window_start=clean[0].open_time if clean else None,
                              window_end=clean[-1].close_time if clean else None, close_to_close=cc, parkinson=pk,
                              yang_zhang=yz, ewma=ew, forecast=forecast, forecast_low=lo, forecast_high=hi, receipts=rc,
                              unavailable_reason=reason)


# ---- implied side ------------------------------------------------------------------
def _iv(q: OptionQuote, spot: float, as_of: datetime, r: float) -> tuple[float | None, str | None]:
    if q.mid is None:
        return None, f"{q.occ_symbol}: quote unusable {q.quality_flags}"
    T = year_fraction(as_of, q.expiry_at)
    try:
        return implied_vol(q.mid, spot, q.strike, T, r, 0.0, q.right), None
    except ImpliedVolUnavailable as exc:
        return None, f"{q.occ_symbol}: {exc}"


def implied_summary(chain: OptionChainSnapshot, spot: float, as_of: datetime, r: float, otm_pct: float = 0.05) -> ImpliedVolSummary:
    usable = chain.usable_quotes()
    calls = sorted([q for q in usable if q.right == "call"], key=lambda q: q.strike)
    puts = sorted([q for q in usable if q.right == "put"], key=lambda q: q.strike)
    notes: list[str] = []
    rc: dict[str, CalculationReceipt] = {}
    base = {"chain": chain.snapshot_id, "spot": spot, "as_of": as_of.isoformat(), "r": r}

    atm_iv = None
    atm_legs: list[str] = []
    if calls and puts:
        c = min(calls, key=lambda q: (abs(q.strike - spot), q.strike))
        p = min(puts, key=lambda q: (abs(q.strike - spot), q.strike))
        ivs = []
        for q in (c, p):
            v, why = _iv(q, spot, as_of, r)
            atm_legs.append(q.occ_symbol)
            if v is None:
                notes.append(why)
            else:
                ivs.append(v)
        atm_iv = sum(ivs) / len(ivs) if ivs else None
        if ivs and len(ivs) == 1:
            notes.append("ATM IV from one leg only")
    else:
        notes.append("no usable call and put near spot")
    rc["atm_iv"] = CalculationReceipt.make("atm_iv", {**base, "legs": atm_legs}, UNITS,
                                           [AMERICAN_AS_EUROPEAN, "mean of nearest-to-spot usable call and put IV solved from mid"],
                                           atm_iv, None if atm_iv is not None else "; ".join(notes) or "unavailable")

    skew = None
    skew_legs: list[str] = []
    lo_puts = [q for q in puts if q.strike <= spot * (1 - otm_pct)]
    hi_calls = [q for q in calls if q.strike >= spot * (1 + otm_pct)]
    if lo_puts and hi_calls:
        p = max(lo_puts, key=lambda q: q.strike)
        c = min(hi_calls, key=lambda q: q.strike)
        pv, pw = _iv(p, spot, as_of, r)
        cv, cw = _iv(c, spot, as_of, r)
        skew_legs = [p.occ_symbol, c.occ_symbol]
        for w in (pw, cw):
            if w:
                notes.append(w)
        if pv is not None and cv is not None:
            skew = pv - cv
    else:
        notes.append(f"no usable strikes at or beyond +/-{otm_pct:.0%} for the skew proxy")
    rc["skew_proxy"] = CalculationReceipt.make("skew_proxy", {**base, "legs": skew_legs, "otm_pct": otm_pct}, UNITS,
                                               [AMERICAN_AS_EUROPEAN, "IV(put at/below spot*(1-otm)) minus IV(call at/above spot*(1+otm))",
                                                "a strike-distance proxy, not a delta-matched skew"],
                                               skew, None if skew is not None else "component IV unavailable")
    rc["iv_rank"] = CalculationReceipt.make("iv_rank", {**base}, "percentile", ["requires a stored IV history; none exists"], None,
                                            "unavailable: no IV history store")
    return ImpliedVolSummary(expiry=chain.expiration.isoformat(), atm_iv=atm_iv, atm_legs=atm_legs, skew_proxy=skew,
                             skew_legs=skew_legs, receipts=rc, notes=notes)


def vol_premium_points(implied: ImpliedVolSummary, realised: RealisedVolSummary) -> float | None:
    """ATM implied vol minus the realised-vol forecast, in vol points (percentage points)."""
    if implied.atm_iv is None or realised.forecast is None:
        return None
    return (implied.atm_iv - realised.forecast) * 100.0
