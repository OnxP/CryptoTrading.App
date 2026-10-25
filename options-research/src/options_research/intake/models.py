"""Research request schema (CLAUDE_SETUP "Ad hoc research requests")."""
from __future__ import annotations

from datetime import datetime
from enum import Enum
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

from options_research.timeutil import require_utc


class IntakeError(ValueError):
    """Request rejected outright (future as-of, email delivery in M1, ...)."""


class Instrument(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    symbol: str
    venue: str = Field(description="ISO 10383 MIC, e.g. XNAS")
    currency: str
    name: str

    @property
    def canonical(self) -> str:
        return f"{self.venue}:{self.symbol}"


class Window(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    start: datetime
    end: datetime
    source: Literal["explicit", "default"]

    _utc = field_validator("start", "end")(require_utc)

    @model_validator(mode="after")
    def _ordered(self) -> "Window":
        if self.end < self.start:
            raise ValueError("window end precedes start")
        return self


class Analysis(str, Enum):
    NEWS_SUMMARY = "news_summary"
    MARKET_REACTION = "market_reaction"
    EVIDENCE_VERIFICATION = "evidence_verification"
    # time-dependent: requires a trade horizon before it may be requested
    OPTIONS_STRUCTURES = "options_structures"


TIME_DEPENDENT = frozenset({Analysis.OPTIONS_STRUCTURES})


class ResearchRequest(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    request_id: str
    requester: str
    original_question: str
    project: Literal["options-research"] = "options-research"
    instrument: Instrument
    evidence_links: list[str] = Field(default_factory=list)
    pasted_text: str | None = None
    window: Window
    as_of: datetime
    as_of_source: Literal["explicit", "request_time"]
    horizon_days: int | None = None
    requested_analyses: list[Analysis]
    delivery: Literal["inline"] = "inline"
    parent_request_id: str | None = None
    disclosures: list[str] = Field(default_factory=list)

    _utc = field_validator("as_of")(require_utc)

    @model_validator(mode="after")
    def _window_within_as_of(self) -> "ResearchRequest":
        if self.window.end > self.as_of:
            raise ValueError("window end is after as_of; evidence after the cutoff is not usable")
        for a in self.requested_analyses:
            if a in TIME_DEPENDENT and self.horizon_days is None:
                raise ValueError(f"{a.value} requires horizon_days")
        return self


class Clarification(BaseModel):
    """Returned instead of a request when one specific thing must be asked."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    reason: Literal["instrument_unresolved", "instrument_ambiguous", "window_missing", "horizon_missing"]
    question: str
    candidates: list[Instrument] = Field(default_factory=list)
