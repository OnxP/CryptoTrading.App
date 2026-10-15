"""Append-only report ledger. A report is never modified after it is written."""
from __future__ import annotations

import hashlib
import json
from datetime import datetime
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator

from options_research.envelope import Envelope
from options_research.findings import BriefingSection
from options_research.intake import ResearchRequest
from options_research.timeutil import require_utc


class ReportRecord(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    report_id: str
    run_id: str
    created_at: datetime
    project_id: str
    request: ResearchRequest
    parent_report_id: str | None = None
    as_of: datetime
    input_hashes: dict[str, str]
    envelopes: list[Envelope]
    sections: list[BriefingSection]
    status: Literal["OK", "PARTIAL"]
    disclosures: list[str] = Field(default_factory=list)
    content_hash: str = ""

    _utc = field_validator("created_at", "as_of")(require_utc)

    def compute_content_hash(self) -> str:
        """Hash of everything that should be identical across two identical runs.

        Excludes report_id, run_id, created_at, request.request_id and the run_id
        inside each envelope, so determinism can be asserted.
        """
        payload = {
            "request": self.request.model_dump(mode="json", exclude={"request_id"}),
            "as_of": self.as_of.isoformat(),
            "input_hashes": self.input_hashes,
            "envelopes": [e.model_dump(mode="json", exclude={"run_id"}) for e in self.envelopes],
            "sections": [s.model_dump(mode="json") for s in self.sections],
            "status": self.status,
            "disclosures": self.disclosures,
        }
        return hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


class ReportLedger:
    def __init__(self, path: Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def append(self, record: ReportRecord) -> ReportRecord:
        if not record.content_hash:
            record = record.model_copy(update={"content_hash": record.compute_content_hash()})
        with open(self.path, "a", encoding="utf-8") as fh:
            fh.write(record.model_dump_json() + "\n")
        return record

    def all(self) -> list[ReportRecord]:
        if not self.path.exists():
            return []
        with open(self.path, encoding="utf-8") as fh:
            return [ReportRecord.model_validate_json(line) for line in fh if line.strip()]

    def get(self, report_id: str) -> ReportRecord:
        for r in self.all():
            if r.report_id == report_id:
                return r
        raise KeyError(report_id)
