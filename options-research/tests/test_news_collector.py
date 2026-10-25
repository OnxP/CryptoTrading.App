import json
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from options_research.agents.news_collector import NewsCollector
from options_research.envelope import Status
from options_research.evidence import EvidenceRecord, EvidenceStore
from options_research.intake import FixtureInstrumentLookup, IntakeValidator
from options_research.model import Budget, MockModelClient, UsageLedger
from options_research.model.mock import MockFixture

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures"
AS_OF = datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)


@pytest.fixture
def store(tmp_path):
    return EvidenceStore.load_fixture(tmp_path / "evidence.jsonl", FIXTURES / "news" / "fixt_2026_10.json")


@pytest.fixture
def request_fixt():
    v = IntakeValidator(FixtureInstrumentLookup(FIXTURES / "instruments.json"), default_window_days=7)
    return v.build(question="Analyse FIXT news from the last seven days", requester="t",
                   request_time=AS_OF, as_of=AS_OF)


@pytest.fixture
def mock(tmp_path):
    return MockModelClient.from_dir(UsageLedger(tmp_path / "usage.jsonl"), FIXTURES / "model")


def test_injection_flagged_content_unchanged_no_delivery_fields(store, request_fixt, mock):
    env = NewsCollector(mock, store).run(request_fixt, run_id="r1", budget=Budget(4, 50_000))
    assert env.status is Status.OK
    blog = next(f for f in env.findings if f.cluster_id == "cluster:n-009")
    assert blog.injection_suspected is True
    assert store.get("n-009").text.startswith("FIXTURE TEXT. Short interest")  # untouched
    def keys(obj):
        if isinstance(obj, dict):
            for k, v in obj.items():
                yield k.lower()
                yield from keys(v)
        elif isinstance(obj, list):
            for v in obj:
                yield from keys(v)
    all_keys = set(keys(env.model_dump(mode="json")))
    for word in ("recipient", "deliver", "email", "send", "order", "approved", "execute"):
        assert not any(word in k for k in all_keys), (word, all_keys)
    assert env.input_hashes["prompt_version"] == "news_collector/v1"
    assert len(env.input_hashes["evidence_query"]) == 64


def test_syndicated_copies_yield_one_candidate(store, request_fixt, mock):
    env = NewsCollector(mock, store).run(request_fixt, run_id="r1", budget=Budget(4, 50_000))
    wires = [f for f in env.findings if f.cluster_id == "cluster:n-002"]
    assert len(wires) == 1 and wires[0].independent_sources == 1
    assert {f.cluster_id for f in env.findings} == {"cluster:n-001", "cluster:n-002", "cluster:n-009"}
    assert any("excluded" in c for c in env.counterevidence)  # n-008 not yet available


def test_injection_detected_by_code_even_if_model_misses_it(store, request_fixt, tmp_path):
    good = json.loads((FIXTURES / "model" / "news_collector" / "fixt_2026_10_10.json").read_text())
    labels = json.loads(good["response"])
    for lab in labels:
        lab["injection_suspected"] = False
    client = MockModelClient(UsageLedger(tmp_path / "u.jsonl"),
                             [MockFixture("news_collector", "news_collector/v1", json.dumps(labels),
                                          match_substring="Instrument: XNAS:FIXT")])
    env = NewsCollector(client, store).run(request_fixt, run_id="r1", budget=Budget(4, 50_000))
    assert next(f for f in env.findings if f.cluster_id == "cluster:n-009").injection_suspected is True


@pytest.mark.parametrize("bad", [
    "not json at all",
    '[{"cluster_id": "cluster:n-999", "entities": []}]',               # unknown cluster
    '[{"cluster_id": "cluster:n-001", "trade": "buy calls"}]',          # unexpected key
    '[{"cluster_id": "cluster:n-001", "fact_or_rumour": "fact"}]',      # missing clusters
])
def test_schema_violation_is_error_not_partial(store, request_fixt, tmp_path, bad):
    client = MockModelClient(UsageLedger(tmp_path / "u.jsonl"),
                             [MockFixture("news_collector", "news_collector/v1", bad,
                                          match_substring="Instrument: XNAS:FIXT")])
    env = NewsCollector(client, store).run(request_fixt, run_id="r1", budget=Budget(4, 50_000))
    assert env.status is Status.ERROR and env.findings == []
    assert env.missing_inputs and "rejected" in env.missing_inputs[0]
    assert not env.is_usable(AS_OF)


def test_no_evidence_abstains_without_model_call(store, tmp_path):
    v = IntakeValidator(FixtureInstrumentLookup(FIXTURES / "instruments.json"), default_window_days=7)
    req = v.build(question="BRIT news", requester="t", request_time=AS_OF, as_of=AS_OF)
    ledger = UsageLedger(tmp_path / "u.jsonl")
    env = NewsCollector(MockModelClient(ledger, []), store).run(req, run_id="r1", budget=Budget(4, 50_000))
    assert env.status is Status.ABSTAIN and ledger.read_all() == []


def test_missing_fixture_is_error(store, request_fixt, tmp_path):
    env = NewsCollector(MockModelClient(UsageLedger(tmp_path / "u.jsonl"), []), store).run(
        request_fixt, run_id="r1", budget=Budget(4, 50_000))
    assert env.status is Status.ERROR and "unavailable" in env.missing_inputs[0]
