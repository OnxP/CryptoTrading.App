"""Option chain snapshot with code-side quote quality flags.

Nothing here fills a missing bid or ask. Vendor greeks and vendor IV are kept
under `vendor_*` and never used by our pricing (OPT-105 recomputes).
"""
from __future__ import annotations

import hashlib
import math
from datetime import date, datetime, time, timedelta, timezone
from typing import Literal
from zoneinfo import ZoneInfo

from pydantic import BaseModel, ConfigDict, Field, field_validator

from options_research.connectors.tradier import TradierAuthUnavailable, TradierClient, TradierError, epoch_ms_to_utc
from options_research.intake.models import Instrument
from options_research.market.snapshot import CalculationReceipt
from options_research.timeutil import require_utc

NY = ZoneInfo("America/New_York")
Right = Literal["call", "put"]
QualityFlag = Literal["zero_bid", "crossed", "wide", "stale", "nonfinite", "no_quote_time"]


class ChainUnavailable(RuntimeError):
    pass


def expiry_instant(d: date) -> datetime:
    """US listed equity options stop trading 16:00 America/New_York on the expiration date."""
    return datetime.combine(d, time(16, 0), tzinfo=NY).astimezone(timezone.utc)


class OptionQuote(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    occ_symbol: str
    underlying: str
    expiry: date
    expiry_at: datetime
    strike: float
    right: Right
    bid: float | None
    ask: float | None
    last: float | None
    bid_time: datetime | None
    ask_time: datetime | None
    last_time: datetime | None
    bid_size: int | None = None
    ask_size: int | None = None
    volume: int | None = None
    open_interest: int | None = None
    contract_size: int = 100
    exercise_style: Literal["american", "european"] = "american"
    settlement: Literal["physical", "cash"] = "physical"
    vendor_iv: float | None = None
    vendor_delta: float | None = None
    vendor_gamma: float | None = None
    vendor_theta: float | None = None
    vendor_vega: float | None = None
    vendor_greeks_updated_at: str | None = None
    quality_flags: list[QualityFlag] = Field(default_factory=list)

    _utc = field_validator("expiry_at", "bid_time", "ask_time", "last_time")(
        lambda v: require_utc(v) if v is not None else v
    )

    @property
    def mid(self) -> float | None:
        if self.bid is None or self.ask is None or not self.usable():
            return None
        return (self.bid + self.ask) / 2.0

    @property
    def quote_time(self) -> datetime | None:
        ts = [t for t in (self.bid_time, self.ask_time) if t is not None]
        return min(ts) if ts else None

    def usable(self) -> bool:
        return not any(f in self.quality_flags for f in ("zero_bid", "crossed", "stale", "nonfinite", "no_quote_time"))


class OptionChainSnapshot(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    snapshot_id: str
    underlying: str
    as_of: datetime
    received_at: datetime
    expirations_available: list[date]
    expiration: date
    quotes: list[OptionQuote]
    underlying_reference: CalculationReceipt
    source: str
    delay_disclosure: str | None = None

    _utc = field_validator("as_of", "received_at")(require_utc)

    def usable_quotes(self) -> list[OptionQuote]:
        return [q for q in self.quotes if q.usable()]

    def by_symbol(self, occ: str) -> OptionQuote:
        for q in self.quotes:
            if q.occ_symbol == occ:
                return q
        raise KeyError(occ)

    @property
    def quality_summary(self) -> dict[str, int]:
        out: dict[str, int] = {}
        for q in self.quotes:
            for f in q.quality_flags:
                out[f] = out.get(f, 0) + 1
        return out


def _flags(bid, ask, bid_time, ask_time, as_of: datetime, max_age_s: float, wide_ratio: float) -> list[QualityFlag]:
    flags: list[QualityFlag] = []
    vals = [v for v in (bid, ask) if v is not None]
    if bid is None or ask is None or any(not math.isfinite(v) for v in vals):
        flags.append("nonfinite")
        return flags
    if bid <= 0:
        flags.append("zero_bid")
    if bid > ask:
        flags.append("crossed")
    mid = (bid + ask) / 2.0
    if mid > 0 and (ask - bid) / mid > wide_ratio:
        flags.append("wide")
    times = [t for t in (bid_time, ask_time) if t is not None]
    if not times:
        flags.append("no_quote_time")
    elif (as_of - min(times)).total_seconds() > max_age_s:
        flags.append("stale")
    return flags


def build_chain_snapshot(rows: list[dict], *, underlying: str, expiration: date, expirations_available: list[date],
                         as_of: datetime, received_at: datetime, underlying_price: float, underlying_price_time: datetime,
                         max_age_s: float, wide_ratio: float, source: str,
                         delay_disclosure: str | None = None) -> OptionChainSnapshot:
    as_of = require_utc(as_of)
    quotes: list[OptionQuote] = []
    for r in rows:
        exp = date.fromisoformat(str(r["expiration_date"]))
        if exp != expiration:
            continue
        g = r.get("greeks") or {}
        bid = _f(r.get("bid")); ask = _f(r.get("ask"))
        bt = epoch_ms_to_utc(r.get("bid_date")); at = epoch_ms_to_utc(r.get("ask_date"))
        quotes.append(OptionQuote(
            occ_symbol=str(r["symbol"]), underlying=str(r.get("underlying") or underlying).upper(),
            expiry=exp, expiry_at=expiry_instant(exp), strike=float(r["strike"]),
            right="call" if str(r.get("option_type", "")).lower().startswith("c") else "put",
            bid=bid, ask=ask, last=_f(r.get("last")), bid_time=bt, ask_time=at,
            last_time=epoch_ms_to_utc(r.get("trade_date")),
            bid_size=_i(r.get("bidsize")), ask_size=_i(r.get("asksize")), volume=_i(r.get("volume")),
            open_interest=_i(r.get("open_interest")), contract_size=int(r.get("contract_size") or 100),
            vendor_iv=_f(g.get("mid_iv")), vendor_delta=_f(g.get("delta")), vendor_gamma=_f(g.get("gamma")),
            vendor_theta=_f(g.get("theta")), vendor_vega=_f(g.get("vega")),
            vendor_greeks_updated_at=g.get("updated_at"),
            quality_flags=_flags(bid, ask, bt, at, as_of, max_age_s, wide_ratio),
        ))
    quotes.sort(key=lambda q: (q.right, q.strike))
    ref = CalculationReceipt.make("underlying_reference_price",
                                  {"underlying": underlying, "price": underlying_price,
                                   "price_time": underlying_price_time.isoformat()},
                                  "price (quote currency)",
                                  ["vendor last/mid at the quote time shown; delayed if sandbox"], underlying_price)
    sid_src = f"{underlying}|{expiration.isoformat()}|{as_of.isoformat()}|" + "|".join(
        f"{q.occ_symbol}:{q.bid}:{q.ask}:{q.bid_time}:{q.ask_time}" for q in quotes)
    return OptionChainSnapshot(
        snapshot_id="chain:" + hashlib.sha256(sid_src.encode("utf-8")).hexdigest()[:24],
        underlying=underlying.upper(), as_of=as_of, received_at=received_at,
        expirations_available=sorted(expirations_available), expiration=expiration, quotes=quotes,
        underlying_reference=ref, source=source, delay_disclosure=delay_disclosure,
    )


def _f(v):
    if v is None or v == "":
        return None
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def _i(v):
    f = _f(v)
    return None if f is None else int(f)


class TradierChainSource:
    def __init__(self, client: TradierClient, max_age_s: float, wide_ratio: float):
        self._c = client
        self._max_age = max_age_s
        self._wide = wide_ratio

    def expirations(self, instrument: Instrument) -> list[date]:
        try:
            return sorted(date.fromisoformat(d) for d in self._c.expirations(instrument.symbol))
        except (TradierAuthUnavailable, TradierError) as exc:
            raise ChainUnavailable(str(exc)) from exc

    def snapshot(self, instrument: Instrument, expiration: date, as_of: datetime) -> OptionChainSnapshot:
        exps = self.expirations(instrument)
        if expiration not in exps:
            raise ChainUnavailable(f"{expiration.isoformat()} is not a listed expiration for {instrument.symbol}")
        try:
            rows, received_at = self._c.chain(instrument.symbol, expiration.isoformat())
            uq = self._c.quotes([instrument.symbol])
        except (TradierAuthUnavailable, TradierError) as exc:
            raise ChainUnavailable(str(exc)) from exc
        if not rows:
            raise ChainUnavailable(f"empty chain for {instrument.symbol} {expiration.isoformat()}")
        if not uq:
            raise ChainUnavailable(f"no underlying quote for {instrument.symbol}")
        u = uq[0]
        ubid, uask, ulast = _f(u.get("bid")), _f(u.get("ask")), _f(u.get("last"))
        uprice = (ubid + uask) / 2 if ubid and uask and ubid > 0 and uask >= ubid else ulast
        if uprice is None:
            raise ChainUnavailable("underlying price unavailable")
        utime = epoch_ms_to_utc(u.get("trade_date")) or epoch_ms_to_utc(u.get("bid_date")) or received_at
        return build_chain_snapshot(
            rows, underlying=instrument.symbol, expiration=expiration, expirations_available=exps, as_of=as_of,
            received_at=received_at, underlying_price=float(uprice), underlying_price_time=utime,
            max_age_s=self._max_age, wide_ratio=self._wide,
            source=f"TRADIER{'-SANDBOX' if self._c.is_sandbox else ''}:chains",
            delay_disclosure="Tradier sandbox quotes are delayed" if self._c.is_sandbox else None,
        )


def nearest_expiration_at_or_after(exps: list[date], target: date, prefer_monthly: bool = True) -> date | None:
    """Monthly expirations are the third Friday. Prefer the first monthly on/after target, else any."""
    after = [e for e in sorted(exps) if e >= target]
    if not after:
        return None
    if prefer_monthly:
        for e in after:
            if e.weekday() == 4 and 15 <= e.day <= 21:
                return e
    return after[0]
