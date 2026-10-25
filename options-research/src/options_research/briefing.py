"""Assemble a briefing from agent envelopes. Code only, no model.

Every section is either built from typed findings or marked BLOCKED/PARTIAL
with the reason. Nothing is filled in from memory. There is no trade-idea
section in M1.
"""
from __future__ import annotations

from options_research.envelope import Envelope, Status
from options_research.findings import (
    BriefingSection,
    ClaimStatus,
    ClaimVerification,
    EventCandidate,
    EvidenceRef,
    MarketSnapshotRef,
    RiskReview,
    StructureComparison,
    VolAssessment,
)
from options_research.intake import ResearchRequest
from options_research.policy.quarantine import Quarantine

_REPORT_REF = lambda run_id: [EvidenceRef(kind="report", id="this-report")]  # noqa: E731  (run id is in the record, not the hash)


def _fmt_pct(x: float | None) -> str:
    return "n/a" if x is None else f"{x:+.2%}"


def assemble(request: ResearchRequest, run_id: str, news: Envelope | None, market: Envelope | None,
             verify: Envelope | None, quarantine: Quarantine | None,
             market_metrics: dict[str, float | None] | None) -> list[BriefingSection]:
    refs = _REPORT_REF(run_id)
    secs: list[BriefingSection] = []

    secs.append(BriefingSection(section="instrument", status="OK", evidence_refs=refs,
                                text=f"{request.instrument.name} ({request.instrument.canonical}, {request.instrument.currency})"))
    scope = (f"as_of {request.as_of.isoformat()} ({request.as_of_source}); window "
             f"{request.window.start.isoformat()} to {request.window.end.isoformat()} ({request.window.source}); "
             f"analyses {[a.value for a in request.requested_analyses]}")
    if request.disclosures:
        scope += "; disclosures: " + "; ".join(request.disclosures)
    secs.append(BriefingSection(section="scope", status="OK", evidence_refs=refs, text=scope))

    # ---- news -------------------------------------------------------------
    events: list[EventCandidate] = []
    if news is None:
        secs.append(_blocked("material_news", "news collection not requested", refs))
    elif news.status is Status.ERROR:
        secs.append(_blocked("material_news", "; ".join(news.missing_inputs) or "news collector failed", refs))
    elif news.status is Status.ABSTAIN:
        secs.append(BriefingSection(section="material_news", status="PARTIAL", evidence_refs=refs,
                                    text="No evidence available in the window by the cutoff. " + news.rationale))
    else:
        events = [f for f in news.findings if isinstance(f, EventCandidate)]
        lines = []
        for e in sorted(events, key=lambda e: e.event_time or request.as_of):
            flag = " [INJECTION SUSPECTED, treat as hostile text]" if e.injection_suspected else ""
            q = " [QUARANTINED]" if quarantine and e.cluster_id in quarantine.quarantined_evidence_ids else ""
            lines.append(f"- {e.headline} | {e.fact_or_rumour}, {e.novelty}, independent sources: "
                         f"{e.independent_sources}, first available {e.event_time.isoformat() if e.event_time else 'n/a'}"
                         f" | source {e.cluster_id}{flag}{q}" + (f" | note: {e.note}" if e.note else ""))
        text = "\n".join(lines) + ("\n" + "; ".join(news.counterevidence) if news.counterevidence else "")
        secs.append(BriefingSection(section="material_news", status="OK", evidence_refs=news.evidence_refs or refs,
                                    text=text))

    # ---- market -----------------------------------------------------------
    if market is None:
        secs.append(_blocked("market_reaction", "market reaction not requested", refs))
    elif market.status in (Status.ERROR, Status.BLOCK):
        secs.append(_blocked("market_reaction", "; ".join(market.missing_inputs) or "market data unavailable", refs))
    else:
        snap = next((f for f in market.findings if isinstance(f, MarketSnapshotRef)), None)
        m = market_metrics or {}
        text = (f"snapshot {snap.snapshot_id if snap else 'n/a'}, quality {snap.quality if snap else 'n/a'}, "
                f"last complete bar {snap.last_complete_bar.isoformat() if snap and snap.last_complete_bar else 'n/a'}; "
                f"window return {_fmt_pct(m.get('window_return'))}; largest single-bar drop "
                f"{_fmt_pct(m.get('largest_single_bar_drop'))}; realised vol (annualised) {_fmt_pct(m.get('realised_vol'))}; "
                f"last-bar volume ratio {m.get('volume_ratio') if m.get('volume_ratio') is None else round(m['volume_ratio'], 2)}")
        if snap and snap.quality_failures:
            text += "; quality failures: " + "; ".join(snap.quality_failures)
        secs.append(BriefingSection(section="market_reaction", status="OK" if snap and snap.quality == "OK" else "PARTIAL",
                                    evidence_refs=market.evidence_refs or refs, text=text,
                                    blocked_reason=None))

    # ---- evidence by direction --------------------------------------------
    verifs: dict[str, ClaimVerification] = {}
    if verify is not None and verify.status not in (Status.ERROR,):
        verifs = {f.claim_id: f for f in verify.findings if isinstance(f, ClaimVerification)}

    def direction_section(name, want):
        if news is None or news.status in (Status.ERROR,):
            return _blocked(name, "no news evidence", refs)
        rows = []
        for e in events:
            if e.implication != want:
                continue
            v = verifs.get(e.cluster_id)
            status = v.status.value if v else "UNVERIFIED"
            rows.append(f"- {e.headline} [{status}]" + (f" {v.note}" if v and v.note else ""))
        if not rows:
            return BriefingSection(section=name, status="OK", evidence_refs=refs,
                                   text=f"No {want} evidence in the sources by the cutoff.")
        return BriefingSection(section=name, status="OK" if verifs else "PARTIAL",
                               evidence_refs=(verify.evidence_refs if verify and verify.evidence_refs else refs),
                               text="\n".join(rows),
                               blocked_reason=None if verifs else "verification unavailable")

    secs.append(direction_section("bullish_evidence", "bullish"))
    secs.append(direction_section("bearish_evidence", "bearish"))

    # ---- catalysts --------------------------------------------------------
    cats = [f"- {e.catalyst} (from {e.cluster_id})" for e in events if e.catalyst]
    secs.append(BriefingSection(section="catalysts", status="OK" if events or news is None else "PARTIAL", evidence_refs=refs,
                                text="\n".join(cats) if cats else "No scheduled catalyst named in the sources."))

    # ---- uncertainties ----------------------------------------------------
    unc = []
    for v in verifs.values():
        if v.status in (ClaimStatus.INSUFFICIENT, ClaimStatus.STALE, ClaimStatus.CONTRADICTED):
            unc.append(f"- {v.claim_text}: {v.status.value}" + (f" ({'; '.join(v.unresolved_disagreements)})" if v.unresolved_disagreements else ""))
    if news is not None and news.counterevidence:
        unc.extend(f"- {c}" for c in news.counterevidence)
    if market is not None and market.status in (Status.ERROR, Status.BLOCK):
        unc.append("- market reaction unavailable; numeric claims could not be rechecked")
    for e in events:
        if e.injection_suspected:
            unc.append(f"- {e.cluster_id} contains instruction-like text; content treated as hostile")
    if quarantine and quarantine.reasons:
        unc.extend(f"- quarantined: {r}" for r in quarantine.reasons)
    secs.append(BriefingSection(section="uncertainties", status="OK", evidence_refs=refs,
                                text="\n".join(unc) if unc else "None recorded."))

    # ---- invalidation / next ----------------------------------------------
    secs.append(BriefingSection(section="invalidation_conditions", status="PARTIAL", evidence_refs=refs,
                                text="No trade thesis is proposed in this milestone; invalidation conditions attach to a thesis.",
                                blocked_reason="no thesis in M1"))
    nxt = []
    for v in verifs.values():
        if v.status is ClaimStatus.INSUFFICIENT:
            nxt.append(f"- Corroborate independently: {v.claim_text}")
        elif v.status is ClaimStatus.STALE:
            nxt.append(f"- Re-run after cutoff to include: {v.claim_text}")
        elif v.status is ClaimStatus.CONTRADICTED:
            nxt.append(f"- Resolve contradiction: {v.claim_text}")
    for e in events:
        for mc in e.missing_corroboration:
            nxt.append(f"- {mc} ({e.cluster_id})")
    if news is not None and news.counterevidence:
        nxt.append("- Re-run with a later as_of to include records not yet available at the cutoff")
    secs.append(BriefingSection(section="next_investigations", status="OK", evidence_refs=refs,
                                text="\n".join(dict.fromkeys(nxt)) if nxt else "None."))
    return secs


