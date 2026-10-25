import shutil
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from options_research.intake.models import Instrument
from options_research.market import MarketDataSpecialist, MarketDataUnavailable

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures" / "market"
FIXT = Instrument(symbol="FIXT", venue="XNAS", currency="USD", name="Fixture Semiconductors Inc")
AS_OF = datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)


@pytest.fixture
def mds():
    return MarketDataSpecialist(FIXTURES)


def test_open_bar_excluded_and_last_complete_bar_before_as_of(mds):
    snap = mds.snapshot(FIXT, AS_OF, "1h")
    assert snap.last_complete_bar <= AS_OF
    # the 19:30 bar closes at 20:30 and must not be used; the 19:00 bar closes exactly at 20:00 and is used
    assert snap.last_complete_bar == datetime(2026, 10, 10, 20, 0, tzinfo=timezone.utc)
    assert snap.freshness_seconds == 0
    earlier = mds.snapshot(FIXT, AS_OF - timedelta(minutes=1), "1h")
    assert earlier.last_complete_bar == datetime(2026, 10, 10, 19, 0, tzinfo=timezone.utc)
    assert earlier.bars_used == snap.bars_used - 1


def test_gap_is_reported_and_snapshot_degraded(mds):
    snap = mds.snapshot(FIXT, AS_OF, "1h", window_start=datetime(2026, 10, 7, tzinfo=timezone.utc))
    gaps = [f for f in snap.quality_failures if f.kind == "gap"]
    assert len(gaps) == 1 and gaps[0].at == datetime(2026, 10, 8, 4, 0, tzinfo=timezone.utc)
    assert snap.quality == "DEGRADED"
    assert snap.metric("window_return") is not None  # still computed, with the gap disclosed in assumptions


def test_nonfinite_value_marks_metrics_none_not_zero(mds, tmp_path):
    src = FIXTURES / "XNAS_FIXT_1h.csv"
    dst_dir = tmp_path / "m"
    dst_dir.mkdir()
    lines = src.read_text().splitlines()
    parts = lines[50].split(",")
    parts[5] = "nan"
    lines[50] = ",".join(parts)
    (dst_dir / "XNAS_FIXT_1h.csv").write_text("\n".join(lines) + "\n")
    snap = MarketDataSpecialist(dst_dir).snapshot(FIXT, AS_OF, "1h", window_start=datetime(2026, 10, 3, tzinfo=timezone.utc))
    assert any(f.kind == "nonfinite" for f in snap.quality_failures)
    for name in ("window_return", "realised_vol", "volume_ratio"):
        assert snap.metric(name) is None
        assert snap.receipts[name].unavailable_reason


def test_missing_fixture_raises_not_empty(mds):
    other = Instrument(symbol="ACME", venue="XNYS", currency="USD", name="Acme Holdings Corp")
    with pytest.raises(MarketDataUnavailable):
        mds.snapshot(other, AS_OF, "1h")


def test_receipts_reproduce_and_capture_the_drop(mds):
    a = mds.snapshot(FIXT, AS_OF, "1h")
    b = mds.snapshot(FIXT, AS_OF, "1h")
    assert a.snapshot_id == b.snapshot_id
    for k in a.receipts:
        assert a.receipts[k].receipt_id == b.receipts[k].receipt_id
        assert a.receipts[k].inputs_hash == b.receipts[k].inputs_hash
        assert a.receipts[k].result == b.receipts[k].result
    drop = a.receipts["largest_single_bar_drop"]
    assert drop.result is not None and -0.10 < drop.result < -0.06
    assert any("2026-10-06T14:00" in s for s in drop.assumptions)
    assert a.receipts["realised_vol"].units == "annualised fraction"
    # a different as-of changes inputs and therefore receipt ids
    c = mds.snapshot(FIXT, AS_OF - timedelta(hours=5), "1h")
    assert c.receipts["window_return"].receipt_id != a.receipts["window_return"].receipt_id
