"""Typed findings every agent returns inside an Envelope.

Each finding carries `evidence_refs`. A finding is a claim about the world or
a reference to a code-produced record; it is never an instruction to act.
"""
from __future__ import annotations

from datetime import datetime
from enum import Enum
from typing import Annotated, Literal, Union

from pydantic import BaseModel, ConfigDict, Field, field_validator

from options_research.timeutil import require_utc


class EvidenceRef(BaseModel):
    """Pointer to an immutable record: evidence, cluster, snapshot or receipt."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    kind: Literal["evidence", "cluster", "snapshot", "receipt", "report", "envelope"]
    id: str = Field(min_length=1)
    content_hash: str | None = None


class Severity(str, Enum):
    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"


class _Finding(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    evidence_refs: list[EvidenceRef] = Field(min_length=1)
    note: str = ""


class EventCandidate(_Finding):
    """News collector output: one source cluster labelled, not a trade view."""

    type: Literal["event_candidate"] = "event_candidate"
    cluster_id: str
    headline: str
    entities: list[str] = Field(default_factory=list)
    event_time: datetime | None = None
    fact_or_rumour: Literal["fact", "rumour", "unclear"] = "unclear"
    novelty: Literal["new", "update", "repeat"] = "new"
    independent_sources: int = Field(ge=0)
    missing_corroboration: list[str] = Field(default_factory=list)
    implication: Literal["bullish", "bearish", "mixed", "unclear"] = "unclear"
    catalyst: str | None = Field(default=None, description="scheduled future event named in the story, if any")
    injection_suspected: bool = False

    _utc = field_validator("event_time")(lambda v: require_utc(v) if v is not None else v)


class ClaimStatus(str, Enum):
    SOURCE_SUPPORTED = "SOURCE_SUPPORTED"
    CONTRADICTED = "CONTRADICTED"
    INSUFFICIENT = "INSUFFICIENT"
    STALE = "STALE"


class ClaimVerification(_Finding):
    """Evidence verifier output for one claim."""

    type: Literal["claim_verification"] = "claim_verification"
    claim_id: str
    claim_text: str
    status: ClaimStatus
    checks_performed: list[str] = Field(default_factory=list)
    severity: Severity = Severity.LOW
    unresolved_disagreements: list[str] = Field(default_factory=list)
    expires_at: datetime | None = None

    _utc = field_validator("expires_at")(lambda v: require_utc(v) if v is not None else v)


class MarketSnapshotRef(_Finding):
    """Reference to a code-produced market snapshot and its quality."""

    type: Literal["market_snapshot_ref"] = "market_snapshot_ref"
    snapshot_id: str
    instrument: str
    as_of: datetime
    last_complete_bar: datetime | None = None
    quality: Literal["OK", "DEGRADED", "UNAVAILABLE"]
    quality_failures: list[str] = Field(default_factory=list)

    _utc = field_validator("as_of", "last_complete_bar")(
        lambda v: require_utc(v) if v is not None else v
    )


class BriefingSection(_Finding):
    """One assembled section of a briefing; BLOCKED/PARTIAL when a specialist failed."""

    type: Literal["briefing_section"] = "briefing_section"
    section: Literal[
        "instrument",
        "scope",
        "material_news",
        "market_reaction",
        "bullish_evidence",
        "bearish_evidence",
        "catalysts",
        "uncertainties",
        "invalidation_conditions",
        "next_investigations",
    ]
    status: Literal["OK", "PARTIAL", "BLOCKED"]
    text: str
    blocked_reason: str | None = None


Finding = Annotated[
    Union[EventCandidate, ClaimVerification, MarketSnapshotRef, BriefingSection],
    Field(discriminator="type"),
]
