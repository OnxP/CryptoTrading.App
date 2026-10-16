"""Approved defined-risk structures, validation, exact payoff and bounds.

The approved set is a whitelist. The validator rejects anything else and,
independently, anything whose expiry payoff is unbounded in either direction.
Bounds and breakevens come from the piecewise-linear payoff, not from a model.
"""
from __future__ import annotations

from datetime import date
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

from options_research.market.snapshot import CalculationReceipt
from options_research.options.pricing import Right, intrinsic

StructureKind = Literal[
    "long_call", "long_put", "bull_call_debit", "bear_put_debit", "bull_put_credit", "bear_call_credit",
]
APPROVED: tuple[StructureKind, ...] = (
    "long_call", "long_put", "bull_call_debit", "bear_put_debit", "bull_put_credit", "bear_call_credit",
)


class StructureInvalid(ValueError):
    pass


class Leg(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    right: Right
    strike: float = Field(gt=0)
    expiry: date
    side: Literal[1, -1]          # +1 long, -1 short
    qty: Literal[1] = 1           # M2: one contract per leg, sizing not configured
    occ_symbol: str | None = None

    def payoff_per_share(self, S: float) -> float:
        return self.side * self.qty * intrinsic(S, self.strike, self.right)


class Structure(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    kind: StructureKind
    legs: list[Leg] = Field(min_length=1, max_length=2)
    contract_size: int = 100

    @model_validator(mode="after")
    def _validate(self) -> "Structure":
        validate_structure(self.kind, self.legs)
        return self

    # ---- exact payoff -----------------------------------------------------
    def payoff_per_share(self, S: float) -> float:
        return sum(l.payoff_per_share(S) for l in self.legs)

    @property
    def expiry(self) -> date:
        return self.legs[0].expiry

    @property
    def strikes(self) -> list[float]:
        return sorted({l.strike for l in self.legs})

    def width(self) -> float:
        ks = self.strikes
        return ks[-1] - ks[0] if len(ks) == 2 else 0.0


def _slope_beyond(legs: list[Leg], direction: Literal["up", "down"]) -> float:
    s = 0.0
    for l in legs:
        if direction == "up" and l.right == "call":
            s += l.side * l.qty
        if direction == "down" and l.right == "put":
            s -= l.side * l.qty  # put payoff slope is -1 per long put as S falls (gain)
    return s


def validate_structure(kind: str, legs: list[Leg]) -> None:
    if kind not in APPROVED:
        raise StructureInvalid(f"{kind} is not in the approved defined-risk set {APPROVED}")
    if len({l.expiry for l in legs}) != 1:
        raise StructureInvalid("all legs must share one expiry (calendars not approved)")
    rights = [l.right for l in legs]
    sides = [l.side for l in legs]
    ks = [l.strike for l in legs]
    pattern = {
        "long_call": (["call"], [1]), "long_put": (["put"], [1]),
        "bull_call_debit": (["call", "call"], [1, -1]), "bear_put_debit": (["put", "put"], [1, -1]),
        "bull_put_credit": (["put", "put"], [-1, 1]), "bear_call_credit": (["call", "call"], [-1, 1]),
    }[kind]
    if rights != pattern[0] or sides != pattern[1]:
        raise StructureInvalid(f"{kind} requires legs {pattern}, got rights={rights} sides={sides}")
    if kind == "bull_call_debit" and not ks[0] < ks[1]:
        raise StructureInvalid("bull call debit: long strike must be below short strike")
    if kind == "bear_put_debit" and not ks[0] > ks[1]:
        raise StructureInvalid("bear put debit: long strike must be above short strike")
    if kind == "bull_put_credit" and not ks[0] > ks[1]:
        raise StructureInvalid("bull put credit: short strike must be above long strike")
    if kind == "bear_call_credit" and not ks[0] < ks[1]:
        raise StructureInvalid("bear call credit: short strike must be below long strike")
    # independent unbounded-risk check (defence in depth)
    if _slope_beyond(legs, "up") < 0:
        raise StructureInvalid("unbounded loss as the underlying rises")
    if sum(-l.side * l.qty for l in legs if l.right == "put") > 0 and _slope_beyond(legs, "down") > 0:
        raise StructureInvalid("unbounded loss as the underlying falls")


# ---- bounds ------------------------------------------------------------------
class Bounds(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    net_debit_per_share: float            # negative for a credit
    fees_total: float
    max_loss: float                       # positive number, per contract set, includes fees
    max_gain: float | None                # per contract set, net of fees; None = unbounded (long call)
    breakevens: list[float]
    receipt: CalculationReceipt


def contractual_bounds(structure: Structure, net_debit_per_share: float, fees_total: float) -> Bounds:
    """Exact bounds from the piecewise-linear expiry payoff. Positive max_loss means money lost."""
    m = structure.contract_size
    ks = structure.strikes
    probes = [0.0] + ks + [ks[-1] * 10.0]
    pnl = [(structure.payoff_per_share(S) - net_debit_per_share) * m - fees_total for S in probes]
    # approved structures have zero slope beyond the outer strikes, so the extremes are at probes
    max_loss = -min(pnl)
    max_gain: float | None = max(pnl)
    if _slope_beyond(structure.legs, "up") > 0:
        max_gain = None  # unbounded upside (e.g. long call): do not report the probe value
    bes: list[float] = []
    for (s0, p0), (s1, p1) in zip(zip(probes, pnl), zip(probes[1:], pnl[1:])):
        if p0 == 0:
            bes.append(s0)
        elif (p0 < 0) != (p1 < 0) and p1 != 0:
            bes.append(s0 + (s1 - s0) * (-p0) / (p1 - p0))
    bes = sorted({round(b, 6) for b in bes if b > 0})
    rcpt = CalculationReceipt.make(
        "contractual_bounds",
        {"kind": structure.kind, "legs": [l.model_dump(mode="json") for l in structure.legs],
         "net_debit_per_share": net_debit_per_share, "fees_total": fees_total, "contract_size": m},
        "quote currency per contract set",
        ["payoff evaluated exactly at expiry from the legs", "fees included in both bounds",
         "assignment before expiry can realise a different path (American style)"],
        max_loss,
    )
    return Bounds(net_debit_per_share=net_debit_per_share, fees_total=fees_total, max_loss=max_loss,
                  max_gain=max_gain, breakevens=bes, receipt=rcpt)
