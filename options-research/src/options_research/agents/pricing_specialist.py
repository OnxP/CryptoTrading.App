"""Pricing specialist agent (OPT-106).

Code does everything numerical: it picks the expiry (nearest monthly at or
beyond the horizon end), selects strikes near spot from USABLE quotes only,
builds scenario grids and contractual bounds (OPT-105), and compares each
approved structure with 100 shares of underlying exposure and with no trade.
The model (mocked) writes one paragraph per structure from the supplied
numbers; a paragraph that names a leg not in the input turns the envelope
into ERROR. No chain -> ABSTAIN with thesis_only, zero legs anywhere.

Direction is an input (from the question text), restated as such. Nothing
here recommends a direction, a quantity other than one contract set, or a
probability of profit.
"""
from __future__ import annotations

import hashlib
import json
import re
from datetime import datetime, timedelta

from options_research.agents.common import ModelOutputInvalid, load_prompt, parse_json_array
from options_research.envelope import Envelope, Status
from options_research.findings import (
    CORRECT_DIRECTION_CAVEAT,
    Direction,
    EvidenceRef,
    ScenarioRow,
    StructureComparison,
    StructureLeg,
)
from options_research.intake import ResearchRequest
from options_research.market import MarketSnapshot
from options_research.model import Budget, ModelClient, ModelRequest, NoFixture
from options_research.options.chain import OptionChainSnapshot, OptionQuote, expiry_instant
from options_research.options.scenarios import ScenarioGrid, scenario_grid
from options_research.options.structures import APPROVED, Leg, Structure, StructureInvalid
from options_research.timeutil import require_utc

AGENT_ID = "pricing_specialist"
PROMPT_VERSION = "v1"
VALID_FOR_MAX = timedelta(hours=6)

DIRECTION_OF = {
    "long_call": "bullish", "bull_call_debit": "bullish", "bull_put_credit": "bullish",
    "long_put": "bearish", "bear_put_debit": "bearish", "bear_call_credit": "bearish",
}
_OCC_RE = re.compile(r"\b[A-Z]{1,6}\d{6}[CP]\d{8}\b")
_ORDER_RE = re.compile(r"\b(place|submit|execute|fill)\b[^.]{0,40}\border\b|\bbuy now\b|\bsell now\b", re.IGNORECASE)
_BULL_RE = re.compile(r"\b(bullish|upside|rally|rebound|calls?)\b", re.IGNORECASE)
_BEAR_RE = re.compile(r"\b(bearish|downside|decline|drop|puts?)\b", re.IGNORECASE)

ASSIGNMENT_CAVEATS = [
    "American-style: a short leg can be assigned before expiry, especially when in the money near a dividend",
    "physical delivery: an in-the-money leg held into expiry becomes 100 shares per contract",
    "pin risk at expiry: a leg near the strike may or may not be exercised",
]


class PricingConfig:
    """Numbers the specialist needs, from config [pricing] with explicit defaults."""

    def __init__(self, raw: dict):
        p = raw.get("pricing", {})
        t = raw.get("tradier", {})
        self.r = float(p.get("risk_free_rate", 0.04))
        self.fees_per_contract = float(p.get("fees_per_contract", 0.65))
        self.spot_steps_pct = [float(x) for x in p.get("spot_steps_pct", [-20, -15, -10, -5, 0, 5, 10, 15, 20])]
        self.iv_shifts_rel = [float(x) for x in p.get("iv_shifts_rel", [-0.30, 0.0, 0.30])]
        self.otm_pct = float(p.get("otm_pct", 0.05))               # short strike distance for spreads
        self.max_quote_age_s = float(t.get("max_quote_age_s", 900))


def direction_hint(question: str) -> Direction | None:
    """Direction is an input taken from the question text. Both or neither -> None (compare both)."""
    bull, bear = bool(_BULL_RE.search(question)), bool(_BEAR_RE.search(question))
    if bull and not bear:
        return "bullish"
    if bear and not bull:
        return "bearish"
    return None


