"""Coordinator: the only code path from a question to a report.

Code selects jobs from the validated request, runs specialists sequentially
(concurrency 1), assembles the briefing and appends an immutable report. The
model never chooses jobs. There is no order, delivery or capital code here.
"""
from __future__ import annotations

import threading
import uuid
from datetime import datetime, timedelta
from pathlib import Path

from options_research.agents.evidence_verifier import Claim, EvidenceVerifier
from options_research.agents.news_collector import NewsCollector
from options_research.briefing import assemble
from options_research.config import PROJECT_ROOT, Config, load_config
from options_research.envelope import Envelope, Status
from options_research.evidence import EvidenceStore
from options_research.findings import EvidenceRef, EventCandidate, MarketSnapshotRef
from options_research.intake import (
    Analysis,
    Clarification,
    FixtureInstrumentLookup,
    IntakeValidator,
    ResearchRequest,
)
from options_research.ledger import ReportLedger, ReportRecord
from options_research.market import MarketDataSpecialist, MarketDataUnavailable, MarketSnapshot
from options_research.model import Budget, MockModelClient, ModelClient, UsageLedger
from options_research.policy.quarantine import Quarantine, QuarantinePolicy
from options_research.timeutil import utc_now

MARKET_AGENT_ID = "market_data_specialist"