ADVISORY_LABEL = ("ADVISORY, not a recommendation to trade; recheck quotes and risk before acting; "
                  "quantities not calculated (account limits not configured)")
ADVISORY_BLOCKED_REASON = "account limits not configured; quantities not calculated"


def _money(x: float | None) -> str:
    return "n/a" if x is None else f"{x:+.2f}"


def _vol(x: float | None) -> str:
    return "n/a" if x is None else f"{x * 100:.1f}%"


def vol_context_lines(vol: VolAssessment | None) -> list[str]:
    if vol is None:
        return ["volatility context: unavailable"]
    out = ["volatility context (descriptive only; annualised; no odds, no direction):"]
    if vol.forecast is None:
        out.append(f"- realised: unavailable ({vol.realised_unavailable_reason or 'no daily bars'})")
    else:
        out.append(f"- realised over {vol.bars_used} daily bars: close-to-close {_vol(vol.realised_close_to_close)}, Parkinson "
                   f"{_vol(vol.realised_parkinson)}, Yang-Zhang {_vol(vol.realised_yang_zhang)}, EWMA {_vol(vol.realised_ewma)}")
        out.append(f"- forecast {_vol(vol.forecast)} (range {_vol(vol.forecast_low)} to {_vol(vol.forecast_high)}); {vol.forecast_method}")
    if vol.atm_iv is None:
        out.append(f"- implied: unavailable ({vol.implied_unavailable_reason or 'no chain'})")
    else:
        out.append(f"- implied for {vol.expiry}: ATM IV {_vol(vol.atm_iv)} from {vol.atm_legs}; skew proxy (put minus call) "
                   f"{_vol(vol.skew_proxy)} from {vol.skew_legs}")
    out.append(f"- IV rank: {vol.iv_rank_reason if vol.iv_rank is None else f'{vol.iv_rank:.0f}'}")
    if vol.premium_points is not None:
        out.append(f"- implied minus realised forecast: {vol.premium_points:+.1f} vol points")
    return out


