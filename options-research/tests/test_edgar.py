from dataclasses import replace
from datetime import datetime, timezone
from pathlib import Path

import pytest

from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.edgar import EdgarClient, EdgarConfigError
from options_research.connectors.ratelimit import RateLimiter
from options_research.evidence import EvidenceStore

CASSETTES = PROJECT_ROOT / "fixtures" / "cassettes" / "edgar"


def cfg_with_ua(ua="Ankur Patel test@example.com"):
    cfg = load_config()
    raw = dict(cfg.raw)
    raw["edgar"] = {**raw.get("edgar", {}), "user_agent": ua}
    return replace(cfg, raw=raw)


@pytest.fixture
def client():
    return EdgarClient(ReplayHttpClient(CASSETTES), cfg_with_ua())


def test_requires_user_agent_with_contact():
    with pytest.raises(EdgarConfigError):
        EdgarClient(ReplayHttpClient(CASSETTES), load_config())  # default is empty
    with pytest.raises(EdgarConfigError):
        EdgarClient(ReplayHttpClient(CASSETTES), cfg_with_ua("just-a-name"))


def test_lookup_exact_symbol_and_ambiguous_name(client):
    lk = client.lookup()
    assert [i.canonical for i in lk.by_symbol("fixt")] == ["XNAS:FIXT"]
    assert {i.symbol for i in lk.resolve_from_text("Thoughts on Acme?")} == {"ACME", "ACMA"}
    assert lk.by_symbol("ZZZZ") == []
    assert lk.cik_for("FIXT") == 1000001


def test_filings_become_evidence_with_acceptance_time_and_asof_filter(client, tmp_path):
    recs = client.filing_evidence("FIXT", retrieved_at=datetime(2026, 10, 16, 12, 0, tzinfo=timezone.utc))
    assert [r.title.split(":")[0] for r in recs] == ["8-K", "8-K", "10-Q"]  # Form 4 excluded
    late = next(r for r in recs if "20261010" in r.source_id)
    assert late.first_available_at == datetime(2026, 10, 10, 21, 30, tzinfo=timezone.utc)
    assert late.source_id.startswith("https://www.sec.gov/Archives/edgar/data/1000001/000100000126000031/")
    store = EvidenceStore(tmp_path / "e.jsonl")
    for r in recs:
        store.append(r)
    as_of = datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)
    q = store.query(as_of=as_of, window_start=datetime(2026, 10, 1, tzinfo=timezone.utc), window_end=as_of, entities=["FIXT"])
    assert {r.id for r in q.records} == {"edgar:0001000001-26-000030"}
    assert q.excluded_future == ["edgar:0001000001-26-000031"]  # filed 10-10 but accepted 21:30Z > as_of


def test_rate_limiter_spaces_calls():
    t = [0.0]
    slept = []

    def clock():
        return t[0]

    def sleep(s):
        slept.append(s)
        t[0] += s

    rl = RateLimiter(10, clock=clock, sleep=sleep)
    for _ in range(11):
        rl.acquire()
    assert sum(slept) >= 1.0 - 1e-9


def test_lookup_cached_single_request():
    class Counting(ReplayHttpClient):
        n = 0

        def get(self, *a, **k):
            Counting.n += 1
            return super().get(*a, **k)

    c = EdgarClient(Counting(CASSETTES), cfg_with_ua())
    c.lookup(); c.lookup(); c.company_tickers()
    assert Counting.n == 1
