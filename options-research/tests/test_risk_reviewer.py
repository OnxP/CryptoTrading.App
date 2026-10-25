from datetime import date, datetime, timedelta, timezone

import pytest

from options_research.agents.pricing_specialist import PricingConfig, PricingSpecialist
from options_research.agents.risk_reviewer import RiskReviewer, parse_event_date
from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.tradier import TradierClient
from options_research.envelope import Envelope, Status
from options_research.findings import EventCandidate, EvidenceRef, RiskReview, RiskVerdict, StructureComparison
from options_research.intake import Analysis, FixtureInstrumentLookup, IntakeValidator
from options_research.model import Budget, MockModelClient, UsageLedger
from options_research.options import TradierChainSource
from options_research.policy.quarantine import Quarantine

FIXTURES = PROJECT_ROOT / "fixtures"
AS_OF = datetime(2026, 10, 15, 20, 0, tzinfo=timezone.utc)
EXP = date(2026, 11, 20)
Q = "Compare defined-risk options on FIXT for the next month"


@pytest.fixture
def chain(monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    cfg = load_config()
    src = TradierChainSource(TradierClient(ReplayHttpClient(FIXTURES / "cassettes" / "tradier"), cfg), 900, 0.25)
    fixt = FixtureInstrumentLookup(FIXTURES / "instruments.json").by_symbol("FIXT")[0]
    return src.snapshot(fixt, EXP, AS_OF)


def request(horizon=30):
    iv = IntakeValidator(FixtureInstrumentLookup(FIXTURES / "instruments.json"), 7)
    return iv.build(question=Q, requester="t", request_time=AS_OF, as_of=AS_OF,
                    requested_analyses=[Analysis.OPTIONS_STRUCTURES], horizon_days=horizon)


@pytest.fixture
def pricing(chain, tmp_path):
    model = MockModelClient.from_dir(UsageLedger(tmp_path / "u.jsonl"), FIXTURES / "model")
    return PricingSpecialist(model, PricingConfig(load_config().raw)).run(request(), chain, None, None, "r1", Budget(8, 60_000))


def reviewer(tmp_path, with_fixtures=True, **kw):
    usage = UsageLedger(tmp_path / "u2.jsonl")
    model = MockModelClient.from_dir(usage, FIXTURES / "model") if with_fixtures else MockModelClient(usage, [])
    return RiskReviewer(model, **kw)


def verify_ok():
    return Envelope(project_id="options-research", run_id="r1", agent_id="evidence_verifier", model_id="mock",
                    prompt_version="evidence_verifier/v1", as_of=AS_OF, valid_until=AS_OF + timedelta(hours=6), status=Status.OK,
                    evidence_refs=[EvidenceRef(kind="cluster", id="cluster:n-001")])


def event(cluster, implication, catalyst=None):
    return EventCandidate(cluster_id=cluster, headline="h", independent_sources=1, implication=implication, catalyst=catalyst,
                          evidence_refs=[EvidenceRef(kind="cluster", id=cluster)])


def reviews(env):
    return {f.comparison_id: f for f in env.findings if isinstance(f, RiskReview)}


def test_all_clean_is_review_for_unconfigured_limits_never_pass(pricing, chain, tmp_path):
    env = reviewer(tmp_path).run(pricing, chain, verify_ok(), request(), "r1", Budget(8, 60_000))
    assert env.status is Status.REVIEW
    rv = reviews(env)
    structures = [f for f in pricing.findings if isinstance(f, StructureComparison) and f.alternative == "structure"]
    assert set(rv) == {s.comparison_id for s in structures}
    for f in rv.values():
        assert f.verdict is RiskVerdict.REVIEW and f.reasons == ["account limits not configured"]
        assert f.expires_at == AS_OF + timedelta(hours=1)  # min(max chain age 3600s, 6h)
        assert f.note  # narrative from the mock
    assert "PASS would not be permission" in env.rationale


def test_stale_leg_blocks_naming_the_symbol(pricing, chain, tmp_path):
    later = AS_OF + timedelta(minutes=20)  # quotes are 19:59:30; max quote age 900s
    env = reviewer(tmp_path, with_fixtures=False).run(pricing, chain, verify_ok(), request(), "r1", Budget(8, 60_000), now=later)
    assert env.status is Status.BLOCK
    for f in reviews(env).values():
        assert f.verdict is RiskVerdict.BLOCK
        assert any("quote stale" in r and "FIXT261120" in r for r in f.reasons)
    assert any("narrative unavailable" in m for m in env.missing_inputs)  # verdicts stand without the model


def test_expiry_before_horizon_end_blocks(pricing, chain, tmp_path):
    env = reviewer(tmp_path, with_fixtures=False).run(pricing, chain, verify_ok(), request(horizon=45), "r1", Budget(8, 60_000))
    assert env.status is Status.BLOCK
    assert all(any("before the horizon end 2026-11-29" in r for r in f.reasons) for f in reviews(env).values())


def test_chain_older_than_max_age_blocks(pricing, chain, tmp_path):
    env = reviewer(tmp_path, with_fixtures=False, max_chain_age_s=10, max_quote_age_s=10 ** 6).run(
        pricing, chain, verify_ok(), request(), "r1", Budget(8, 60_000))
    assert env.status is Status.BLOCK
    assert all(any("chain older than max age" in r for r in f.reasons) for f in reviews(env).values())
    assert env.valid_until == AS_OF + timedelta(seconds=10)


def test_earnings_inside_horizon_is_review_with_date(pricing, chain, tmp_path):
    ev = [event("cluster:n-001", "bearish", catalyst="full results on 28 October")]
    env = reviewer(tmp_path).run(pricing, chain, verify_ok(), request(), "r1", Budget(8, 60_000), events=ev)
    assert env.status is Status.REVIEW
    for f in reviews(env).values():
        assert f.verdict is RiskVerdict.REVIEW
        assert any("2026-10-28" in r and "inside horizon" in r for r in f.reasons)
    assert parse_event_date("results on 28 October", AS_OF) == date(2026, 10, 28)
    assert parse_event_date("Investor day January 5", AS_OF) == date(2027, 1, 5)  # rolled forward past as_of
    assert parse_event_date("on 2026-11-02", AS_OF) == date(2026, 11, 2)
    assert parse_event_date("no date here", AS_OF) is None


def test_quarantined_evidence_behind_direction_blocks_only_that_direction(pricing, chain, tmp_path):
    q = Quarantine(quarantined_claim_ids=["k"], quarantined_evidence_ids=["cluster:n-002"], blocks_new_entry=True,
                   reasons=["k: CONTRADICTED (HIGH)"])
    ev = [event("cluster:n-002", "bearish")]
    env = reviewer(tmp_path, with_fixtures=False).run(pricing, chain, verify_ok(), request(), "r1", Budget(8, 60_000),
                                                      events=ev, quarantine=q)
    by_kind = {}
    for s in pricing.findings:
        if isinstance(s, StructureComparison) and s.alternative == "structure":
            by_kind[s.kind] = reviews(env)[s.comparison_id]
    assert by_kind["long_put"].verdict is RiskVerdict.BLOCK and any("quarantined evidence behind the bearish" in r for r in by_kind["long_put"].reasons)
    assert by_kind["long_call"].verdict is RiskVerdict.REVIEW and any("other direction" in r for r in by_kind["long_call"].reasons)


def test_verification_missing_or_error_is_review(pricing, chain, tmp_path):
    env = reviewer(tmp_path, with_fixtures=False).run(pricing, chain, None, request(), "r1", Budget(8, 60_000))
    assert all(any("verification unavailable" in r for r in f.reasons) for f in reviews(env).values())
    bad = verify_ok().model_copy(update={"status": Status.ERROR, "evidence_refs": []})
    env2 = reviewer(tmp_path, with_fixtures=False).run(pricing, chain, bad, request(), "r1", Budget(8, 60_000))
    assert all(any("envelope ERROR" in r for r in f.reasons) for f in reviews(env2).values())


def test_thesis_only_pricing_gets_one_review_finding(tmp_path):
    abstain = Envelope(project_id="options-research", run_id="r1", agent_id="pricing_specialist", model_id="mock",
                       prompt_version="pricing_specialist/v1", as_of=AS_OF, valid_until=AS_OF + timedelta(hours=6),
                       status=Status.ABSTAIN, missing_inputs=["option chain unavailable: token env var not set"],
                       rationale="thesis-only")
    env = reviewer(tmp_path).run(abstain, None, verify_ok(), request(), "r1", Budget(8, 60_000))
    assert env.status is Status.REVIEW and len(env.findings) == 1
    f = env.findings[0]
    assert f.comparison_id == "none" and f.verdict is RiskVerdict.REVIEW
    assert "no priced structure" in f.reasons[0] and "account limits not configured" in f.reasons


def test_reviewer_never_changes_pricing_findings(pricing, chain, tmp_path):
    before = pricing.content_hash()
    reviewer(tmp_path).run(pricing, chain, verify_ok(), request(), "r1", Budget(8, 60_000))
    assert pricing.content_hash() == before
