"""Evidence verifier agent.

Verdicts are produced by code:
  * numeric claims ("fell 8%", "-8%", "down 8 per cent") are rechecked against
    the market snapshot's calculation receipts;
  * non-numeric claims are judged on independent sources available by as-of.
The model (mocked in M1) only drafts a one-sentence narrative per claim and
cannot change a status. If the model is unavailable the verdicts still stand.
"""
from __future__ import annotations

import json
import re
from datetime import datetime, timedelta

from pydantic import BaseModel, ConfigDict, Field

from options_research.agents.common import ModelOutputInvalid, load_prompt, parse_json_array
from options_research.envelope import Envelope, Status
from options_research.evidence import EvidenceStore, QueryResult
from options_research.findings import ClaimStatus, ClaimVerification, EvidenceRef, Severity
from options_research.market import MarketSnapshot
from options_research.model import Budget, ModelClient, ModelRequest, NoFixture
from options_research.timeutil import require_utc

AGENT_ID = "evidence_verifier"
PROMPT_VERSION = "v1"
VALID_FOR = timedelta(hours=6)

# "fell 8%", "down 8.5 %", "-8%", "drop of 8 per cent", "rose 3%"
_PCT = re.compile(
    r"(?P<verb>fell|fall|falls|down|drop|dropped|declined|decline|slid|plunged|lost|rose|rise|up|gained|jumped|surged)"
    r"[^0-9%\-+]{0,12}(?P<sign>[-+]?)(?P<num>\d+(?:\.\d+)?)\s*(?:%|per ?cent|percent)",
    re.IGNORECASE,
)
_DOWN = {"fell", "fall", "falls", "down", "drop", "dropped", "declined", "decline", "slid", "plunged", "lost"}
TOLERANCE_PP = 1.5  # percentage points


