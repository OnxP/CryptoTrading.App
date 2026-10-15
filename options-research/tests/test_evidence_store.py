from datetime import datetime, timezone
from pathlib import Path

import pytest

from options_research.evidence import EvidenceRecord, EvidenceStore
from options_research.evidence.store import DuplicateRecord

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures"
AS_OF = datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)
W0 = datetime(2026, 10, 3, 20, 0, tzinfo=timezone.utc)


@pytest.fixture
def store(tmp_path):
    return EvidenceStore.load_fixture(tmp_path / "evidence.jsonl", FIXTURES / "news" / "fixt_2026_10.json")


def test_future_available_record_is_excluded_and_listed(store):
    res = store.query(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["FIXT"])
    ids = {r.id for r in res.records}
    assert "n-008" not in ids  # published 10-09 but first available 10-10T22:00 > as_of
    assert res.excluded_future == ["n-008"]
    # same query one day later sees it
    later = store.query(as_of=datetime(2026, 10, 11, 20, 0, tzinfo=timezone.utc),
                        window_start=W0, window_end=datetime(2026, 10, 11, 20, 0, tzinfo=timezone.utc),
                        entities=["FIXT"])
    assert "n-008" in {r.id for r in later.records}


def test_entity_filter_and_window(store):
    res = store.query(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["ACME"])
    assert [r.id for r in res.records] == ["n-010"]
    narrow = store.query(as_of=AS_OF, window_start=datetime(2026, 10, 7, tzinfo=timezone.utc),
                         window_end=AS_OF, entities=["FIXT"])
    assert {r.id for r in narrow.records} == {"n-009"}


def test_syndicated_copies_form_one_cluster_with_one_independent_source(store):
    res = store.query(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["FIXT"])
    by_origin = {c.origin_record_id: c for c in res.clusters}
    c = by_origin["n-002"]
    assert len(c.member_ids) == 6  # n-002 plus five copies
    assert c.independent_sources == 1
    assert len(c.publishers) == 6
    assert {c.origin_record_id for c in res.clusters} == {"n-001", "n-002", "n-009"}


def test_correction_appends_and_hides_original_only_once_available(store, tmp_path):
    before = (tmp_path / "evidence.jsonl").read_bytes()
    corr = store.append_correction(
        "n-002", new_id="n-002r2",
        title="FIXTURE: Correction: Fixture Semiconductors shares fell 3%, not 8%",
        text="FIXTURE TEXT. An earlier version misstated the decline.",
        published_at=datetime(2026, 10, 6, 18, 0, tzinfo=timezone.utc),
        first_available_at=datetime(2026, 10, 6, 18, 0, tzinfo=timezone.utc),
        retrieved_at=datetime(2026, 10, 10, 19, 0, tzinfo=timezone.utc),
    )
    assert corr.supersedes == "n-002" and corr.revision == 2
    assert store.get("n-002").title.endswith("fall 8% after revenue warning")  # original retrievable
    after = (tmp_path / "evidence.jsonl").read_bytes()
    assert after.startswith(before) and len(after) > len(before)  # append-only
    res = store.query(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["FIXT"])
    ids = {r.id for r in res.records}
    assert "n-002r2" in ids and "n-002" not in ids and "n-002" in res.excluded_superseded
    # before the correction was available, the original is what you saw
    early = store.query(as_of=datetime(2026, 10, 6, 17, 0, tzinfo=timezone.utc),
                        window_start=W0, window_end=datetime(2026, 10, 6, 17, 0, tzinfo=timezone.utc),
                        entities=["FIXT"])
    assert "n-002" in {r.id for r in early.records}


def test_no_overwrite_and_content_hash(store):
    rec = store.get("n-001")
    with pytest.raises(DuplicateRecord):
        store.append(rec)
    with pytest.raises(ValueError):
        EvidenceRecord(**{**rec.model_dump(), "content_hash": "0" * 64})


def test_reload_from_disk_is_identical(store, tmp_path):
    again = EvidenceStore(tmp_path / "evidence.jsonl")
    assert [r.id for r in again.all()] == [r.id for r in store.all()]
    q = dict(as_of=AS_OF, window_start=W0, window_end=AS_OF, entities=["FIXT"])
    assert again.query(**q).result_hash() == store.query(**q).result_hash()
