"""Required output envelope for every agent (CLAUDE_SETUP "Common context").

Status OK means "the agent produced usable findings". It never authorises a
trade: there is deliberately no approval, order or execution concept here.
"""
from __future__ import annotations

import hashlib
import json
from datetime import datetime
from enum import Enum

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

from options_research.findings import EvidenceRef, Finding
from options_research.timeutil import require_utc


class Status(str, Enum):
    OK = "OK"
    ABSTAIN = "ABSTAIN"
    REVIEW = "REVIEW"
    BLOCK = "BLOCK"
    ERROR = "ERROR"


class Envelope(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    project_id: str = Field(min_length=1)
    run_id: str = Field(min_length=1)
    agent_id: str = Field(min_length=1)
    model_id: str = Field(min_length=1, description="model used, or 'none' for deterministic agents")
    prompt_version: str = Field(min_length=1, description="e.g. news_collector/v1, or 'none'")
    input_hashes: dict[str, str] = Field(default_factory=dict)
    as_of: datetime
    valid_until: datetime
    status: Status
    evidence_refs: list[EvidenceRef] = Field(default_factory=list)
    findings: list[Finding] = Field(default_factory=list)
    missing_inputs: list[str] = Field(default_factory=list)
    counterevidence: list[str] = Field(default_factory=list)
    rationale: str = Field(default="", description="brief evidence-backed rationale, not chain-of-thought")

    _utc = field_validator("as_of", "valid_until")(require_utc)

    @model_validator(mode="after")
    def _valid_window(self) -> "Envelope":
        if self.valid_until < self.as_of:
            raise ValueError("valid_until must not precede as_of")
        return self

    # ---- usability ---------------------------------------------------------
    def is_expired(self, now: datetime) -> bool:
        return require_utc(now) > self.valid_until

    def is_usable(self, now: datetime) -> bool:
        """Usable = not expired, not ERROR, and OK only with evidence."""
        if self.is_expired(now):
            return False
        if self.status is Status.ERROR:
            return False
        if self.status is Status.OK and not self.evidence_refs:
            return False
        return True

    # ---- hashing -----------------------------------------------------------
    def canonical_json(self) -> str:
        return json.dumps(self.model_dump(mode="json"), sort_keys=True, separators=(",", ":"))

    def content_hash(self) -> str:
        return hashlib.sha256(self.canonical_json().encode("utf-8")).hexdigest()

    @classmethod
    def from_json(cls, text: str) -> "Envelope":
        return cls.model_validate_json(text)
