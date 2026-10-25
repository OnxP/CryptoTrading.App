"""End-to-end M2 options scenarios over cassettes (CLAUDE_SETUP "Required scenarios", options items).

Each test names the spec scenario it covers. Every assertion is on code output
(typed findings, sections, CLI text), never on mock narrative text alone.
Scenarios M2 cannot honestly cover are listed in docs/planning/OPTIONS_M2_TEST_REPORT.md.
"""
import re
from datetime import date, datetime, timedelta, timezone

import pytest

from options_research.agents.pricing_specialist import PricingConfig, PricingSpecialist
from options_research.agents.risk_reviewer import RiskReviewer
from options_research.briefing import advisory_section, render
from options_research.cli import main
from options_research.config import PROJECT_ROOT, load_config
from options_research.coordinator import Coordinator
from options_research.envelope import Status
from options_research.findings import RiskReview, RiskVerdict, StructureComparison
from options_research.intake import Analysis, FixtureInstrumentLookup, IntakeValidator
from options_research.model import Budget, MockModelClient, UsageLedger

FIXTURES = PROJECT_ROOT / "fixtures"
AS_OF = datetime(2026, 10, 15, 20, 0, tzinfo=timezone.utc)
Q = "Compare defined-risk options on FIXT for the next month"
UNUSABLE_FIXTURE_LEGS = ("FIXT261120C00142500", "FIXT261120P00077500", "FIXT261120C00082500")  # crossed, zero bid, stale


@pytest.fixture
def coord(tmp_path):
    return Coordinator.offline(fixtures_dir=FIXTURES, data_dir=tmp_path / "data")


@pytest.fixture
def token(monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "replay-only")


def run(coord, horizon=30, question=Q):
    return coord.handle(question, as_of=AS_OF, request_time=AS_OF, window_days=10, horizon_days=horizon,
                        analyses=[Analysis.OPTIONS_STRUCTURES])


def advisory(rec):
    s = rec.sections[-1]
    assert s.section == "trade_ideas_advisory"
    return s


# O1: options advice without orders ------------------------------------------------------------
def test_o1_options_advice_without_orders(coord, token):
    rec = run(coord)
    adv = advisory(rec)
    assert adv.status == "PARTIAL" and adv.text.startswith("ADVISORY")
    pricing = next(e for e in rec.envelopes if e.agent_id == "pricing_specialist")
    structures = [f for f in pricing.findings if isinstance(f, StructureComparison) and f.alternative == "structure"]
    assert len(structures) == 6 and all(len(s.legs) in (1, 2) and all(l.qty == 1 for l in s.legs) for s in structures)
    text = rec.model_dump_json()
    for leg in UNUSABLE_FIXTURE_LEGS:  # missing/unusable option quotes are never priced or shown
        assert leg not in text
    low = text.lower()
    for k in ('"order', '"quantity', '"fill', '"approved', '"execute', '"limit_price', "probability", "expected_value"):
        assert k not in low
    assert rec.status == "PARTIAL" and rec.valid_until is not None


# O2: missing chain -> thesis-only ----------------------------------------------------------------
def test_o2_missing_chain_is_thesis_only(coord, monkeypatch):
    monkeypatch.delenv("TRADIER_SANDBOX_TOKEN", raising=False)
    rec = run(coord)
    adv = advisory(rec)
    assert "THESIS-ONLY" in adv.text and adv.status == "PARTIAL"
    assert "FIXT261120" not in rec.model_dump_json() and rec.valid_until is None
    risk = next(e for e in rec.envelopes if e.agent_id == "risk_reviewer")
    assert risk.status is Status.REVIEW and risk.findings[0].comparison_id == "none"


# O3: expired quotes -> BLOCK, and STALE on print -----------------------------------------------------
def test_o3_expired_quotes_block_and_print_stale(coord, token, tmp_path, capsys):
    rec = run(coord)
    pricing = next(e for e in rec.envelopes if e.agent_id == "pricing_specialist")
    later = AS_OF + timedelta(minutes=30)  # beyond max_quote_age_s = 900
    req = rec.request
    risk = RiskReviewer(MockModelClient(UsageLedger(tmp_path / "u.jsonl"), [])).run(
        pricing, None, None, req, "r2", Budget(8, 60_000), verification_requested=False, now=later)
    assert risk.status is Status.BLOCK
    assert all(f.verdict is RiskVerdict.BLOCK and any("quote stale" in r for r in f.reasons) for f in risk.findings)
    rc = main(["show", rec.report_id, "--data-dir", str(tmp_path / "data"), "--now", later.isoformat()])
    out = capsys.readouterr().out
    assert rc == 0 and out.startswith("STALE:") and "not actionable" in out