class PricingSpecialist:
    def __init__(self, model: ModelClient, config: PricingConfig):
        self._model = model
        self._cfg = config

    # ---- public ------------------------------------------------------------
    def run(self, request: ResearchRequest, chain: OptionChainSnapshot | None, chain_reason: str | None,
            market: MarketSnapshot | None, run_id: str, budget: Budget, project_id: str = "options-research",
            direction: Direction | None = None) -> Envelope:
        as_of = require_utc(request.as_of)
        if request.horizon_days is None:
            raise ValueError("options structures require horizon_days (intake should have asked)")
        horizon_end = as_of + timedelta(days=request.horizon_days)
        pv, system = load_prompt(AGENT_ID, PROMPT_VERSION)
        base = dict(project_id=project_id, run_id=run_id, agent_id=AGENT_ID, model_id=self._model.model_id,
                    prompt_version=pv, as_of=as_of)
        input_hashes = {"prompt_version": pv, "horizon_end": horizon_end.isoformat(),
                        "direction_input": direction or "both"}
        if market is not None:
            input_hashes["market_snapshot"] = market.snapshot_id

        if chain is None:
            return Envelope(**base, valid_until=as_of + VALID_FOR_MAX, status=Status.ABSTAIN, input_hashes=input_hashes,
                            missing_inputs=[f"option chain unavailable: {chain_reason or 'no chain source'}"],
                            rationale="thesis-only: no chain, so no legs, prices or scenario tables are produced")
        input_hashes["chain_snapshot"] = chain.snapshot_id
        spot = chain.underlying_reference.result
        if spot is None or spot <= 0:
            return Envelope(**base, valid_until=as_of + VALID_FOR_MAX, status=Status.ABSTAIN, input_hashes=input_hashes,
                            missing_inputs=["underlying reference price unavailable in chain snapshot"],
                            rationale="thesis-only: no usable underlying price")
        if chain.expiration < horizon_end.date():
            return Envelope(**base, valid_until=as_of + VALID_FOR_MAX, status=Status.ABSTAIN, input_hashes=input_hashes,
                            missing_inputs=[f"chain expiration {chain.expiration.isoformat()} is before the horizon end "
                                            f"{horizon_end.date().isoformat()}"],
                            rationale="thesis-only: no listed expiration at or beyond the horizon end was supplied")

        kinds = [k for k in APPROVED if direction is None or DIRECTION_OF[k] == direction]
        comparisons: list[StructureComparison] = []
        skipped: list[str] = []
        grids: list[ScenarioGrid] = []
        for kind in kinds:
            try:
                st = self._select(kind, chain, spot)
            except _Unselectable as exc:
                skipped.append(f"{kind}: {exc}")
                continue
            quotes = {q.occ_symbol: q for q in chain.quotes}
            try:
                grid = scenario_grid(st, quotes, spot0=spot, as_of=as_of, horizon_end=horizon_end, r=self._cfg.r,
                                     fees_per_contract=self._cfg.fees_per_contract,
                                     spot_steps_pct=self._cfg.spot_steps_pct, iv_shifts_rel=self._cfg.iv_shifts_rel)
            except (ValueError, KeyError) as exc:
                skipped.append(f"{kind}: {exc}")
                continue
            grids.append(grid)
            comparisons.append(self._comparison(kind, st, grid, quotes, chain, horizon_end))

        if not comparisons:
            return Envelope(**base, valid_until=as_of + VALID_FOR_MAX, status=Status.ABSTAIN, input_hashes=input_hashes,
                            evidence_refs=[EvidenceRef(kind="snapshot", id=chain.snapshot_id)],
                            missing_inputs=["no approved structure could be priced from usable quotes"] + skipped,
                            rationale="thesis-only: chain present but no usable legs near spot")

        underlying = self._underlying_row(grids[0], chain, horizon_end)
        no_trade = self._no_trade_row(grids[0], chain, horizon_end)
        findings: list[StructureComparison] = comparisons + [underlying, no_trade]

        # earliest leg quote expiry bounds the envelope's validity
        quote_times = [l.quote_time for c in comparisons for l in c.legs]
        valid_until = min(min(quote_times) + timedelta(seconds=self._cfg.max_quote_age_s), as_of + VALID_FOR_MAX)
        valid_until = max(valid_until, as_of)
        refs = [EvidenceRef(kind="snapshot", id=chain.snapshot_id)]
        refs += [EvidenceRef(kind="receipt", id=rid) for c in comparisons for rid in c.receipt_ids]
        refs = sorted({r for r in refs}, key=lambda r: (r.kind, r.id))
        input_hashes["structures"] = hashlib.sha256(
            json.dumps([c.model_dump(mode="json", include={"kind", "legs"}) for c in comparisons], sort_keys=True).encode()
        ).hexdigest()

        missing = list(skipped)
        try:
            narratives = self._narratives(comparisons, pv, system, budget)
        except NoFixture as exc:
            narratives = {}
            missing.append(f"narrative unavailable: {exc}")
        except ModelOutputInvalid as exc:
            return Envelope(**base, valid_until=valid_until, status=Status.ERROR, input_hashes=input_hashes,
                            evidence_refs=refs, missing_inputs=[f"model output rejected: {exc}"],
                            rationale="model narrative referenced something not in the input; nothing accepted")
        findings = [f.model_copy(update={"note": narratives.get(f.comparison_id, f.note)}) for f in findings]
        return Envelope(**base, valid_until=valid_until, status=Status.OK, input_hashes=input_hashes,
                        evidence_refs=refs, findings=findings, missing_inputs=missing,
                        rationale=f"{len(comparisons)} structure(s) priced from chain {chain.snapshot_id} at spot {spot:.2f}; "
                                  f"direction input: {direction or 'both (none stated)'}; one contract set, no sizing")

    # ---- strike selection (code only) --------------------------------------
    def _select(self, kind: str, chain: OptionChainSnapshot, spot: float) -> Structure:
        usable = chain.usable_quotes()
        calls = sorted([q for q in usable if q.right == "call"], key=lambda q: q.strike)
        puts = sorted([q for q in usable if q.right == "put"], key=lambda q: q.strike)
        up, down = spot * (1 + self._cfg.otm_pct), spot * (1 - self._cfg.otm_pct)

        def atm(qs):
            if not qs:
                raise _Unselectable("no usable quotes of that right")
            return min(qs, key=lambda q: (abs(q.strike - spot), q.strike))

        def first_at_or_above(qs, level, strictly_above: float | None = None):
            c = [q for q in qs if q.strike >= level and (strictly_above is None or q.strike > strictly_above)]
            if not c:
                raise _Unselectable(f"no usable strike at or above {level:.2f}")
            return min(c, key=lambda q: q.strike)

        def first_at_or_below(qs, level, strictly_below: float | None = None):
            c = [q for q in qs if q.strike <= level and (strictly_below is None or q.strike < strictly_below)]
            if not c:
                raise _Unselectable(f"no usable strike at or below {level:.2f}")
            return max(c, key=lambda q: q.strike)

        def next_above(qs, k):
            c = [q for q in qs if q.strike > k]
            if not c:
                raise _Unselectable(f"no usable strike above {k:.2f}")
            return min(c, key=lambda q: q.strike)

        def next_below(qs, k):
            c = [q for q in qs if q.strike < k]
            if not c:
                raise _Unselectable(f"no usable strike below {k:.2f}")
            return max(c, key=lambda q: q.strike)

        if kind == "long_call":
            legs = [(atm(calls), 1)]
        elif kind == "long_put":
            legs = [(atm(puts), 1)]
        elif kind == "bull_call_debit":
            lo = atm(calls)
            legs = [(lo, 1), (first_at_or_above(calls, up, strictly_above=lo.strike), -1)]
        elif kind == "bear_put_debit":
            hi = atm(puts)
            legs = [(hi, 1), (first_at_or_below(puts, down, strictly_below=hi.strike), -1)]
        elif kind == "bull_put_credit":
            short = first_at_or_below(puts, down)
            legs = [(short, -1), (next_below(puts, short.strike), 1)]
        elif kind == "bear_call_credit":
            short = first_at_or_above(calls, up)
            legs = [(short, -1), (next_above(calls, short.strike), 1)]
        else:  # pragma: no cover - APPROVED is the whitelist
            raise _Unselectable("not approved")
        try:
            return Structure(kind=kind, legs=[Leg(right=q.right, strike=q.strike, expiry=q.expiry, side=side,
                                                 occ_symbol=q.occ_symbol) for q, side in legs])
        except (StructureInvalid, ValueError) as exc:
            raise _Unselectable(str(exc)) from exc

    # ---- findings ----------------------------------------------------------
    def _comparison(self, kind: str, st: Structure, grid: ScenarioGrid, quotes: dict[str, OptionQuote],
                    chain: OptionChainSnapshot, horizon_end: datetime) -> StructureComparison:
        direction = DIRECTION_OF[kind]
        legs = []
        for leg in st.legs:
            q = quotes[leg.occ_symbol]
            legs.append(StructureLeg(occ_symbol=q.occ_symbol, right=q.right, strike=q.strike, expiry=q.expiry,
                                     side=leg.side, bid=q.bid, ask=q.ask, quote_time=q.quote_time, bid_size=q.bid_size,
                                     ask_size=q.ask_size, open_interest=q.open_interest, volume=q.volume,
                                     liquidity_flags=list(q.quality_flags)))
        rows = self._rows(grid, horizon_end)
        liquidity = sorted({f"{l.occ_symbol}:{fl}" for l in legs for fl in l.liquidity_flags})
        for l in legs:
            if (l.open_interest or 0) < 100:
                liquidity.append(f"{l.occ_symbol}:low_open_interest")
        # correct-direction cells with negative P&L at the horizon end (pre-expiry): the spec caveat made concrete
        loss_cells: list[str] = []
        for r in rows:
            right_way = r.spot_pct > 0 if direction == "bullish" else r.spot_pct < 0
            if not right_way:
                continue
            for label, v in (("iv-30%", r.pnl_horizon_iv_down), ("iv flat", r.pnl_horizon_iv_flat), ("expiry", r.pnl_expiry)):
                if v is not None and v < 0:
                    loss_cells.append(f"spot {r.spot_pct:+.0f}% {label}: {v:+.2f}")
        caveats = [CORRECT_DIRECTION_CAVEAT] + ASSIGNMENT_CAVEATS + list(grid.assumptions)
        if loss_cells:
            caveats.append("correct-direction scenarios with negative P&L exist in the grid: " + "; ".join(loss_cells))
        receipts = [grid.bounds.receipt.receipt_id, chain.underlying_reference.receipt_id] + \
                   [e.iv_receipt.receipt_id for e in grid.entries] + [grid.grid_id]
        b = grid.bounds
        net_set = b.net_debit_per_share * st.contract_size
        cid = "cmp:" + hashlib.sha256(f"{kind}|{grid.grid_id}".encode()).hexdigest()[:16]
        return StructureComparison(
            comparison_id=cid, alternative="structure", kind=kind, direction=direction, expiry=st.expiry,
            expiry_at=expiry_instant(st.expiry), legs=legs, net_debit_per_share=b.net_debit_per_share,
            net_per_contract_set=net_set, fees_total=b.fees_total,
            fees_assumption=f"{self._cfg.fees_per_contract} per contract per side (FIXTURE schedule)",
            max_loss=b.max_loss, max_gain=b.max_gain, max_gain_unbounded=b.max_gain is None, breakevens=b.breakevens,
            scenario_grid_id=grid.grid_id, scenario_rows=rows, horizon_end=horizon_end, liquidity_flags=liquidity,
            correct_direction_loss_cells=loss_cells, caveats=caveats, receipt_ids=receipts,
            evidence_refs=[EvidenceRef(kind="snapshot", id=chain.snapshot_id)] +
                          [EvidenceRef(kind="receipt", id=r) for r in receipts],
            note=f"direction '{direction}' is the thesis this structure expresses, stated as an input, not a forecast",
        )

    def _rows(self, grid: ScenarioGrid, horizon_end: datetime) -> list[ScenarioRow]:
        expiry_at = max(c.at for c in grid.cells)
        h_at = min(horizon_end, expiry_at)
        shifts = sorted({c.iv_shift for c in grid.cells})
        lo, flat, hi = shifts[0], (0.0 if 0.0 in shifts else shifts[len(shifts) // 2]), shifts[-1]
        under = {c.spot_pct: c.pnl for c in grid.underlying_long_100}
        rows = []
        for pct in sorted({c.spot_pct for c in grid.cells}):
            def cell(shift, at):
                c = grid.cell(pct, shift, at)
                return None if c is None else c.pnl
            rows.append(ScenarioRow(spot_pct=pct, spot=grid.spot0 * (1 + pct / 100.0), pnl_expiry=cell(flat, expiry_at),
                                    pnl_horizon_iv_down=cell(lo, h_at), pnl_horizon_iv_flat=cell(flat, h_at),
                                    pnl_horizon_iv_up=cell(hi, h_at), underlying_long_100_pnl=under[pct]))
        return rows

    def _underlying_row(self, grid: ScenarioGrid, chain: OptionChainSnapshot, horizon_end: datetime) -> StructureComparison:
        rows = [ScenarioRow(spot_pct=c.spot_pct, spot=c.spot, pnl_expiry=c.pnl, pnl_horizon_iv_down=c.pnl,
                            pnl_horizon_iv_flat=c.pnl, pnl_horizon_iv_up=c.pnl, underlying_long_100_pnl=c.pnl)
                for c in grid.underlying_long_100]
        return StructureComparison(
            comparison_id="cmp:underlying_long_100", alternative="underlying_long_100", direction="bullish",
            scenario_rows=rows, horizon_end=horizon_end, max_loss=grid.spot0 * 100, max_gain=None, max_gain_unbounded=True,
            fees_assumption="no fees or financing assumed for the share comparison",
            caveats=["100 shares long, linear exposure, no expiry; loss bounded only by the shares going to zero"],
            receipt_ids=[chain.underlying_reference.receipt_id],
            evidence_refs=[EvidenceRef(kind="snapshot", id=chain.snapshot_id),
                           EvidenceRef(kind="receipt", id=chain.underlying_reference.receipt_id)],
            note="comparison row: plain underlying exposure over the same spot grid",
        )

    def _no_trade_row(self, grid: ScenarioGrid, chain: OptionChainSnapshot, horizon_end: datetime) -> StructureComparison:
        rows = [ScenarioRow(spot_pct=c.spot_pct, spot=c.spot, pnl_expiry=0.0, pnl_horizon_iv_down=0.0,
                            pnl_horizon_iv_flat=0.0, pnl_horizon_iv_up=0.0, underlying_long_100_pnl=c.pnl)
                for c in grid.underlying_long_100]
        return StructureComparison(
            comparison_id="cmp:no_trade", alternative="no_trade", scenario_rows=rows, horizon_end=horizon_end,
            max_loss=0.0, max_gain=0.0, fees_assumption="none", caveats=["no exposure, no cost, no payoff"],
            receipt_ids=[chain.underlying_reference.receipt_id],
            evidence_refs=[EvidenceRef(kind="snapshot", id=chain.snapshot_id)],
            note="comparison row: no trade",
        )

    # ---- model narrative (optional, validated) ----------------------------
    def _narratives(self, comparisons: list[StructureComparison], pv: str, system: str, budget: Budget) -> dict[str, str]:
        payload = []
        for c in comparisons:
            payload.append({
                "comparison_id": c.comparison_id, "kind": c.kind, "direction_input": c.direction,
                "legs": [{"occ_symbol": l.occ_symbol, "side": "long" if l.side > 0 else "short", "bid": l.bid, "ask": l.ask,
                          "quote_time": l.quote_time.isoformat(), "flags": l.liquidity_flags} for l in c.legs],
                "net_debit_per_share": c.net_debit_per_share, "max_loss": c.max_loss,
                "max_gain": "unbounded" if c.max_gain_unbounded else c.max_gain, "breakevens": c.breakevens,
                "scenario_rows": [r.model_dump(mode="json") for r in c.scenario_rows],
                "correct_direction_loss_cells": c.correct_direction_loss_cells,
            })
        req = ModelRequest(role=AGENT_ID, prompt_version=pv, system=system,
                           messages=({"role": "user", "content": "Structures:\n" + json.dumps(payload, indent=1)},))
        resp = self._model.complete(req, budget)
        allowed_occ = {l.occ_symbol for c in comparisons for l in c.legs}
        by_kind = {c.kind: c.comparison_id for c in comparisons}
        out: dict[str, str] = {}
        for item in parse_json_array(resp.text):
            if set(item) != {"kind", "narrative"} or item["kind"] not in by_kind:
                raise ModelOutputInvalid(f"bad narrative item (keys {sorted(item)}, kind {item.get('kind')!r})")
            text = str(item["narrative"])
            unknown = set(_OCC_RE.findall(text)) - allowed_occ
            if unknown:
                raise ModelOutputInvalid(f"narrative names leg(s) not in the input: {sorted(unknown)}")
            if _ORDER_RE.search(text):
                raise ModelOutputInvalid("narrative reads as an order instruction")
            out[by_kind[item["kind"]]] = text
        return out


class _Unselectable(ValueError):
    pass
