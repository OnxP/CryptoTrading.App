"""Deterministic quarantine policy.

A CONTRADICTED material claim quarantines the evidence it rests on for
downstream sections of THIS report. Nothing is deleted or rewritten; the
evidence store is untouched. Descriptive research may still show the claim,
labelled; any new-entry recommendation depending on it is blocked (M2+).
"""
from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field

from options_research.findings import ClaimStatus, ClaimVerification, Severity


class Quarantine(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    quarantined_claim_ids: list[str] = Field(default_factory=list)
    quarantined_evidence_ids: list[str] = Field(default_factory=list)
    blocks_new_entry: bool = False
    reasons: list[str] = Field(default_factory=list)


class QuarantinePolicy:
    MATERIAL = {Severity.MEDIUM, Severity.HIGH}

    def apply(self, verifications: list[ClaimVerification]) -> Quarantine:
        claims, evidence, reasons = [], [], []
        for v in verifications:
            if v.status is ClaimStatus.CONTRADICTED and v.severity in self.MATERIAL:
                claims.append(v.claim_id)
                evidence.extend(r.id for r in v.evidence_refs if r.kind in ("evidence", "cluster"))
                reasons.append(f"{v.claim_id}: CONTRADICTED ({v.severity.value})")
        return Quarantine(
            quarantined_claim_ids=claims,
            quarantined_evidence_ids=sorted(set(evidence)),
            blocks_new_entry=bool(claims),
            reasons=reasons,
        )
