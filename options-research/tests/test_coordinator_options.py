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
    assert agents[-3:] == ["pricing_specialist", "vol_specialist", "risk_reviewer"]
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


# ---- M3: volatility context (OPT-203) ---------------------------------------------------------------
def test_vol_context_block_precedes_structures_and_feeds_risk_review(coord, monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    rec = run_options(coord)
    vol_env = next(e for e in rec.envelopes if e.agent_id == "vol_specialist")
    assert vol_env.model_id == "none" and vol_env.status.value == "OK"
    v = vol_env.findings[0]
    assert v.type == "vol_assessment" and v.bars_used == 22
    for x in (v.realised_close_to_close, v.realised_parkinson, v.realised_yang_zhang, v.realised_ewma, v.forecast, v.atm_iv, v.skew_proxy):
        assert x is not None
    assert v.forecast_low <= v.forecast <= v.forecast_high and v.iv_rank is None and "no IV history" in v.iv_rank_reason
    assert v.premium_points is not None and v.premium_points > 10  # fixture chain is priced well above realised
    assert v.receipt_ids and all(r.startswith("rcpt:") for r in v.receipt_ids)
    adv = rec.sections[-1].text
    assert "volatility context" in adv and adv.index("volatility context") < adv.index("### long_call")
    for needle in ("close-to-close", "Parkinson", "Yang-Zhang", "EWMA", "forecast", "ATM IV", "skew proxy", "IV rank: unavailable", "vol points"):
        assert needle in adv
    risk = next(e for e in rec.envelopes if e.agent_id == "risk_reviewer")
    assert all(any("exceeds the realised-vol forecast" in r for r in f.reasons) for f in risk.findings)


def test_vol_context_unavailable_without_daily_bars_structures_still_priced(tmp_path, monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    from options_research.market import MarketDataUnavailable

    class NoBars:
        def load(self, *a, **k):
            raise MarketDataUnavailable("no daily bars offline")

    coord = Coordinator.offline(fixtures_dir=FIXTURES, data_dir=tmp_path / "data")
    coord._daily = NoBars()
    rec = run_options(coord)
    v = next(e for e in rec.envelopes if e.agent_id == "vol_specialist").findings[0]
    assert v.forecast is None and "no daily bars offline" in v.realised_unavailable_reason and v.atm_iv is not None
    assert v.premium_points is None
    adv = rec.sections[-1].text
    assert "realised: unavailable" in adv and "### long_call" in adv
    risk = next(e for e in rec.envelopes if e.agent_id == "risk_reviewer")
    assert all(any("vol premium: unavailable" in c for c in f.checks_performed) for f in risk.findings)
    assert not any("exceeds the realised-vol" in r for f in risk.findings for r in f.reasons)


def test_premium_below_threshold_is_a_check_not_a_review_reason(coord, monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    coord._cfg.raw["risk"] = {**coord._cfg.raw.get("risk", {}), "iv_premium_review_points": 1000}
    rec = run_options(coord)
    risk = next(e for e in rec.envelopes if e.agent_id == "risk_reviewer")
    assert all(any("vol premium +" in c and "threshold 1000" in c for c in f.checks_performed) for f in risk.findings)
    assert not any("exceeds the realised-vol" in r for f in risk.findings for r in f.reasons)
