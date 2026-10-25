from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from options_research.intake import (
    Analysis,
    Clarification,
    FixtureInstrumentLookup,
    IntakeError,
    IntakeValidator,
    ResearchRequest,
)

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures"
REQ_TIME = datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)


@pytest.fixture
def validator():
    return IntakeValidator(FixtureInstrumentLookup(FIXTURES / "instruments.json"), default_window_days=7)


def build(v, **kw):
    kw.setdefault("requester", "ankur")
    kw.setdefault("request_time", REQ_TIME)
    return v.build(**kw)


def test_deictic_question_requires_clarification(validator):
    out = build(validator, question="Look at this stock and its news")
    assert isinstance(out, Clarification) and out.reason == "instrument_unresolved"


def test_ambiguous_name_lists_candidates_never_guesses(validator):
    out = build(validator, question="What is going on with Acme?")
    assert isinstance(out, Clarification) and out.reason == "instrument_ambiguous"
    assert {c.symbol for c in out.candidates} == {"ACME", "ACMA"}


def test_symbol_resolves(validator):
    out = build(validator, question="Analyse FIXT news from the last seven days")
    assert isinstance(out, ResearchRequest)
    assert out.instrument.canonical == "XNAS:FIXT"


def test_explicit_window_preserved_exactly(validator):
    start = datetime(2026, 10, 1, tzinfo=timezone.utc)
    end = datetime(2026, 10, 7, tzinfo=timezone.utc)
    out = build(validator, question="FIXT news", window=(start, end))
    assert out.window.start == start and out.window.end == end and out.window.source == "explicit"
    assert not any("window defaulted" in d for d in out.disclosures)


def test_omitted_window_uses_default_and_discloses(validator):
    out = build(validator, question="FIXT news", as_of=REQ_TIME)
    assert out.window.source == "default"
    assert out.window.end == REQ_TIME and out.window.start == REQ_TIME - timedelta(days=7)
    assert any("7 days" in d for d in out.disclosures)


def test_omitted_window_without_default_requires_clarification():
    v = IntakeValidator(FixtureInstrumentLookup(FIXTURES / "instruments.json"), default_window_days=None)
    out = build(v, question="FIXT news")
    assert isinstance(out, Clarification) and out.reason == "window_missing"


def test_as_of_defaults_to_request_time_and_is_recorded(validator):
    out = build(validator, question="FIXT news")
    assert out.as_of == REQ_TIME and out.as_of_source == "request_time"
    assert any("as_of defaulted" in d for d in out.disclosures)


def test_future_as_of_rejected(validator):
    with pytest.raises(IntakeError):
        build(validator, question="FIXT news", as_of=REQ_TIME + timedelta(seconds=1))


def test_window_after_as_of_rejected(validator):
    with pytest.raises(IntakeError):
        build(validator, question="FIXT news", as_of=REQ_TIME,
              window=(REQ_TIME - timedelta(days=1), REQ_TIME + timedelta(hours=1)))


def test_news_summary_needs_no_horizon_but_options_does(validator):
    ok = build(validator, question="FIXT news", requested_analyses=[Analysis.NEWS_SUMMARY])
    assert isinstance(ok, ResearchRequest) and ok.horizon_days is None
    out = build(validator, question="Compare defined-risk options on FIXT",
                requested_analyses=[Analysis.OPTIONS_STRUCTURES])
    assert isinstance(out, Clarification) and out.reason == "horizon_missing"
    ok2 = build(validator, question="Compare defined-risk options on FIXT",
                requested_analyses=[Analysis.OPTIONS_STRUCTURES], horizon_days=30)
    assert isinstance(ok2, ResearchRequest)


def test_email_delivery_rejected_in_m1(validator):
    with pytest.raises(IntakeError):
        build(validator, question="FIXT news", delivery="email")
    out = build(validator, question="FIXT news")
    assert out.delivery == "inline"


def test_naive_datetimes_rejected(validator):
    with pytest.raises(ValueError):
        build(validator, question="FIXT news", request_time=datetime(2026, 10, 10))
