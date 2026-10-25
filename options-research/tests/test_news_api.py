from datetime import date, datetime, timezone

import pytest

from options_research.agents.news_collector import NewsCollector
from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.news_api import NewsApiClient, NewsApiUnavailable, canonical_url, ingest
from options_research.envelope import Status
from options_research.evidence import EvidenceStore
from options_research.intake import Analysis, FixtureInstrumentLookup, IntakeValidator
from options_research.model import Budget, MockModelClient, UsageLedger

CASSETTES = PROJECT_ROOT / "fixtures" / "cassettes" / "news"
AS_OF = datetime(2026, 10, 15, 20, 0, tzinfo=timezone.utc)
W0 = datetime(2026, 10, 1, 0, 0, tzinfo=timezone.utc)
LIVE_SINCE = datetime(2026, 10, 8, 0, 0, tzinfo=timezone.utc)


@pytest.fixture
def client(monkeypatch):
    monkeypatch.setenv("FINNHUB_API_KEY", "test-key-not-real")
    return NewsApiClient(ReplayHttpClient(CASSETTES), load_config())


def test_canonical_url_strips_tracking_and_case():
    a = canonical_url("https://News.Example.com/fixt/interim-cfo?utm_source=feed&utm_medium=rss#top")
    b = canonical_url("https://news.example.com/fixt/interim-cfo/")
    assert a == b == "https://news.example.com/fixt/interim-cfo"
    assert canonical_url("https://x.org/a?b=2&a=1") == "https://x.org/a?a=1&b=2"


def test_backfill_marked_and_hidden_unless_allowed(client, tmp_path):
    store = EvidenceStore(tmp_path / "e.jsonl")
    res = ingest(client, store, "FIXT", date(2026, 10, 8), date(2026, 10, 15), live_since=LIVE_SINCE)
    assert res.available and len(res.appended) == 5 and res.backfilled == 1
    old = store.get("finnhub:909990")
    assert old.backfill is True and old.published_at == datetime(2026, 10, 2, 11, 0, tzinfo=timezone.utc)
    live = store.get("finnhub:910010")
    assert live.backfill is False
    # availability is the FIRST ingestion time when it is later than publication
    assert live.first_available_at == datetime(2026, 10, 15, 18, 0, tzinfo=timezone.utc) and live.retrieved_at == live.first_available_at
    q = store.query(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["FIXT"])
    assert "finnhub:909990" in q.excluded_backfill and all(r.id != "finnhub:909990" for r in q.records)
    q2 = store.query(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["FIXT"], allow_backfill=True)
    assert "finnhub:909990" in q2.backfill_included and any(r.id == "finnhub:909990" for r in q2.records)
    assert q.result_hash() != q2.result_hash()  # disclosure changes the evidence basis
    # nothing stored is full article text
    assert all(len(r.text) < 400 for r in store.all())
    assert "test-key-not-real" not in (tmp_path / "e.jsonl").read_text(encoding="utf-8")


def test_everything_is_backfill_without_live_since(client, tmp_path):
    store = EvidenceStore(tmp_path / "e.jsonl")
    res = ingest(client, store, "FIXT", date(2026, 10, 8), date(2026, 10, 15))  # config live_since is empty
    assert res.backfilled == 5
    q = store.query(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["FIXT"])
    assert q.records == [] and len(q.excluded_backfill) == 5


def test_same_canonical_url_clusters_same_title_elsewhere_does_not(client, tmp_path):
    store = EvidenceStore(tmp_path / "e.jsonl")
    ingest(client, store, "FIXT", date(2026, 10, 8), date(2026, 10, 15), live_since=LIVE_SINCE)
    wire, copy, paper = store.get("finnhub:910001"), store.get("finnhub:910002"), store.get("finnhub:910003")
    assert wire.origin_id is None and copy.origin_id == wire.id
    assert paper.origin_id is None and paper.title_hash == wire.title_hash  # same title, different outlet: not clustered
    q = store.query(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["FIXT"])
    by_origin = {c.origin_record_id: c for c in q.clusters}
    assert set(by_origin[wire.id].member_ids) == {wire.id, copy.id} and by_origin[wire.id].independent_sources == 1
    assert by_origin[paper.id].member_ids == [paper.id]
    # re-ingesting the same cassette appends nothing new
    again = ingest(client, store, "FIXT", date(2026, 10, 8), date(2026, 10, 15), live_since=LIVE_SINCE)
    assert again.appended == [] and len(store.all()) == 5


def test_missing_key_is_unavailable_and_collector_abstains(monkeypatch, tmp_path):
    monkeypatch.delenv("FINNHUB_API_KEY", raising=False)
    client = NewsApiClient(ReplayHttpClient(CASSETTES), load_config())
    store = EvidenceStore(tmp_path / "e.jsonl")
    res = ingest(client, store, "FIXT", date(2026, 10, 8), date(2026, 10, 15))
    assert not res.available and "FINNHUB_API_KEY not set" in res.unavailable_reason and store.all() == []
    with pytest.raises(NewsApiUnavailable):
        client.company_news("FIXT", date(2026, 10, 8), date(2026, 10, 15))
    iv = IntakeValidator(FixtureInstrumentLookup(PROJECT_ROOT / "fixtures" / "instruments.json"), 7)
    req = iv.build(question="FIXT news", requester="t", request_time=AS_OF, as_of=AS_OF, requested_analyses=[Analysis.NEWS_SUMMARY])
    env = NewsCollector(MockModelClient(UsageLedger(tmp_path / "u.jsonl"), []), store).run(req, "r1", Budget(4, 10_000))
    assert env.status is Status.ABSTAIN and "no evidence available" in env.missing_inputs[0]


def test_unsupported_provider_refused_with_reason(monkeypatch):
    cfg = load_config()
    cfg.raw["news"] = {**cfg.raw["news"], "provider": "marketaux"}
    with pytest.raises(NewsApiUnavailable, match="query-parameter tokens"):
        NewsApiClient(ReplayHttpClient(CASSETTES), cfg)
