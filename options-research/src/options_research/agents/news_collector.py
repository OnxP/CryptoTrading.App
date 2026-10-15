"""News collector agent.

Code decides WHICH clusters exist (evidence store, as-of filtered). The model
only labels them. Any label for an unknown cluster, or any schema violation,
turns the whole envelope into ERROR: partial acceptance of a malformed model
answer is not allowed.
"""
from __future__ import annotations

import json
from datetime import timedelta

from pydantic import ValidationError

from options_research.agents.common import (
    ModelOutputInvalid,
    delimit_untrusted,
    injection_suspected,
    load_prompt,
    parse_json_array,
)
from options_research.envelope import Envelope, Status
from options_research.evidence import EvidenceStore, QueryResult
from options_research.findings import EventCandidate, EvidenceRef
from options_research.intake import ResearchRequest
from options_research.model import Budget, ModelClient, ModelRequest, NoFixture

AGENT_ID = "news_collector"
PROMPT_VERSION = "v1"
VALID_FOR = timedelta(hours=6)

_LABEL_KEYS = {"cluster_id", "entities", "fact_or_rumour", "novelty", "missing_corroboration",
               "implication", "catalyst", "injection_suspected", "note"}


class NewsCollector:
    def __init__(self, model: ModelClient, store: EvidenceStore):
        self._model = model
        self._store = store

    # ---- public ------------------------------------------------------------
    def query(self, request: ResearchRequest) -> QueryResult:
        return self._store.query(
            as_of=request.as_of,
            window_start=request.window.start,
            window_end=request.window.end,
            entities=[request.instrument.symbol],
        )

    def run(self, request: ResearchRequest, run_id: str, budget: Budget,
            project_id: str = "options-research") -> Envelope:
        result = self.query(request)
        pv, system = load_prompt(AGENT_ID, PROMPT_VERSION)
        input_hashes = {"evidence_query": result.result_hash(), "prompt_version": pv}
        base = dict(project_id=project_id, run_id=run_id, agent_id=AGENT_ID,
                    model_id=self._model.model_id, prompt_version=pv, input_hashes=input_hashes,
                    as_of=request.as_of, valid_until=request.as_of + VALID_FOR)
        cluster_refs = [EvidenceRef(kind="cluster", id=c.cluster_id) for c in result.clusters]

        if not result.clusters:
            return Envelope(**base, status=Status.ABSTAIN, evidence_refs=[],
                            missing_inputs=["no evidence available in window by as_of"],
                            rationale=f"{len(result.excluded_future)} record(s) excluded as not yet available")

        user = self._user_message(request, result)
        req = ModelRequest(role=AGENT_ID, prompt_version=pv, system=system,
                           messages=({"role": "user", "content": user},))
        try:
            resp = self._model.complete(req, budget)
        except NoFixture as exc:
            return Envelope(**base, status=Status.ERROR, evidence_refs=cluster_refs,
                            missing_inputs=[f"model response unavailable: {exc}"])
        try:
            findings = self._findings(result, parse_json_array(resp.text))
        except (ModelOutputInvalid, ValidationError) as exc:
            return Envelope(**base, status=Status.ERROR, evidence_refs=cluster_refs,
                            missing_inputs=[f"model output rejected: {exc}"])

        counter = [f"{len(result.excluded_future)} record(s) excluded: first available after as_of"] \
            if result.excluded_future else []
        return Envelope(**base, status=Status.OK, evidence_refs=cluster_refs, findings=findings,
                        counterevidence=counter,
                        rationale=f"{len(result.clusters)} source cluster(s) from {len(result.records)} record(s)")

    # ---- internals ---------------------------------------------------------
    def _user_message(self, request: ResearchRequest, result: QueryResult) -> str:
        parts = [
            f"Instrument: {request.instrument.canonical} ({request.instrument.name})",
            f"Decision cutoff (as_of): {request.as_of.isoformat()}",
            f"Window: {request.window.start.isoformat()} to {request.window.end.isoformat()}",
            f"Clusters: {len(result.clusters)}. Return exactly one object per cluster_id below.",
        ]
        by_id = {r.id: r for r in result.records}
        for c in result.clusters:
            origin = by_id[c.origin_record_id]
            parts.append(json.dumps({
                "cluster_id": c.cluster_id, "origin_record_id": c.origin_record_id,
                "publishers": c.publishers, "copies": len(c.member_ids),
                "independent_sources": c.independent_sources,
                "earliest_available_at": c.earliest_available_at.isoformat(),
            }))
            parts.append(delimit_untrusted(origin.id, origin.title, origin.text))
        return "\n".join(parts)

    def _findings(self, result: QueryResult, labels: list[dict]) -> list[EventCandidate]:
        clusters = {c.cluster_id: c for c in result.clusters}
        by_id = {r.id: r for r in result.records}
        seen: set[str] = set()
        out: list[EventCandidate] = []
        for lab in labels:
            unknown = set(lab) - _LABEL_KEYS
            if unknown:
                raise ModelOutputInvalid(f"unexpected keys {sorted(unknown)}")
            cid = lab.get("cluster_id")
            if cid not in clusters:
                raise ModelOutputInvalid(f"label for unknown cluster {cid!r}")
            if cid in seen:
                raise ModelOutputInvalid(f"duplicate label for {cid}")
            seen.add(cid)
            c = clusters[cid]
            origin = by_id[c.origin_record_id]
            # Injection is decided by code from the article text; the model may only add to it.
            inj = injection_suspected(origin.title + " " + origin.text) or bool(lab.get("injection_suspected"))
            out.append(EventCandidate(
                cluster_id=cid,
                headline=c.title,
                entities=list(lab.get("entities", [])),
                event_time=c.earliest_available_at,
                fact_or_rumour=lab.get("fact_or_rumour", "unclear"),
                novelty=lab.get("novelty", "new"),
                independent_sources=c.independent_sources,
                missing_corroboration=list(lab.get("missing_corroboration", [])),
                implication=lab.get("implication", "unclear"),
                catalyst=lab.get("catalyst"),
                injection_suspected=inj,
                note=str(lab.get("note", "")),
                evidence_refs=[EvidenceRef(kind="cluster", id=cid),
                               EvidenceRef(kind="evidence", id=origin.id, content_hash=origin.content_hash)],
            ))
        missing = set(clusters) - seen
        if missing:
            raise ModelOutputInvalid(f"no label for clusters {sorted(missing)}")
        return out
