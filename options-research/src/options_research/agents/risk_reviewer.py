"""Risk reviewer agent (OPT-107).

Verdicts are produced by code, structure by structure, from the pricing
specialist's typed findings and the surrounding evidence:

  BLOCK   stale or unusable leg quote; expiry before the horizon end; chain
          older than the configured maximum age; quarantined evidence behind
          the structure's direction
  REVIEW  earnings/catalyst date inside the horizon; verification envelope
          missing or ERROR; account limits not configured (always, in M2)
  PASS    no blocking or review finding. PASS is not permission.

The model (mocked) drafts a narrative only and cannot change a verdict. If it
is unavailable the verdicts stand. This agent changes no structure, no limit
and no other agent's status.
"""
from __future__ import annotations

import json
import re
from datetime import date, datetime, timedelta

from options_research.agents.common import ModelOutputInvalid, load_prompt, parse_json_array
from options_research.envelope import Envelope, Status
from options_research.findings import (
    EventCandidate,
    EvidenceRef,
    RiskReview,
    RiskVerdict,
    StructureComparison,
)
from options_research.intake import ResearchRequest
from options_research.model import Budget, ModelClient, ModelRequest, NoFixture
from options_research.options.chain import OptionChainSnapshot
from options_research.policy.quarantine import Quarantine
from options_research.timeutil import require_utc

AGENT_ID = "risk_reviewer"
PROMPT_VERSION = "v1"
VALID_FOR_MAX = timedelta(hours=6)
UNUSABLE_FLAGS = ("stale", "zero_bid", "crossed", "nonfinite", "no_quote_time")

_MONTHS = {m: i for i, m in enumerate(
    ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november",
     "december"], start=1)}
_DMY = re.compile(r"\b(\d{1,2})(?:st|nd|rd|th)?\s+([A-Za-z]{3,9})\.?(?:,?\s+(\d{4}))?\b")
_MDY = re.compile(r"\b([A-Za-z]{3,9})\.?\s+(\d{1,2})(?:st|nd|rd|th)?(?:,?\s+(\d{4}))?\b")
_ISO = re.compile(r"\b(\d{4})-(\d{2})-(\d{2})\b")


def parse_event_date(text: str, as_of: datetime) -> date | None:
    """First calendar date named in free text. Year defaults to as_of's year, rolling forward if already past."""
    m = _ISO.search(text)
    if m:
        try:
            return date(int(m.group(1)), int(m.group(2)), int(m.group(3)))
        except ValueError:
            return None
    for rx, order in ((_DMY, "dmy"), (_MDY, "mdy")):
        for m in rx.finditer(text):
            mon_txt = (m.group(2) if order == "dmy" else m.group(1)).lower()
            day_txt = m.group(1) if order == "dmy" else m.group(2)
            mon = next((v for k, v in _MONTHS.items() if k.startswith(mon_txt[:3]) and (len(mon_txt) <= 3 or k.startswith(mon_txt))), None)
            if mon is None:
                continue
            year = int(m.group(3)) if m.group(3) else as_of.year
            try:
                d = date(year, mon, int(day_txt))
            except ValueError:
                continue
            if not m.group(3) and d < as_of.date():
                d = date(year + 1, mon, int(day_txt))
            return d
    return None


