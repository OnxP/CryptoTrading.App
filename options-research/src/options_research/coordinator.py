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
from options_research.agents.pricing_specialist import PricingConfig, PricingSpecialist, direction_hint
from options_research.agents.risk_reviewer import RiskReviewer
from options_research.briefing import advisory_section, assemble
from options_research.config import PROJECT_ROOT, Config, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.tradier import TradierClient
from options_research.envelope import Envelope, Status
from options_research.evidence import EvidenceStore
from options_research.findings import EvidenceRef, EventCandidate, MarketSnapshotRef, VolAssessment
from options_research.intake import (
    Analysis,
    Clarification,
    FixtureInstrumentLookup,
    IntakeValidator,
    ResearchRequest,
)
from options_research.ledger import ReportLedger, ReportRecord
from options_research.market import MarketDataSpecialist, MarketDataUnavailable, MarketSnapshot
from options_research.market.sources import BarSource, TradierBarSource
from options_research.model import Budget, MockModelClient, ModelClient, UsageLedger
from options_research.options import ChainUnavailable, OptionChainSnapshot, TradierChainSource
from options_research.options.chain import nearest_expiration_at_or_after
from options_research.options.vol import HAR_NOT_FITTED, implied_summary, realised_summary, vol_premium_points
from options_research.policy.quarantine import Quarantine, QuarantinePolicy
from options_research.timeutil import utc_now

MARKET_AGENT_ID = "market_data_specialist"
VOL_AGENT_ID = "vol_specialist"
VOL_LOOKBACK_DAYS = 30


