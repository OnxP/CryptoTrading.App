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
