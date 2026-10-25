from datetime import datetime, timedelta, timezone

import pytest

from options_research.cli import main
from options_research.config import PROJECT_ROOT
from options_research.coordinator import Coordinator
from options_research.intake import Analysis

FIXTURES = PROJECT_ROOT / "fixtures"
AS_OF = datetime(2026, 10, 15, 20, 0, tzinfo=timezone.utc)
Q = "Compare defined-risk options on FIXT for the next month"
M1_AS_OF = datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)
M1_Q = "Analyse FIXT news from the last seven days"
# content hash of the M1 fixture run recorded before any M2 coordinator change (2026-10-25)
M1_BASELINE_HASH = "1675134238357bbade4e3f0a2763923ed71e3e30807394eabeb9595cd4fa58c6"


@pytest.fixture
def coord(tmp_path):
    return Coordinator.offline(fixtures_dir=FIXTURES, data_dir=tmp_path / "data")


def run_options(coord, **kw):
    return coord.handle(Q, as_of=AS_OF, request_time=AS_OF, window_days=10, horizon_days=30,
                        analyses=[Analysis.OPTIONS_STRUCTURES], **kw)


def test_full_options_run_adds_labelled_advisory_section_last(coord, monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    rec = run_options(coord)
    assert rec.status == "PARTIAL"
    assert rec.valid_until == datetime(2026, 10, 15, 20, 14, 30, tzinfo=timezone.utc)
    agents = [e.agent_id for e in rec.envelopes]
    assert agents[-2:] == ["pricing_specialist", "risk_reviewer"]
    assert {"market_data_specialist", "news_collector", "evidence_verifier"} <= set(agents)
    adv = rec.sections[-1]
    assert adv.section == "trade_ideas_advisory" and adv.status == "PARTIAL"
    assert adv.text.startswith("ADVISORY, not a recommendation to trade")
    assert "quantities not calculated" in adv.text and "account limits not configured" in adv.blocked_reason
    assert [s.section for s in rec.sections].index("trade_ideas_advisory") == len(rec.sections) - 1
    # legs, quote times, net debit/credit, bounds, scenario table, liquidity, caveats, verdict with expiry
    assert "FIXT261120C00110000" in adv.text and "quote 2026-10-15T19:59:30+00:00" in adv.text
    assert "net debit" in adv.text and "net credit" in adv.text and "contractual bounds" in adv.text
    assert "scenario P&L per set" in adv.text and "100 shares" in adv.text
    assert "risk review: REVIEW (expires 2026-10-15T21:00:00+00:00)" in adv.text
    assert "scheduled event inside horizon on 2026-10-28" in adv.text  # catalyst from the verified news
    assert "comparison row: no_trade" in adv.text
    # factual sections are intact and precede the advisory one
    assert "28 October" in next(s for s in rec.sections if s.section == "catalysts").text
    dumped = rec.model_dump_json().lower()
    for k in ('"order', '"quantity', '"approved', '"execute', '"recipient', "probability"):
        assert k not in dumped


def test_chain_unavailable_gives_thesis_only_section_and_no_legs(coord, monkeypatch):
    monkeypatch.delenv("TRADIER_SANDBOX_TOKEN", raising=False)
    rec = run_options(coord)
    assert rec.status == "PARTIAL" and rec.valid_until is None
    adv = rec.sections[-1]
    assert adv.section == "trade_ideas_advisory" and adv.status == "PARTIAL"
    assert "THESIS-ONLY" in adv.text and "token env var" in adv.text
    assert "risk review: REVIEW" in adv.text
    text = rec.model_dump_json()
    assert "occ_symbol" not in text and "FIXT261120" not in text
    pricing = next(e for e in rec.envelopes if e.agent_id == "pricing_specialist")
    assert pricing.status.value == "ABSTAIN" and pricing.findings == []


def test_reprinting_after_valid_until_prints_stale_not_actionable(coord, monkeypatch, capsys, tmp_path):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    rec = run_options(coord)
    data = str(tmp_path / "data")
    rc = main(["show", rec.report_id, "--data-dir", data, "--now", "2026-10-15T20:10:00Z"])
    out = capsys.readouterr().out
    assert rc == 0 and "STALE" not in out and "valid_until: 2026-10-15T20:14:30+00:00" in out
    rc = main(["show", rec.report_id, "--data-dir", data, "--now", "2026-10-15T20:30:00Z"])
    out = capsys.readouterr().out
    assert rc == 0 and out.startswith("STALE:") and "not actionable" in out and "[STALE, not actionable]" in out
    assert "## TRADE IDEAS ADVISORY [PARTIAL" in out
    rc = main(["show", "rep:doesnotexist", "--data-dir", data])
    assert rc == 4


def test_m1_fixture_run_hash_unchanged(coord):
    rec = coord.handle(M1_Q, as_of=M1_AS_OF, request_time=M1_AS_OF)
    assert rec.valid_until is None and "trade_ideas_advisory" not in {s.section for s in rec.sections}
    assert rec.content_hash == M1_BASELINE_HASH


def test_options_without_horizon_still_clarifies(coord, monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    out = coord.handle(Q, as_of=AS_OF, request_time=AS_OF, analyses=[Analysis.OPTIONS_STRUCTURES])
    assert out.__class__.__name__ == "Clarification" and out.reason == "horizon_missing"