class Coordinator:
    def __init__(self, config: Config, model: ModelClient, store: EvidenceStore,
                 market: MarketDataSpecialist, lookup: FixtureInstrumentLookup, reports: ReportLedger):
        self._cfg = config
        self._model = model
        self._store = store
        self._market = market
        self._reports = reports
        self._intake = IntakeValidator(lookup, config.news_window_days, inline_only=config.inline_only)
        self._lock = threading.Lock()  # bounded concurrency: one request at a time

    # ---- construction ------------------------------------------------------
    @classmethod
    def offline(cls, config: Config | None = None, fixtures_dir: Path | None = None,
                data_dir: Path | None = None) -> "Coordinator":
        cfg = config or load_config()
        fx = fixtures_dir or PROJECT_ROOT / "fixtures"
        data = data_dir or cfg.ledger_dir
        usage = UsageLedger(data / "usage.jsonl")
        model = MockModelClient.from_dir(usage, fx / "model")
        store = EvidenceStore(data / "evidence.jsonl")
        for p in sorted((fx / "news").glob("*.json")):
            store = EvidenceStore.load_fixture(data / "evidence.jsonl", p)
        return cls(cfg, model, store, MarketDataSpecialist(fx / "market"),
                   FixtureInstrumentLookup(fx / "instruments.json"), ReportLedger(data / "reports.jsonl"))

    # ---- entry point -------------------------------------------------------
    def handle(self, question: str, *, requester: str = "local", as_of: datetime | None = None,
               window: tuple[datetime, datetime] | None = None, window_days: int | None = None,
               parent_report_id: str | None = None, analyses: list[Analysis] | None = None,
               horizon_days: int | None = None, request_time: datetime | None = None,
               ) -> ReportRecord | Clarification:
        with self._lock:
            request_time = request_time or utc_now()
            if window is None and window_days is not None:
                end = as_of or request_time
                window = (end - timedelta(days=window_days), end)
            parent_request_id = None
            if parent_report_id:
                parent_request_id = self._reports.get(parent_report_id).request.request_id
            req = self._intake.build(question=question, requester=requester, request_time=request_time,
                                     as_of=as_of, window=window, requested_analyses=analyses,
                                     horizon_days=horizon_days, parent_request_id=parent_request_id)
            if isinstance(req, Clarification):
                return req
            return self._run(req, parent_report_id)

    # ---- pipeline ----------------------------------------------------------
    def _run(self, req: ResearchRequest, parent_report_id: str | None) -> ReportRecord:
        run_id = uuid.uuid4().hex
        budget = Budget(self._cfg.model.max_calls_per_request, self._cfg.model.max_tokens_per_request)
        envelopes: list[Envelope] = []
        want = set(req.requested_analyses)

        market_env: Envelope | None = None
        snapshot: MarketSnapshot | None = None
        metrics: dict[str, float | None] | None = None
        if Analysis.MARKET_REACTION in want or Analysis.EVIDENCE_VERIFICATION in want:
            market_env, snapshot = self._market_job(req, run_id)
            envelopes.append(market_env)
            if snapshot is not None:
                metrics = {k: r.result for k, r in snapshot.receipts.items()}

        news_env: Envelope | None = None
        collector = NewsCollector(self._model, self._store)
        if Analysis.NEWS_SUMMARY in want or Analysis.EVIDENCE_VERIFICATION in want:
            news_env = collector.run(req, run_id, budget, self._cfg.project_id)
            envelopes.append(news_env)

        verify_env: Envelope | None = None
        quarantine: Quarantine | None = None
        if Analysis.EVIDENCE_VERIFICATION in want and news_env is not None and news_env.status is Status.OK:
            claims = [Claim(claim_id=e.cluster_id, text=e.headline, cluster_ids=[e.cluster_id],
                            material=(e.fact_or_rumour != "rumour"))
                      for e in news_env.findings if isinstance(e, EventCandidate)]
            query = collector.query(req)
            verify_env = EvidenceVerifier(self._model, self._store).run(
                claims, query, snapshot, req.as_of, run_id, budget, self._cfg.project_id)
            envelopes.append(verify_env)
            quarantine = QuarantinePolicy().apply(
                [f for f in verify_env.findings if f.type == "claim_verification"])

        sections = assemble(req, run_id, news_env, market_env, verify_env, quarantine, metrics)
        status = "OK" if all(s.status == "OK" or s.section == "invalidation_conditions" for s in sections) else "PARTIAL"
        input_hashes: dict[str, str] = {}
        for e in envelopes:
            for k, v in e.input_hashes.items():
                input_hashes[f"{e.agent_id}.{k}"] = v
        record = ReportRecord(
            report_id="rep:" + uuid.uuid4().hex[:16], run_id=run_id, created_at=utc_now(),
            project_id=self._cfg.project_id, request=req, parent_report_id=parent_report_id,
            as_of=req.as_of, input_hashes=input_hashes, envelopes=envelopes, sections=sections,
            status=status, disclosures=list(req.disclosures),
        )
        return self._reports.append(record)

    def _market_job(self, req: ResearchRequest, run_id: str) -> tuple[Envelope, MarketSnapshot | None]:
        base = dict(project_id=self._cfg.project_id, run_id=run_id, agent_id=MARKET_AGENT_ID, model_id="none",
                    prompt_version="none", as_of=req.as_of, valid_until=req.as_of + timedelta(hours=6))
        try:
            snap = self._market.snapshot(req.instrument, req.as_of, "1h", window_start=req.window.start)
        except MarketDataUnavailable as exc:
            return Envelope(**base, status=Status.BLOCK, missing_inputs=[str(exc)]), None
        ref = EvidenceRef(kind="snapshot", id=snap.snapshot_id)
        finding = MarketSnapshotRef(
            snapshot_id=snap.snapshot_id, instrument=snap.instrument, as_of=snap.as_of,
            last_complete_bar=snap.last_complete_bar, quality=snap.quality,
            quality_failures=[f"{f.kind}: {f.detail}" + (f" at {f.at.isoformat()}" if f.at else "") for f in snap.quality_failures],
            evidence_refs=[ref] + [EvidenceRef(kind="receipt", id=r.receipt_id, content_hash=r.inputs_hash)
                                   for r in snap.receipts.values()],
        )
        env = Envelope(**base, status=Status.OK, input_hashes={"market_snapshot": snap.snapshot_id},
                       evidence_refs=finding.evidence_refs, findings=[finding],
                       rationale=f"{snap.bars_used} complete bars from {snap.source}")
        return env, snap
