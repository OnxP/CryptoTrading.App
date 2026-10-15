from datetime import datetime, timezone
from pathlib import Path

import pytest

from options_research.agents.evidence_verifier import Claim, EvidenceVerifier
from options_research.envelope import Status
from options_research.evidence import EvidenceStore
from options_research.findings import ClaimStatus, Severity
from options_research.intake.models import Instrument
from options_research.market import CalculationReceipt, MarketDataSpecialist
from options_research.model import Budget, MockModelClient, UsageLedger
from options_research.policy.quarantine import QuarantinePolicy

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures"
AS_OF = datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)
W0 = datetime(2026, 10, 3, 20, 0, tzinfo=timezone.utc)
FIXT = Instrument(symbol="FIXT", venue="XNAS", currency="USD", name="Fixture Semiconductors Inc")


@pytest.fixture
def store(tmp_path):
    return EvidenceStore.load_fixture(tmp_path / "e.jsonl", FIXTURES / "news" / "fixt_2026_10.json")


@pytest.fixture
def query(store):
    return store.query(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["FIXT"])


@pytest.fixture
def snapshot():
    return MarketDataSpecialist(FIXTURES / "market").snapshot(FIXT, AS_OF, "1h", window_start=W0)


@pytest.fixture
def verifier(store, tmp_path):
    return EvidenceVerifier(MockModelClient.from_dir(UsageLedger(tmp_path / "u.jsonl"), FIXTURES / "model"), store)


def with_receipt(snapshot, value):
    r = CalculationReceipt.make("largest_single_bar_drop", {"synthetic": value}, "fraction", ["test"], value)
    return snapshot.model_copy(update={"receipts": {**snapshot.receipts, "largest_single_bar_drop": r}})


def test_numeric_claim_contradicted_by_receipt(verifier, query, snapshot):
    snap = with_receipt(snapshot, -0.031)
    env = verifier.run([Claim(claim_id="k", text="shares fell 8% after the warning", cluster_ids=["cluster:n-002"])],
                       query, snap, AS_OF, "r1", Budget(4, 10_000))
    f = env.findings[0]
    assert f.status is ClaimStatus.CONTRADICTED and f.severity is Severity.HIGH
    assert any("-8.0%" in u and "-3.10%" in u and "rcpt:" in u for u in f.unresolved_disagreements)
    assert any(r.kind == "receipt" for r in f.evidence_refs)


def test_numeric_claim_supported_when_receipt_agrees(verifier, query, snapshot):
    claims = [Claim(claim_id="cluster:n-001", text="pre-announces Q3 revenue below prior guidance",
                    cluster_ids=["cluster:n-001"]),
              Claim(claim_id="cluster:n-002", text="shares fell 8% after the warning", cluster_ids=["cluster:n-002"]),
              Claim(claim_id="cluster:n-009", text="massive short squeeze coming", cluster_ids=["cluster:n-009"])]
    env = verifier.run(claims, query, snapshot, AS_OF, "r1", Budget(4, 10_000))
    assert env.missing_inputs == []
    by = {f.claim_id: f for f in env.findings}
    assert by["cluster:n-002"].status is ClaimStatus.SOURCE_SUPPORTED  # fixture drop is about -7.9%
    assert "consistent with the market snapshot" in by["cluster:n-002"].note  # narrative from mock
    assert by["cluster:n-001"].status is ClaimStatus.INSUFFICIENT
    assert by["cluster:n-009"].status is ClaimStatus.INSUFFICIENT


def test_syndicated_only_is_insufficient(verifier, query, snapshot):
    env = verifier.run([Claim(claim_id="k", text="analysts at two brokers lowered price targets",
                              cluster_ids=["cluster:n-002"])], query, snapshot, AS_OF, "r1", Budget(4, 10_000))
    f = env.findings[0]
    assert f.status is ClaimStatus.INSUFFICIENT
    assert any("6 publisher" in u and "1 origin" in u for u in f.unresolved_disagreements)


def test_only_source_after_as_of_is_stale(verifier, query, snapshot):
    env = verifier.run([Claim(claim_id="k", text="CEO to step down", cluster_ids=["cluster:n-008"])],
                       query, snapshot, AS_OF, "r1", Budget(4, 10_000))
    f = env.findings[0]
    assert f.status is ClaimStatus.STALE and any("n-008" in u for u in f.unresolved_disagreements)


def test_extreme_consistent_move_not_flagged(verifier, query, snapshot):
    snap = with_receipt(snapshot, -0.35)
    env = verifier.run([Claim(claim_id="k", text="stock plunged 35% on the day", cluster_ids=["cluster:n-002"])],
                       query, snap, AS_OF, "r1", Budget(4, 10_000))
    assert env.findings[0].status is ClaimStatus.SOURCE_SUPPORTED


def test_numeric_without_snapshot_is_insufficient_not_guessed(verifier, query):
    env = verifier.run([Claim(claim_id="k", text="fell 8%", cluster_ids=["cluster:n-002"])],
                       query, None, AS_OF, "r1", Budget(4, 10_000))
    assert env.findings[0].status is ClaimStatus.INSUFFICIENT


def test_quarantine_marks_without_deleting(verifier, query, snapshot, store):
    snap = with_receipt(snapshot, -0.031)
    env = verifier.run([Claim(claim_id="k1", text="fell 8%", cluster_ids=["cluster:n-002"]),
                        Claim(claim_id="k2", text="dividend declared", cluster_ids=["cluster:n-001"])],
                       query, snap, AS_OF, "r1", Budget(4, 10_000))
    q = QuarantinePolicy().apply(env.findings)
    assert q.quarantined_claim_ids == ["k1"] and "cluster:n-002" in q.quarantined_evidence_ids
    assert q.blocks_new_entry is True
    assert store.get("n-002").text.startswith("FIXTURE TEXT. Shares")  # evidence untouched
    assert len(store.all()) == 10


def test_verdicts_stand_when_model_unavailable(store, query, snapshot, tmp_path):
    v = EvidenceVerifier(MockModelClient(UsageLedger(tmp_path / "u.jsonl"), []), store)
    env = v.run([Claim(claim_id="k", text="fell 8%", cluster_ids=["cluster:n-002"])],
                query, snapshot, AS_OF, "r1", Budget(4, 10_000))
    assert env.status is Status.OK and env.findings[0].status is ClaimStatus.SOURCE_SUPPORTED
    assert env.missing_inputs and "narrative unavailable" in env.missing_inputs[0]
