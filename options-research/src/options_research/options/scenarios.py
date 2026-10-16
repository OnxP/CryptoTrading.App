"""Scenario P&L grids for a structure, 100 shares of underlying, and no trade.

Entry: pay the ask on long legs, receive the bid on short legs. Valuation at a
future time: BSM theoretical mid per leg with the leg's entry implied vol
shifted by the scenario, less half the ENTRY spread on each leg as an exit
friction assumption. At expiry: exact intrinsic, no spread. Fees are a flat
per-contract constant (FIXTURE until the real schedule is configured).
No probabilities. Every cell is conditional on its scenario.
"""
from __future__ import annotations

import hashlib
import json
from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field

from options_research.market.snapshot import CalculationReceipt
from options_research.options.chain import OptionQuote, expiry_instant
from options_research.options.pricing import (
    AMERICAN_AS_EUROPEAN,
    ImpliedVolUnavailable,
    bsm_price,
    implied_vol,
    year_fraction,
)
from options_research.options.structures import Bounds, Structure, contractual_bounds
from options_research.timeutil import require_utc


class LegEntry(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    occ_symbol: str
    side: int
    entry_price: float            # ask for long, bid for short
    half_spread: float
    entry_iv: float | None        # None if unavailable -> grid cells None
    iv_receipt: CalculationReceipt
    quote_time: datetime


class ScenarioCell(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    spot_pct: float
    spot: float
    iv_shift: float
    at: datetime
    pnl: float | None                 # per one contract set, after fees; None if unpriceable


class ScenarioGrid(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    grid_id: str
    structure: Structure
    entries: list[LegEntry]
    bounds: Bounds
    spot0: float
    as_of: datetime
    horizon_end: datetime
    cells: list[ScenarioCell]
    underlying_long_100: list[ScenarioCell]   # same spot grid, 100 shares long, no fees
    assumptions: list[str]
    inputs_hash: str

    def cell(self, spot_pct: float, iv_shift: float, at: datetime) -> ScenarioCell | None:
        for c in self.cells:
            if c.spot_pct == spot_pct and c.iv_shift == iv_shift and c.at == at:
                return c
        return None

    def expiry_cells(self) -> list[ScenarioCell]:
        last = max(c.at for c in self.cells)
        return [c for c in self.cells if c.at == last]


def build_entries(structure: Structure, quotes: dict[str, OptionQuote], spot0: float, as_of: datetime,
                  r: float, q: float = 0.0) -> list[LegEntry]:
    out: list[LegEntry] = []
    for leg in structure.legs:
        if not leg.occ_symbol or leg.occ_symbol not in quotes:
            raise KeyError(f"no quote for leg {leg}")
        qt = quotes[leg.occ_symbol]
        if not qt.usable() or qt.bid is None or qt.ask is None:
            raise ValueError(f"quote {qt.occ_symbol} unusable: {qt.quality_flags}")
        entry = qt.ask if leg.side > 0 else qt.bid
        hs = (qt.ask - qt.bid) / 2.0
        T = year_fraction(as_of, qt.expiry_at)
        inputs = {"occ": qt.occ_symbol, "mid": qt.mid, "S": spot0, "K": qt.strike, "T": T, "r": r, "q": q, "right": qt.right}
        try:
            iv = implied_vol(qt.mid, spot0, qt.strike, T, r, q, qt.right)
            rc = CalculationReceipt.make("implied_vol_from_mid", inputs, "annualised vol (fraction)",
                                         [AMERICAN_AS_EUROPEAN, "solved by bisection on BSM against the quote mid"], iv)
        except ImpliedVolUnavailable as exc:
            iv, rc = None, CalculationReceipt.make("implied_vol_from_mid", inputs, "annualised vol (fraction)",
                                                   [AMERICAN_AS_EUROPEAN], None, str(exc))
        out.append(LegEntry(occ_symbol=qt.occ_symbol, side=leg.side, entry_price=entry, half_spread=hs,
                            entry_iv=iv, iv_receipt=rc, quote_time=qt.quote_time))
    return out


def scenario_grid(structure: Structure, quotes: dict[str, OptionQuote], *, spot0: float, as_of: datetime,
                  horizon_end: datetime, r: float, fees_per_contract: float, spot_steps_pct: list[float],
                  iv_shifts_rel: list[float], q: float = 0.0) -> ScenarioGrid:
    as_of, horizon_end = require_utc(as_of), require_utc(horizon_end)
    entries = build_entries(structure, quotes, spot0, as_of, r, q)
    m = structure.contract_size
    n_contracts = sum(l.qty for l in structure.legs)
    fees_total = fees_per_contract * n_contracts * 2  # open and close
    net_debit = sum(e.side * e.entry_price for e in entries)
    bounds = contractual_bounds(structure, net_debit, fees_total)
    expiry_at = expiry_instant(structure.expiry)
    mid_t = as_of + (min(horizon_end, expiry_at) - as_of) / 2
    times = sorted({as_of, mid_t, min(horizon_end, expiry_at), expiry_at})
    by_occ = {e.occ_symbol: e for e in entries}

    cells: list[ScenarioCell] = []
    for pct in spot_steps_pct:
        S = spot0 * (1 + pct / 100.0)
        for shift in iv_shifts_rel:
            for t in times:
                if t >= expiry_at:
                    value = structure.payoff_per_share(S)
                    ok = True
                else:
                    value, ok = 0.0, True
                    for leg in structure.legs:
                        e = by_occ[leg.occ_symbol]
                        if e.entry_iv is None:
                            ok = False
                            break
                        T = year_fraction(t, expiry_at)
                        theo = bsm_price(S, leg.strike, T, r, q, e.entry_iv * (1 + shift), leg.right)
                        exit_px = theo - e.half_spread if leg.side > 0 else theo + e.half_spread
                        value += leg.side * max(0.0, exit_px) if leg.side > 0 else leg.side * exit_px
                pnl = ((value - net_debit) * m - fees_total) if ok else None
                cells.append(ScenarioCell(spot_pct=pct, spot=S, iv_shift=shift, at=t, pnl=pnl))
    underlying = [ScenarioCell(spot_pct=pct, spot=spot0 * (1 + pct / 100.0), iv_shift=0.0, at=expiry_at,
                               pnl=(spot0 * (pct / 100.0)) * 100) for pct in spot_steps_pct]
    assumptions = [
        AMERICAN_AS_EUROPEAN,
        "entry at ask for long legs and bid for short legs; exit at theoretical mid less half the entry spread per leg",
        "at expiry: exact intrinsic, no spread, cash-equivalent settlement assumed (physical delivery in practice)",
        f"fees {fees_per_contract} per contract per side (FIXTURE schedule), {n_contracts} contract(s), open and close",
        f"risk-free rate {r} (FIXTURE placeholder), dividend yield {q}",
        "implied vols per leg solved from entry mids; scenario shifts applied multiplicatively to each leg's vol",
        "no probabilities: each cell is conditional on its spot/vol/time scenario",
        "pre-expiry cells include exit friction and can fall outside the hold-to-expiry contractual bounds",
        "underlying comparison is 100 shares long with no fees or financing",
    ]
    inputs = {"structure": structure.model_dump(mode="json"), "entries": [e.model_dump(mode="json") for e in entries],
              "spot0": spot0, "as_of": as_of.isoformat(), "horizon_end": horizon_end.isoformat(), "r": r, "q": q,
              "fees": fees_per_contract, "spot_steps": spot_steps_pct, "iv_shifts": iv_shifts_rel}
    ih = hashlib.sha256(json.dumps(inputs, sort_keys=True, default=str).encode()).hexdigest()
    return ScenarioGrid(grid_id="grid:" + ih[:24], structure=structure, entries=entries, bounds=bounds, spot0=spot0,
                        as_of=as_of, horizon_end=horizon_end, cells=cells, underlying_long_100=underlying,
                        assumptions=assumptions, inputs_hash=ih)
