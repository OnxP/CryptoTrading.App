"""Append-only usage ledger (JSONL). One line per model call."""
from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from datetime import datetime
from pathlib import Path


@dataclass(frozen=True)
class UsageRecord:
    at: datetime
    request_id: str
    role: str
    prompt_version: str
    model_id: str
    input_hash: str
    input_tokens: int | None
    output_tokens: int | None
    cost_estimate_usd: float | None  # None == UNKNOWN

    def to_json(self) -> str:
        d = asdict(self)
        d["at"] = self.at.isoformat()
        return json.dumps(d, sort_keys=True, separators=(",", ":"))


class UsageLedger:
    def __init__(self, path: Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def append(self, record: UsageRecord) -> None:
        with open(self.path, "a", encoding="utf-8") as fh:
            fh.write(record.to_json() + "\n")

    def read_all(self) -> list[dict]:
        if not self.path.exists():
            return []
        with open(self.path, encoding="utf-8") as fh:
            return [json.loads(line) for line in fh if line.strip()]