# O4: earnings inside horizon -> REVIEW with date -----------------------------------------------------
def test_o4_earnings_inside_horizon_is_review_with_date(coord, token):
    rec = run(coord)
    risk = next(e for e in rec.envelopes if e.agent_id == "risk_reviewer")
    reviews = [f for f in risk.findings if isinstance(f, RiskReview)]
    assert reviews and all(f.verdict is RiskVerdict.REVIEW for f in reviews)
    assert all(any("2026-10-28" in r for r in f.reasons) for f in reviews)
    assert "28 October" in next(s for s in rec.sections if s.section == "catalysts").text


# O5: stale report advice is not labelled actionable ----------------------------------------------------
def test_o5_stale_report_advice_not_actionable(coord, token, tmp_path, capsys):
    rec = run(coord)
    assert rec.valid_until == datetime(2026, 10, 15, 20, 14, 30, tzinfo=timezone.utc)
    main(["show", rec.report_id, "--data-dir", str(tmp_path / "data"), "--now", "2026-10-15T20:14:00Z"])
    fresh = capsys.readouterr().out
    main(["show", rec.report_id, "--data-dir", str(tmp_path / "data"), "--now", "2026-10-15T20:15:00Z"])
    stale = capsys.readouterr().out
    assert "STALE" not in fresh and stale.startswith("STALE:") and "[STALE, not actionable]" in stale
    # code never labels advice actionable: the only code-produced wording is the "not a recommendation" label
    # and, once past valid_until, the STALE / not actionable banner and tail tag
    assert "not a recommendation to trade" in fresh and "[STALE" not in fresh
    assert re.search(r"valid_until: 2026-10-15T20:14:30\+00:00", stale)


# O6: expiry before horizon -> BLOCK (reviewer) / thesis-only when no later chain exists (coordinator) -------
def test_o6_expiry_before_horizon(coord, token, tmp_path):
    rec30 = run(coord, horizon=30)
    pricing = next(e for e in rec30.envelopes if e.agent_id == "pricing_specialist")
    long_req = run(coord, horizon=45).request
    risk = RiskReviewer(MockModelClient(UsageLedger(tmp_path / "u.jsonl"), [])).run(
        pricing, None, None, long_req, "r3", Budget(8, 60_000), verification_requested=False)
    assert risk.status is Status.BLOCK
    assert all(any("before the horizon end 2026-11-29" in r for r in f.reasons) for f in risk.findings)
    rec45 = run(coord, horizon=45)  # nearest monthly at/after 29 Nov is 18 Dec; no chain cassette -> no fabrication
    assert "THESIS-ONLY" in advisory(rec45).text and "FIXT261120" not in rec45.model_dump_json()


# O7: expensive IV: correct direction still loses, visible in the grid -------------------------------------
def test_o7_expensive_iv_loss_visible_in_rendered_grid(tmp_path):
    from tests.test_pricing_specialist import synthetic_chain

    chain = synthetic_chain(100.0, 1.20, AS_OF, date(2026, 11, 20))
    iv = IntakeValidator(FixtureInstrumentLookup(FIXTURES / "instruments.json"), 7)
    req = iv.build(question=Q, requester="t", request_time=AS_OF, as_of=AS_OF,
                   requested_analyses=[Analysis.OPTIONS_STRUCTURES], horizon_days=30)
    model = MockModelClient(UsageLedger(tmp_path / "u.jsonl"), [])
    pricing = PricingSpecialist(model, PricingConfig(load_config().raw)).run(req, chain, None, None, "r1", Budget(8, 60_000),
                                                                             direction="bullish")
    risk = RiskReviewer(model).run(pricing, chain, None, req, "r1", Budget(8, 60_000), verification_requested=False)
    text = render([advisory_section(req, pricing, risk)])
    lc = next(f for f in pricing.findings if isinstance(f, StructureComparison) and f.kind == "long_call")
    up5 = next(r for r in lc.scenario_rows if r.spot_pct == 5)
    assert up5.pnl_expiry < 0 and f"{up5.pnl_expiry:+.2f}" in text
    assert "correct-direction scenarios that still lose" in text and "Expensive volatility" in text


# O8: report/position identity mismatch -> NOT IN M2 (no positions exist) ----------------------------------
def test_o8_no_position_concept_exists_yet():
    src = PROJECT_ROOT / "src" / "options_research"
    names = {p.stem for p in src.rglob("*.py")}
    assert not ({"position", "positions", "portfolio", "orders", "broker"} & names)
    offenders = [p.name for p in src.rglob("*.py")
                 if re.search(r"^\s*(from|import)\s+\S*(position|order|broker|execut)", p.read_text(encoding="utf-8"), re.M)]
    assert offenders == []