class Claim(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    claim_id: str
    text: str
    cluster_ids: list[str] = Field(default_factory=list)
    material: bool = True


class EvidenceVerifier:
    def __init__(self, model: ModelClient, store: EvidenceStore):
        self._model = model
        self._store = store

    # ---- public ------------------------------------------------------------
    def run(self, claims: list[Claim], query: QueryResult, snapshot: MarketSnapshot | None,
            as_of: datetime, run_id: str, budget: Budget, project_id: str = "options-research") -> Envelope:
        as_of = require_utc(as_of)
        pv, system = load_prompt(AGENT_ID, PROMPT_VERSION)
        verdicts = [self._verify(c, query, snapshot, as_of) for c in claims]
        input_hashes = {"evidence_query": query.result_hash(), "prompt_version": pv,
                        "claims": _hash_claims(claims)}
        if snapshot is not None:
            input_hashes["market_snapshot"] = snapshot.snapshot_id
        refs = sorted({r for v in verdicts for r in v.evidence_refs}, key=lambda r: (r.kind, r.id))
        base = dict(project_id=project_id, run_id=run_id, agent_id=AGENT_ID, model_id=self._model.model_id,
                    prompt_version=pv, input_hashes=input_hashes, as_of=as_of, valid_until=as_of + VALID_FOR)
        if not claims:
            return Envelope(**base, status=Status.ABSTAIN, missing_inputs=["no claims to verify"])

        missing: list[str] = []
        narratives: dict[str, str] = {}
        try:
            narratives = self._narratives(verdicts, pv, system, budget)
        except NoFixture as exc:
            missing.append(f"narrative unavailable: {exc}")
        except ModelOutputInvalid as exc:
            missing.append(f"narrative rejected: {exc}")
        findings = [v.model_copy(update={"note": narratives.get(v.claim_id, v.note)}) for v in verdicts]
        status = Status.OK if refs else Status.REVIEW
        return Envelope(**base, status=status, evidence_refs=refs, findings=findings, missing_inputs=missing,
                        rationale=_summary(findings))

    # ---- code verdicts -----------------------------------------------------
    def _verify(self, claim: Claim, query: QueryResult, snapshot: MarketSnapshot | None,
                as_of: datetime) -> ClaimVerification:
        checks: list[str] = []
        clusters = [c for c in query.clusters if c.cluster_id in claim.cluster_ids]
        independent = sum(c.independent_sources for c in clusters)
        refs = [EvidenceRef(kind="cluster", id=c.cluster_id) for c in clusters]
        future = [rid for rid in query.excluded_future if any(rid in cid for cid in claim.cluster_ids)]
        sev = Severity.MEDIUM if claim.material else Severity.LOW
        unresolved: list[str] = []

        m = _PCT.search(claim.text)
        if m and snapshot is not None:
            claimed = float(m.group("num")) / 100.0
            if m.group("verb").lower() in _DOWN or m.group("sign") == "-":
                claimed = -abs(claimed)
            receipt_name = "largest_single_bar_drop" if claimed < 0 else "window_return"
            rcpt = snapshot.receipts.get(receipt_name)
            checks.append(f"numeric recheck of {claimed:+.1%} against receipt {receipt_name}")
            if rcpt is None or rcpt.result is None:
                checks.append("receipt unavailable")
                unresolved.append(f"{receipt_name} unavailable: {getattr(rcpt, 'unavailable_reason', 'no receipt')}")
                status = ClaimStatus.INSUFFICIENT
            else:
                refs.append(EvidenceRef(kind="receipt", id=rcpt.receipt_id, content_hash=rcpt.inputs_hash))
                diff_pp = abs(claimed - rcpt.result) * 100
                checks.append(f"receipt result {rcpt.result:+.2%}, difference {diff_pp:.2f}pp, tolerance {TOLERANCE_PP}pp")
                if diff_pp <= TOLERANCE_PP:
                    status = ClaimStatus.SOURCE_SUPPORTED
                    sev = Severity.LOW
                else:
                    status = ClaimStatus.CONTRADICTED
                    sev = Severity.HIGH if claim.material else Severity.LOW
                    unresolved.append(f"claimed {claimed:+.1%} vs computed {rcpt.result:+.2%} ({rcpt.receipt_id})")
        elif m and snapshot is None:
            checks.append("numeric claim but no market snapshot available")
            unresolved.append("market snapshot unavailable; number not rechecked")
            status = ClaimStatus.INSUFFICIENT
        else:
            checks.append(f"independent sources available by as_of: {independent}")
            if not clusters and future:
                status = ClaimStatus.STALE
                unresolved.append(f"only source(s) {future} first available after as_of")
                refs.extend(EvidenceRef(kind="evidence", id=rid) for rid in future)
            elif independent >= 2:
                status = ClaimStatus.SOURCE_SUPPORTED
            else:
                status = ClaimStatus.INSUFFICIENT
                if clusters:
                    copies = sum(len(c.member_ids) for c in clusters)
                    unresolved.append(f"{copies} publisher copy/copies of {independent} origin; not independent corroboration")
                else:
                    unresolved.append("no source available by as_of")

        if not refs:  # keep schema: a verification without a source still points at the query
            refs.append(EvidenceRef(kind="report", id=f"query:{query.result_hash()[:16]}"))
        return ClaimVerification(
            claim_id=claim.claim_id, claim_text=claim.text, status=status, checks_performed=checks,
            severity=sev, unresolved_disagreements=unresolved, expires_at=as_of + VALID_FOR,
            evidence_refs=refs, note="",
        )

    # ---- model narrative (optional) ----------------------------------------
    def _narratives(self, verdicts: list[ClaimVerification], pv: str, system: str, budget: Budget) -> dict[str, str]:
        payload = [v.model_dump(mode="json", include={"claim_id", "claim_text", "status", "checks_performed",
                                                      "unresolved_disagreements"}) for v in verdicts]
        req = ModelRequest(role=AGENT_ID, prompt_version=pv, system=system,
                           messages=({"role": "user", "content": "Claims:\n" + json.dumps(payload, indent=1)},))
        resp = self._model.complete(req, budget)
        out: dict[str, str] = {}
        ids = {v.claim_id for v in verdicts}
        for item in parse_json_array(resp.text):
            if set(item) != {"claim_id", "narrative"} or item["claim_id"] not in ids:
                raise ModelOutputInvalid(f"bad narrative item {item}")
            out[item["claim_id"]] = str(item["narrative"])
        return out


def _hash_claims(claims: list[Claim]) -> str:
    import hashlib
    return hashlib.sha256(json.dumps([c.model_dump() for c in claims], sort_keys=True).encode()).hexdigest()


def _summary(findings: list[ClaimVerification]) -> str:
    counts: dict[str, int] = {}
    for f in findings:
        counts[f.status.value] = counts.get(f.status.value, 0) + 1
    return ", ".join(f"{k}={v}" for k, v in sorted(counts.items()))