class Coordinator:
    def __init__(self, config: Config, model: ModelClient, store: EvidenceStore,
                 market: MarketDataSpecialist, lookup: FixtureInstrumentLookup, reports: ReportLedger,
                 chain: TradierChainSource | None = None, daily_bars: BarSource | None = None):
        self._cfg = config
        self._model = model
        self._store = store
        self._market = market
        self._reports = reports
        self._chain = chain
        self._daily = daily_bars
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
        chain = None
        daily = None
        cassettes = fx / "cassettes" / "tradier"
        if cassettes.exists():  # replay only; the token env var still gates every call (no token -> thesis-only)
            t = cfg.raw.get("tradier", {})
            client = TradierClient(ReplayHttpClient(cassettes), cfg)
            chain = TradierChainSource(client, float(t.get("max_quote_age_s", 900)), float(t.get("wide_spread_ratio", 0.25)))
            daily = TradierBarSource(client)
        return cls(cfg, model, store, MarketDataSpecialist(fx / "market"),
                   FixtureInstrumentLookup(fx / "instruments.json"), ReportLedger(data / "reports.jsonl"), chain=chain,
                   daily_bars=daily)

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
        options = Analysis.OPTIONS_STRUCTURES in want
        if options:  # options advice always sits on top of the full factual pipeline
            want |= {Analysis.MARKET_REACTION, Analysis.NEWS_SUMMARY, Analysis.EVIDENCE_VERIFICATION}

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
        valid_until: datetime | None = None
        if options:
            chain, chain_reason = self._chain_job(req)
            pricing_env = PricingSpecialist(self._model, PricingConfig(self._cfg.raw)).run(
                req, chain, chain_reason, snapshot, run_id, budget, self._cfg.project_id,
                direction=direction_hint(req.original_question))
            envelopes.append(pricing_env)
            vol_env, vol = self._vol_job(req, chain, run_id)
            envelopes.append(vol_env)
            events = [f for f in (news_env.findings if news_env else []) if isinstance(f, EventCandidate)]
            risk_cfg = self._cfg.raw.get("risk", {})
            risk_env = RiskReviewer(
                self._model, max_chain_age_s=float(risk_cfg.get("max_chain_age_s", 3600)),
                max_quote_age_s=float(self._cfg.raw.get("tradier", {}).get("max_quote_age_s", 900)),
                account_limits_configured=bool(risk_cfg.get("account_limits_configured", False)),
                iv_premium_review_points=float(risk_cfg.get("iv_premium_review_points", 10)),
            ).run(pricing_env, chain, verify_env, req, run_id, budget, self._cfg.project_id, events=events,
                  quarantine=quarantine, verification_requested=True, vol=vol)
            envelopes.append(risk_env)
            # the advisory section is appended AFTER every factual section and only with a risk-review envelope
            sections.append(advisory_section(req, pricing_env, risk_env, vol))
            if pricing_env.status is Status.OK:
                valid_until = pricing_env.valid_until  # earliest leg quote expiry (bounded) from the specialist
        status = "OK" if all(s.status == "OK" or s.section == "invalidation_conditions" for s in sections) else "PARTIAL"
        if options:
            status = "PARTIAL"  # no sizing while account limits are unknown
        input_hashes: dict[str, str] = {}
        for e in envelopes:
            for k, v in e.input_hashes.items():
                input_hashes[f"{e.agent_id}.{k}"] = v
        record = ReportRecord(
            report_id="rep:" + uuid.uuid4().hex[:16], run_id=run_id, created_at=utc_now(),
            project_id=self._cfg.project_id, request=req, parent_report_id=parent_report_id,
            as_of=req.as_of, input_hashes=input_hashes, envelopes=envelopes, sections=sections,
            status=status, disclosures=list(req.disclosures), valid_until=valid_until,
        )
        return self._reports.append(record)

    def _vol_job(self, req: ResearchRequest, chain: OptionChainSnapshot | None, run_id: str
                 ) -> tuple[Envelope, VolAssessment]:
        """Deterministic vol context: realised estimators from daily bars, implied side from the chain. Model: none."""
        as_of = req.as_of
        base = dict(project_id=self._cfg.project_id, run_id=run_id, agent_id=VOL_AGENT_ID, model_id="none",
                    prompt_version="none", as_of=as_of, valid_until=as_of + timedelta(hours=6))
        missing: list[str] = []
        realised = None
        if self._daily is None:
            missing.append("daily bars: no source configured")
        else:
            try:
                batch = self._daily.load(req.instrument, "1d", as_of - timedelta(days=VOL_LOOKBACK_DAYS), as_of)
                realised = realised_summary([b for b in batch.bars if b.close_time <= as_of])
                if realised.unavailable_reason:
                    missing.append(f"daily bars: {realised.unavailable_reason}")
            except (MarketDataUnavailable, LookupError) as exc:
                missing.append(f"daily bars: {exc}")
        implied = None
        if chain is None:
            missing.append("implied side: no chain snapshot")
        else:
            spot = chain.underlying_reference.result
            implied = implied_summary(chain, spot, as_of, float(self._cfg.raw.get("pricing", {}).get("risk_free_rate", 0.04)),
                                      float(self._cfg.raw.get("pricing", {}).get("otm_pct", 0.05)))
        rids = (realised.receipt_ids if realised else []) + (implied.receipt_ids if implied else [])
        refs = [EvidenceRef(kind="receipt", id=r) for r in rids]
        if chain is not None:
            refs.append(EvidenceRef(kind="snapshot", id=chain.snapshot_id))
        finding = VolAssessment(
            instrument=req.instrument.canonical, as_of=as_of, bars_used=realised.bars_used if realised else 0,
            realised_close_to_close=realised.close_to_close if realised else None,
            realised_parkinson=realised.parkinson if realised else None,
            realised_yang_zhang=realised.yang_zhang if realised else None,
            realised_ewma=realised.ewma if realised else None,
            forecast=realised.forecast if realised else None,
            forecast_low=realised.forecast_low if realised else None,
            forecast_high=realised.forecast_high if realised else None,
            forecast_method=HAR_NOT_FITTED,
            realised_unavailable_reason=(realised.unavailable_reason if realised else "; ".join(m for m in missing if m.startswith("daily")) or None),
            expiry=implied.expiry if implied else None,
            atm_iv=implied.atm_iv if implied else None, atm_legs=implied.atm_legs if implied else [],
            skew_proxy=implied.skew_proxy if implied else None, skew_legs=implied.skew_legs if implied else [],
            iv_rank=None, iv_rank_reason=implied.iv_rank_reason if implied else "unavailable: no IV history store",
            premium_points=vol_premium_points(implied, realised) if implied and realised else None,
            implied_unavailable_reason=("; ".join(implied.notes) if implied and implied.atm_iv is None else None)
                                       if chain is not None else "no chain snapshot",
            receipt_ids=rids,
            evidence_refs=refs or [EvidenceRef(kind="report", id="this-report")],
            note="volatility context is descriptive only: no odds, no direction, no recommendation",
        )
        status = Status.OK if (realised and realised.forecast is not None) or (implied and implied.atm_iv is not None) else Status.ABSTAIN
        env = Envelope(**base, status=status, evidence_refs=refs, findings=[finding], missing_inputs=missing,
                       input_hashes={"chain_snapshot": chain.snapshot_id} if chain else {},
                       rationale=f"realised bars {finding.bars_used}; forecast {finding.forecast}; atm_iv {finding.atm_iv}; "
                                 f"premium_points {finding.premium_points}")
        return env, finding

    def _chain_job(self, req: ResearchRequest) -> tuple[OptionChainSnapshot | None, str | None]:
        """Chain snapshot for the nearest monthly expiration at or beyond the horizon end; never fabricated."""
        if self._chain is None:
            return None, "no chain source configured"
        horizon_end = req.as_of + timedelta(days=req.horizon_days or 0)
        try:
            exps = self._chain.expirations(req.instrument)
            target = nearest_expiration_at_or_after(exps, horizon_end.date())
            if target is None:
                return None, f"no listed expiration at or beyond the horizon end {horizon_end.date().isoformat()}"
            return self._chain.snapshot(req.instrument, target, req.as_of), None
        except ChainUnavailable as exc:
            return None, str(exc)
        except LookupError as exc:  # replay miss (no cassette for that expiration): unavailable, never fabricated
            return None, f"chain not available offline: {exc}"

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
