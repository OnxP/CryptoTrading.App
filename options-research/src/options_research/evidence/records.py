"""Point-in-time evidence records.

The as-of filter uses `first_available_at`, never `published_at`: a backdated
or late-indexed article was not available to a decision made before it arrived.
"""
from __future__ import annotations

import hashlib
import json
from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

from options_research.timeutil import require_utc


def text_hash(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


class EvidenceRecord(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    id: str = Field(min_length=1)
    source_id: str = Field(description="publisher-specific id or URL")
    publisher: str
    origin_id: str | None = Field(
        default=None,
        description="id of the originating report when this is a syndicated copy; "
                    "None means this record is its own origin",
    )
    published_at: datetime
    first_available_at: datetime
    retrieved_at: datetime
    revision: int = 1
    supersedes: str | None = None
    title: str
    text: str
    entities: list[str] = Field(default_factory=list)
    content_hash: str = ""

    _utc = field_validator("published_at", "first_available_at", "retrieved_at")(require_utc)

    @model_validator(mode="after")
    def _fill_hash(self) -> "EvidenceRecord":
        h = text_hash(self.title + "\n" + self.text)
        if self.content_hash and self.content_hash != h:
            raise ValueError("content_hash does not match title+text")
        if not self.content_hash:
            object.__setattr__(self, "content_hash", h)
        if self.first_available_at < self.published_at:
            # allowed in principle (pre-release embargo), but flag by refusing silently odd data
            raise ValueError("first_available_at precedes published_at")
        return self

    @property
    def cluster_key(self) -> str:
        return self.origin_id or self.id


class SourceCluster(BaseModel):
    """One originating story and all its copies. Copies are not corroboration."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    cluster_id: str
    origin_record_id: str
    member_ids: list[str]
    publishers: list[str]
    independent_sources: int
    earliest_available_at: datetime
    title: str

    _utc = field_validator("earliest_available_at")(require_utc)


class QueryResult(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    as_of: datetime
    window_start: datetime
    window_end: datetime
    records: list[EvidenceRecord]
    clusters: list[SourceCluster]
    excluded_future: list[str] = Field(description="record ids first available after as_of")
    excluded_superseded: list[str] = Field(default_factory=list)

    _utc = field_validator("as_of", "window_start", "window_end")(require_utc)

    def result_hash(self) -> str:
        payload = {
            "as_of": self.as_of.isoformat(),
            "window": [self.window_start.isoformat(), self.window_end.isoformat()],
            "records": sorted(r.id + ":" + r.content_hash for r in self.records),
            "excluded_future": sorted(self.excluded_future),
        }
        return hashlib.sha256(json.dumps(payload, sort_keys=True).encode("utf-8")).hexdigest()
