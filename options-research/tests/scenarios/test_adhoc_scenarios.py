"""End-to-end scenarios from CLAUDE_SETUP "Ad hoc tests must cover ...".

Each test names the spec scenario it covers. Scenarios M1 cannot honestly
cover are listed in docs/planning/OPTIONS_M1_TEST_REPORT.md, not faked here.
"""
import shutil
from datetime import datetime, timedelta, timezone

import pytest

from options_research.cli import main
from options_research.config import PROJECT_ROOT
from options_research.coordinator import Coordinator
from options_research.intake import Analysis, Clarification, IntakeError
from options_research.ledger import ReportLedger

FIXTURES = PROJECT_ROOT / "fixtures"
AS_OF = datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)


@pytest.fixture
def coord(tmp_path):
    return Coordinator.offline(fixtures_dir=FIXTURES, data_dir=tmp_path / "data")


def sec(rec, name):
    return next(s for s in rec.sections if s.section == name)


# S1: stock/news request without an account -------------------------------------------------
def test_s1_stock_news_without_any_account(coord):
    rec = coord.handle("Analyse FIXT news from the last seven days", as_of=AS_OF, request_time=AS_OF)
    assert rec.status in ("OK", "PARTIAL") and sec(rec, "material_news").status == "OK"
    dumped = rec.model_dump_json().lower()
    assert "account" not in dumped and "capital" not in dumped and "quantity" not in dumped


# S2: unknown / ambiguous symbols --------------------------------------------------------------
def test_s2_unknown_and_ambiguous_symbols(coord):
    assert isinstance(coord.handle("Tell me about ZZZZ", as_of=AS_OF, request_time=AS_OF), Clarification)
    out = coord.handle("Is Acme a buy?", as_of=AS_OF, request_time=AS_OF)
    assert isinstance(out, Clarification) and len(out.candidates) == 2
    assert ReportLedger(coord._reports.path).all() == []  # nothing recorded without a resolved instrument


# S3: exact window preservation ----------------------------------------------------------------
def test_s3_exact_window_preserved_end_to_end(coord):
    start, end = datetime(2026, 10, 6, 0, 0, tzinfo=timezone.utc), datetime(2026, 10, 7, 0, 0, tzinfo=timezone.utc)
    rec = coord.handle("FIXT news", as_of=AS_OF, request_time=AS_OF, window=(start, end))
    assert rec.request.window.start == start and rec.request.window.end == end
    news = sec(rec, "material_news").text
    assert "cluster:n-001" in news and "cluster:n-002" in news and "cluster:n-009" not in news  # 10-08 is outside
    assert "window defaulted" not in sec(rec, "scope").text


# S4: optional specialist routing --------------------------------------------------------------
def test_s4_only_requested_specialists_run(coord):
    rec = coord.handle("FIXT news", as_of=AS_OF, request_time=AS_OF, analyses=[Analysis.NEWS_SUMMARY])
    assert [e.agent_id for e in rec.envelopes] == ["news_collector"]
    rec2 = coord.handle("FIXT price move", as_of=AS_OF, request_time=AS_OF, analyses=[Analysis.MARKET_REACTION])
    assert [e.agent_id for e in rec2.envelopes] == ["market_data_specialist"]
    assert sec(rec2, "material_news").status == "BLOCKED"


# S5: missing options horizon ------------------------------------------------------------------
def test_s5_missing_options_horizon_clarifies_not_guesses(coord):
    out = coord.handle("Compare defined-risk options on FIXT for next month", as_of=AS_OF, request_time=AS_OF,
                       analyses=[Analysis.OPTIONS_STRUCTURES])
    assert isinstance(out, Clarification) and out.reason == "horizon_missing"


# S6: scoped follow-ups ------------------------------------------------------------------------
def test_s6_follow_up_keeps_lineage_and_changed_scope(coord):
    first = coord.handle("FIXT news", as_of=AS_OF, request_time=AS_OF)
    later = AS_OF + timedelta(days=1)
    second = coord.handle("Any update on FIXT?", as_of=later, request_time=later, parent_report_id=first.report_id)
    assert second.parent_report_id == first.report_id and second.as_of != first.as_of
    assert "CEO to step down" in sec(second, "material_news").text  # now available
    assert "CEO to step down" not in sec(first, "material_news").text
    with pytest.raises(KeyError):
        coord.handle("FIXT", as_of=later, request_time=later, parent_report_id="rep:doesnotexist")


# S7: failure / partial answers ----------------------------------------------------------------
def test_s7_specialist_failure_gives_partial_not_fabrication(tmp_path):
    fx = tmp_path / "fx"
    shutil.copytree(FIXTURES, fx)
    shutil.rmtree(fx / "model" / "news_collector")  # model has no answer for the collector
    rec = Coordinator.offline(fixtures_dir=fx, data_dir=tmp_path / "d").handle("FIXT news", as_of=AS_OF, request_time=AS_OF)
    assert rec.status == "PARTIAL"
    assert sec(rec, "material_news").status == "BLOCKED"
    assert sec(rec, "bearish_evidence").status == "BLOCKED"
    assert sec(rec, "market_reaction").status in ("OK", "PARTIAL")  # independent specialist still reports
    assert "Fixture Semiconductors pre-announces" not in rec.model_dump_json()  # nothing filled from memory


# S8: injected article instructions ------------------------------------------------------------
def test_s8_injected_instructions_are_data(coord):
    rec = coord.handle("FIXT news", as_of=AS_OF, request_time=AS_OF)
    assert "INJECTION SUSPECTED" in sec(rec, "material_news").text
    assert rec.request.delivery == "inline"
    keys = set()

    def walk(o):
        if isinstance(o, dict):
            for k, v in o.items():
                keys.add(k.lower()); walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
    walk(rec.model_dump(mode="json"))
    assert not {k for k in keys if "recipient" in k or "email" in k or "send" in k}


# S9: no implicit email ------------------------------------------------------------------------
def test_s9_no_implicit_email(coord, tmp_path, capsys):
    rec = coord.handle("Email me the FIXT news summary", as_of=AS_OF, request_time=AS_OF)
    assert rec.request.delivery == "inline"  # the words in the question do not select delivery
    with pytest.raises(IntakeError):
        coord._intake.build(question="FIXT news", requester="t", request_time=AS_OF, delivery="email")
    rc = main(["research", "FIXT news", "--as-of", "2026-10-10T20:00Z", "--data-dir", str(tmp_path / "d"),
               "--fixtures-dir", str(FIXTURES)])
    assert rc == 0 and "report_id:" in capsys.readouterr().out
    assert not any(p.suffix == ".eml" for p in (tmp_path / "d").rglob("*"))


# S10: research records rejected at the order boundary ----------------------------------------
def test_s10_no_order_boundary_exists(coord):
    rec = coord.handle("FIXT warned, buy it now", as_of=AS_OF, request_time=AS_OF)
    import options_research
    import pkgutil
    names = [m.name for m in pkgutil.walk_packages(options_research.__path__, "options_research.")]
    assert not [n for n in names if any(x in n for x in ("order", "broker", "execut"))]
    assert "buy" not in " ".join(s.text for s in rec.sections).lower()
