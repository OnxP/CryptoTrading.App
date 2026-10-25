"""Append-only JSONL evidence ledger with as-of queries and origin clustering."""
from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path

from options_research.evidence.records import EvidenceRecord, QueryResult, SourceCluster
from options_research.timeutil import require_utc


class DuplicateRecord(ValueError):
    pass


class EvidenceStore:
    def __init__(self, path: Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._records: dict[str, EvidenceRecord] = {}
        self._order: list[str] = []
        if self.path.exists():
            with open(self.path, encoding="utf-8") as fh:
                for line in fh:
                    if line.strip():
                        rec = EvidenceRecord.model_validate_json(line)
                        self._records[rec.id] = rec
                        self._order.append(rec.id)

    # ---- writes (append only) ---------------------------------------------
    def append(self, rec: EvidenceRecord) -> EvidenceRecord:
        if rec.id in self._records:
            raise DuplicateRecord(f"record {rec.id} already exists; append a correction instead")
        if rec.supersedes is not None and rec.supersedes not in self._records:
            raise ValueError(f"supersedes unknown record {rec.supersedes}")
        with open(self.path, "a", encoding="utf-8") as fh:
            fh.write(rec.model_dump_json() + "\n")
        self._records[rec.id] = rec
        self._order.append(rec.id)
        return rec

    def append_correction(self, original_id: str, *, new_id: str, title: str, text: str,
                          published_at: datetime, first_available_at: datetime,
                          retrieved_at: datetime) -> EvidenceRecord:
        orig = self.get(original_id)
        corrected = EvidenceRecord(
            id=new_id,
            source_id=orig.source_id,
            publisher=orig.publisher,
            origin_id=orig.origin_id,
            published_at=published_at,
            first_available_at=first_available_at,
            retrieved_at=retrieved_at,
            revision=orig.revision + 1,
            supersedes=orig.id,
            title=title,
            text=text,
            entities=orig.entities,
        )
        return self.append(corrected)

    @classmethod
    def load_fixture(cls, ledger_path: Path, fixture_json: Path) -> "EvidenceStore":
        store = cls(ledger_path)
        with open(fixture_json, encoding="utf-8") as fh:
            raw = json.load(fh)
        for item in raw["records"]:
            rec = EvidenceRecord(**item)
            if rec.id not in store._records:
                store.append(rec)
        return store

    # ---- reads -------------------------------------------------------------
    def get(self, record_id: str) -> EvidenceRecord:
        return self._records[record_id]

    def all(self) -> list[EvidenceRecord]:
        return [self._records[i] for i in self._order]

    def query(self, *, as_of: datetime, window_start: datetime, window_end: datetime,
              entities: list[str] | None = None, allow_backfill: bool = False) -> QueryResult:
        """As-of query. Backfilled records are hidden unless `allow_backfill` (the caller must disclose it)."""
        as_of = require_utc(as_of)
        window_start, window_end = require_utc(window_start), require_utc(window_end)
        wanted = {e.upper() for e in (entities or [])}

        excluded_future: list[str] = []
        excluded_backfill: list[str] = []
        backfill_included: list[str] = []
        superseded_by_visible: set[str] = set()
        candidates: list[EvidenceRecord] = []
        for rec in self.all():
            if wanted and not (wanted & {e.upper() for e in rec.entities}):
                continue
            if rec.first_available_at > as_of:
                excluded_future.append(rec.id)
                continue
            if not (window_start <= rec.published_at <= window_end):
                continue
            if rec.backfill:
                if not allow_backfill:
                    excluded_backfill.append(rec.id)
                    continue
                backfill_included.append(rec.id)
            candidates.append(rec)
        # A correction that was itself available by as_of hides its predecessor.
        for rec in candidates:
            if rec.supersedes:
                superseded_by_visible.add(rec.supersedes)
        visible = [r for r in candidates if r.id not in superseded_by_visible]

        clusters: dict[str, list[EvidenceRecord]] = {}
        for rec in visible:
            clusters.setdefault(rec.cluster_key, []).append(rec)
        cluster_models = []
        for key, members in clusters.items():
            members_sorted = sorted(members, key=lambda r: r.first_available_at)
            origin = next((m for m in members_sorted if m.origin_id is None), members_sorted[0])
            cluster_models.append(SourceCluster(
                cluster_id=f"cluster:{key}",
                origin_record_id=origin.id,
                member_ids=[m.id for m in members_sorted],
                publishers=sorted({m.publisher for m in members_sorted}),
                independent_sources=1,  # one origin == one independent source, by construction
                earliest_available_at=members_sorted[0].first_available_at,
                title=origin.title,
            ))
        cluster_models.sort(key=lambda c: c.earliest_available_at)
        return QueryResult(
            as_of=as_of,
            window_start=window_start,
            window_end=window_end,
            records=visible,
            clusters=cluster_models,
            excluded_future=excluded_future,
            excluded_superseded=sorted(superseded_by_visible),
            excluded_backfill=excluded_backfill,
            backfill_included=backfill_included,
        )
