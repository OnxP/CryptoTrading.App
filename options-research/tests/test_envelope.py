from datetime import datetime, timedelta, timezone

import pytest
from pydantic import TypeAdapter, ValidationError

from options_research.envelope import Envelope, Status
from options_research.findings import (
    BriefingSection,
    ClaimStatus,
    ClaimVerification,
    EventCandidate,
    EvidenceRef,
    Finding,
    MarketSnapshotRef,
)

T0 = datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)
REF = EvidenceRef(kind="cluster", id="c-1", content_hash="ab" * 32)


def make(**over):
    base = dict(
        project_id="options-research",
        run_id="run-1",
        agent_id="news_collector",
        model_id="mock",
        prompt_version="news_collector/v1",
        input_hashes={"evidence_query": "cd" * 32},
        as_of=T0,
        valid_until=T0 + timedelta(hours=6),
        status=Status.OK,
        evidence_refs=[REF],
        findings=[],
    )
    base.update(over)
    return Envelope(**base)


def test_unknown_status_rejected():
    with pytest.raises(ValidationError):
        make(status="APPROVED")


def test_naive_datetime_rejected():
    with pytest.raises(ValidationError):
        make(as_of=datetime(2026, 10, 10, 20, 0))


def test_non_utc_offset_rejected():
    tz = timezone(timedelta(hours=1))
    with pytest.raises(ValidationError):
        make(as_of=datetime(2026, 10, 10, 20, 0, tzinfo=tz))


def test_valid_until_before_as_of_rejected():
    with pytest.raises(ValidationError):
        make(valid_until=T0 - timedelta(minutes=1))


def test_usability_rules():
    now = T0 + timedelta(hours=1)
    assert make().is_usable(now)
    assert not make().is_usable(T0 + timedelta(hours=7))  # expired
    assert not make(status=Status.ERROR).is_usable(now)
    assert not make(status=Status.OK, evidence_refs=[]).is_usable(now)
    assert make(status=Status.REVIEW, evidence_refs=[]).is_usable(now)  # REVIEW may lack evidence


def test_ok_carries_no_authorisation():
    env = make()
    for name in ("approved", "approval", "order", "execute", "authorise", "authorize", "trade"):
        assert not hasattr(env, name)
        assert name not in Envelope.model_fields
    with pytest.raises(ValidationError):
        make(approved=True)  # extra fields forbidden


def test_findings_are_discriminated_union_with_evidence():
    ta = TypeAdapter(Finding)
    ev = ta.validate_python(
        {
            "type": "event_candidate",
            "cluster_id": "c-1",
            "headline": "FIXTURE: company announces thing",
            "independent_sources": 1,
            "evidence_refs": [REF.model_dump()],
        }
    )
    assert isinstance(ev, EventCandidate)
    cv = ta.validate_python(
        {
            "type": "claim_verification",
            "claim_id": "k-1",
            "claim_text": "fell 8%",
            "status": "CONTRADICTED",
            "evidence_refs": [REF.model_dump()],
        }
    )
    assert isinstance(cv, ClaimVerification) and cv.status is ClaimStatus.CONTRADICTED
    ms = ta.validate_python(
        {
            "type": "market_snapshot_ref",
            "snapshot_id": "s-1",
            "instrument": "XNAS:FIXT",
            "as_of": T0.isoformat(),
            "quality": "OK",
            "evidence_refs": [REF.model_dump()],
        }
    )
    assert isinstance(ms, MarketSnapshotRef)
    bs = ta.validate_python(
        {
            "type": "briefing_section",
            "section": "market_reaction",
            "status": "BLOCKED",
            "text": "",
            "blocked_reason": "market fixture missing",
            "evidence_refs": [REF.model_dump()],
        }
    )
    assert isinstance(bs, BriefingSection)
    # every finding needs at least one evidence ref
    with pytest.raises(ValidationError):
        ta.validate_python({"type": "event_candidate", "cluster_id": "c", "headline": "h",
                            "independent_sources": 1, "evidence_refs": []})
    # unknown discriminator rejected
    with pytest.raises(ValidationError):
        ta.validate_python({"type": "trade_idea", "evidence_refs": [REF.model_dump()]})


def test_content_hash_stable_across_roundtrip():
    env = make(findings=[EventCandidate(cluster_id="c-1", headline="h", independent_sources=2,
                                        evidence_refs=[REF])])
    h1 = env.content_hash()
    again = Envelope.from_json(env.model_dump_json())
    assert again == env
    assert again.content_hash() == h1
    # different run id -> different hash (hash covers identity too)
    assert make(run_id="run-2").content_hash() != make().content_hash()