def advisory_section(request: ResearchRequest, pricing: Envelope, risk: Envelope,
                     vol: VolAssessment | None = None) -> BriefingSection:
    """The ONLY place trade structures appear in a briefing. Always PARTIAL in M2 (no sizing).

    Requires a risk-review envelope: the coordinator never calls this without one.
    """
    refs = list(pricing.evidence_refs) + list(risk.evidence_refs) or _REPORT_REF("")
    reviews = {f.comparison_id: f for f in risk.findings if isinstance(f, RiskReview)}
    lines = [ADVISORY_LABEL, ""]
    if pricing.status is not Status.OK:
        reason = "; ".join(pricing.missing_inputs) or pricing.rationale or "pricing unavailable"
        lines.append(f"THESIS-ONLY: {reason}. No option legs, prices or scenario P&L are given because no usable chain "
                     f"snapshot was available; a directional view, if any, is in the factual sections above.")
        lines.append("")
        lines.extend(vol_context_lines(vol))
        r = reviews.get("none")
        if r is not None:
            lines.append(f"risk review: {r.verdict.value} (expires {r.expires_at.isoformat()}): " + "; ".join(r.reasons))
        return BriefingSection(section="trade_ideas_advisory", status="PARTIAL", evidence_refs=refs,
                               text="\n".join(lines), blocked_reason=ADVISORY_BLOCKED_REASON)

    structures = [f for f in pricing.findings if isinstance(f, StructureComparison) and f.alternative == "structure"]
    others = [f for f in pricing.findings if isinstance(f, StructureComparison) and f.alternative != "structure"]
    lines.append(f"direction input: {pricing.input_hashes.get('direction_input', 'both')}; horizon end "
                 f"{structures[0].horizon_end.isoformat() if structures and structures[0].horizon_end else 'n/a'}; "
                 f"one contract set per structure; fees {structures[0].fees_assumption if structures else 'n/a'}")
    lines.append("")
    lines.extend(vol_context_lines(vol))
    for s in structures:
        rv = reviews.get(s.comparison_id)
        lines.append("")
        lines.append(f"### {s.kind} (expresses a {s.direction} thesis; expiry {s.expiry.isoformat()} "
                     f"{s.expiry_at.isoformat() if s.expiry_at else ''})")
        for l in s.legs:
            lines.append(f"- {'LONG ' if l.side > 0 else 'SHORT'} 1x {l.occ_symbol} {l.right} {l.strike:g} | bid {l.bid} ask {l.ask} "
                         f"| quote {l.quote_time.isoformat()} | OI {l.open_interest} vol {l.volume}"
                         + (f" | flags {l.liquidity_flags}" if l.liquidity_flags else ""))
        kind = "credit" if (s.net_debit_per_share or 0) < 0 else "debit"
        lines.append(f"- net {kind} {abs(s.net_debit_per_share or 0):.2f}/share = {abs(s.net_per_contract_set or 0):.2f} per set before fees {s.fees_total}")
        lines.append(f"- contractual bounds (hold to expiry, after fees): max loss {_money(-(s.max_loss or 0))}, max gain "
                     f"{'unbounded' if s.max_gain_unbounded else _money(s.max_gain)}, breakevens {s.breakevens}")
        lines.append("- scenario P&L per set (spot move: at expiry | horizon end IV-30% / flat / +30% | 100 shares):")
        for r in s.scenario_rows:
            lines.append(f"    {r.spot_pct:+5.0f}% ({r.spot:.2f}): {_money(r.pnl_expiry):>10} | {_money(r.pnl_horizon_iv_down):>10} / "
                         f"{_money(r.pnl_horizon_iv_flat):>10} / {_money(r.pnl_horizon_iv_up):>10} | {_money(r.underlying_long_100_pnl):>10}")
        if s.liquidity_flags:
            lines.append(f"- liquidity flags: {s.liquidity_flags}")
        if s.correct_direction_loss_cells:
            lines.append(f"- correct-direction scenarios that still lose: {s.correct_direction_loss_cells}")
        lines.append("- caveats: " + " | ".join(s.caveats[:4]))
        if s.note:
            lines.append(f"- note: {s.note}")
        if rv is None:
            lines.append("- risk review: MISSING (treat as not reviewed)")
        else:
            lines.append(f"- risk review: {rv.verdict.value} (expires {rv.expires_at.isoformat()}): " + "; ".join(rv.reasons)
                         + (f" | {rv.note}" if rv.note else ""))
    for o in others:
        lines.append("")
        lines.append(f"### comparison row: {o.alternative} - {o.note}")
        lines.append("    " + ", ".join(f"{r.spot_pct:+.0f}%: {_money(r.pnl_expiry)}" for r in o.scenario_rows))
    if pricing.missing_inputs:
        lines.append("")
        lines.append("not priced / notes: " + "; ".join(pricing.missing_inputs))
    return BriefingSection(section="trade_ideas_advisory", status="PARTIAL", evidence_refs=refs,
                           text="\n".join(lines), blocked_reason=ADVISORY_BLOCKED_REASON)


def _blocked(name, reason, refs) -> BriefingSection:
    return BriefingSection(section=name, status="BLOCKED", evidence_refs=refs, text="", blocked_reason=reason)


def render(sections: list[BriefingSection]) -> str:
    out = []
    for s in sections:
        head = s.section.replace("_", " ").upper()
        if s.status == "BLOCKED":
            out.append(f"## {head} [BLOCKED: {s.blocked_reason}]")
        elif s.status == "PARTIAL":
            out.append(f"## {head} [PARTIAL{': ' + s.blocked_reason if s.blocked_reason else ''}]")
        else:
            out.append(f"## {head}")
        if s.text:
            out.append(s.text)
        out.append("")
    return "\n".join(out)