class RiskReviewer:
    def __init__(self, model: ModelClient, max_chain_age_s: float = 3600.0, max_quote_age_s: float = 900.0,
                 account_limits_configured: bool = False):
        self._model = model
        self._max_chain_age = float(max_chain_age_s)
        self._max_quote_age = float(max_quote_age_s)
        self._limits = bool(account_limits_configured)

    # ---- public ------------------------------------------------------------
    def run(self, pricing: Envelope, chain: OptionChainSnapshot | None, verify: Envelope | None, request: ResearchRequest,
            run_id: str, budget: Budget, project_id: str = "options-research", *, events: list[EventCandidate] | None = None,
            quarantine: Quarantine | None = None, verification_requested: bool = True,
            now: datetime | None = None) -> Envelope:
        as_of = require_utc(request.as_of)
        now = require_utc(now) if now else as_of
        if request.horizon_days is None:
            raise ValueError("risk review of options structures requires horizon_days")
        horizon_end = as_of + timedelta(days=request.horizon_days)
        pv, system = load_prompt(AGENT_ID, PROMPT_VERSION)
        expires_at = as_of + min(timedelta(seconds=self._max_chain_age), VALID_FOR_MAX)
        base = dict(project_id=project_id, run_id=run_id, agent_id=AGENT_ID, model_id=self._model.model_id,
                    prompt_version=pv, as_of=as_of, valid_until=max(expires_at, as_of))
        input_hashes = {"prompt_version": pv, "pricing_envelope": pricing.content_hash(),
                        "horizon_end": horizon_end.isoformat(), "account_limits_configured": str(self._limits).lower()}
        if verify is not None:
            input_hashes["verification_envelope"] = verify.content_hash()
        if chain is not None:
            input_hashes["chain_snapshot"] = chain.snapshot_id
        events = list(events or [])

        # ---- shared (per-run) checks ----------------------------------------
        shared_review: list[str] = []
        shared_checks: list[str] = []
        if not self._limits:
            shared_review.append("account limits not configured")
        shared_checks.append(f"account limits configured: {self._limits}")
        if verification_requested and (verify is None or verify.status is Status.ERROR):
            shared_review.append("evidence verification unavailable" if verify is None else "evidence verification envelope ERROR")
        shared_checks.append(f"verification envelope: {verify.status.value if verify else 'none'}")
        catalysts = self._catalysts_in_horizon(events, as_of, horizon_end)
        shared_checks.append(f"catalyst dates inside horizon: {[d.isoformat() for d, _ in catalysts] or 'none'}")
        chain_age_reason = None
        if chain is not None:
            times = [q.quote_time for q in chain.quotes if q.quote_time is not None]
            if times:
                age = (now - max(times)).total_seconds()
                shared_checks.append(f"chain freshest quote age {age:.0f}s vs max {self._max_chain_age:.0f}s")
                if age > self._max_chain_age:
                    chain_age_reason = f"chain older than max age ({age:.0f}s > {self._max_chain_age:.0f}s)"
            else:
                chain_age_reason = "chain carries no quote times"

        comparisons = [f for f in pricing.findings if isinstance(f, StructureComparison) and f.alternative == "structure"]
        refs: list[EvidenceRef] = [EvidenceRef(kind="envelope", id=f"{pricing.agent_id}:{pricing.content_hash()[:16]}")]
        if chain is not None:
            refs.append(EvidenceRef(kind="snapshot", id=chain.snapshot_id))

        findings: list[RiskReview] = []
        if not comparisons:
            reason = "no priced structure to review: " + ("; ".join(pricing.missing_inputs) or pricing.rationale or "pricing abstained")
            findings.append(RiskReview(comparison_id="none", verdict=RiskVerdict.REVIEW,
                                       reasons=[reason] + shared_review, checks_performed=shared_checks,
                                       expires_at=expires_at, evidence_refs=refs))
        for c in comparisons:
            block: list[str] = []
            review: list[str] = list(shared_review)
            checks: list[str] = list(shared_checks)
            for l in c.legs:
                bad = [f for f in l.liquidity_flags if f in UNUSABLE_FLAGS]
                age = (now - l.quote_time).total_seconds()
                checks.append(f"{l.occ_symbol}: quote age {age:.0f}s, flags {l.liquidity_flags or 'none'}")
                if bad:
                    block.append(f"leg {l.occ_symbol} quote unusable: {bad}")
                elif age > self._max_quote_age:
                    block.append(f"leg {l.occ_symbol} quote stale: {age:.0f}s old vs max {self._max_quote_age:.0f}s")
            checks.append(f"expiry_at {c.expiry_at.isoformat() if c.expiry_at else 'n/a'} vs horizon end {horizon_end.isoformat()}")
            if c.expiry_at is None or c.expiry_at < horizon_end:
                block.append(f"expiry {c.expiry.isoformat() if c.expiry else 'unknown'} is before the horizon end {horizon_end.date().isoformat()}")
            if chain_age_reason:
                block.append(chain_age_reason)
            for d, label in catalysts:
                review.append(f"scheduled event inside horizon on {d.isoformat()}: {label}")
            if quarantine and quarantine.blocks_new_entry:
                behind = [e.cluster_id for e in events
                          if e.cluster_id in quarantine.quarantined_evidence_ids and e.implication == c.direction]
                if behind:
                    block.append(f"quarantined evidence behind the {c.direction} direction: {behind}")
                else:
                    review.append("quarantined evidence present in this report (other direction)")
            verdict = RiskVerdict.BLOCK if block else (RiskVerdict.REVIEW if review else RiskVerdict.PASS)
            if verdict is RiskVerdict.PASS and not self._limits:  # stop condition, defence in depth
                raise RuntimeError("PASS while account limits are unknown is not allowed")
            findings.append(RiskReview(comparison_id=c.comparison_id, verdict=verdict, reasons=block + review,
                                       checks_performed=checks, expires_at=expires_at,
                                       evidence_refs=refs + [EvidenceRef(kind="receipt", id=r) for r in c.receipt_ids[:2]]))

        missing: list[str] = []
        narratives: dict[str, str] = {}
        if comparisons:
            try:
                narratives = self._narratives(comparisons, findings, pv, system, budget)
            except NoFixture as exc:
                missing.append(f"narrative unavailable: {exc}")
            except ModelOutputInvalid as exc:
                missing.append(f"narrative rejected: {exc}")
        findings = [f.model_copy(update={"note": narratives.get(f.comparison_id, f.note)}) for f in findings]
        worst = RiskVerdict.BLOCK if any(f.verdict is RiskVerdict.BLOCK for f in findings) else (
            RiskVerdict.REVIEW if any(f.verdict is RiskVerdict.REVIEW for f in findings) else RiskVerdict.PASS)
        status = {RiskVerdict.BLOCK: Status.BLOCK, RiskVerdict.REVIEW: Status.REVIEW, RiskVerdict.PASS: Status.OK}[worst]
        counts = {v.value: sum(1 for f in findings if f.verdict is v) for v in RiskVerdict}
        return Envelope(**base, status=status, input_hashes=input_hashes, evidence_refs=sorted(set(refs), key=lambda r: (r.kind, r.id)),
                        findings=findings, missing_inputs=missing,
                        rationale=", ".join(f"{k}={v}" for k, v in counts.items() if v) +
                                  f"; verdicts expire {expires_at.isoformat()}; PASS would not be permission")

    # ---- helpers -----------------------------------------------------------
    @staticmethod
    def _catalysts_in_horizon(events: list[EventCandidate], as_of: datetime, horizon_end: datetime) -> list[tuple[date, str]]:
        out: list[tuple[date, str]] = []
        for e in events:
            if not e.catalyst:
                continue
            d = parse_event_date(e.catalyst, as_of)
            if d is not None and as_of.date() <= d <= horizon_end.date():
                out.append((d, f"{e.catalyst} ({e.cluster_id})"))
        return sorted(set(out))

    def _narratives(self, comparisons: list[StructureComparison], findings: list[RiskReview], pv: str, system: str,
                    budget: Budget) -> dict[str, str]:
        by_id = {c.comparison_id: c for c in comparisons}
        payload = [{"kind": by_id[f.comparison_id].kind, "verdict": f.verdict.value, "reasons": f.reasons,
                    "checks_performed": f.checks_performed} for f in findings if f.comparison_id in by_id]
        req = ModelRequest(role=AGENT_ID, prompt_version=pv, system=system,
                           messages=({"role": "user", "content": "Reviews:\n" + json.dumps(payload, indent=1)},))
        resp = self._model.complete(req, budget)
        kind_to_id = {c.kind: c.comparison_id for c in comparisons}
        verdict_of = {f.comparison_id: f.verdict.value for f in findings}
        out: dict[str, str] = {}
        for item in parse_json_array(resp.text):
            if set(item) != {"kind", "narrative"} or item["kind"] not in kind_to_id:
                raise ModelOutputInvalid(f"bad narrative item {sorted(item)} / {item.get('kind')!r}")
            text = str(item["narrative"])
            cid = kind_to_id[item["kind"]]
            # a narrative may not announce a different verdict than code produced
            for v in ("PASS", "BLOCK", "REVIEW"):
                if v in text.upper().split() and v != verdict_of[cid]:
                    raise ModelOutputInvalid(f"narrative for {item['kind']} states {v}, code verdict is {verdict_of[cid]}")
            out[cid] = text
        return out
