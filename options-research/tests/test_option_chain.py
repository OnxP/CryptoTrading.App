from datetime import date, datetime, timezone

import pytest

from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.tradier import TradierClient
from options_research.intake.models import Instrument
from options_research.options import ChainUnavailable, TradierChainSource
from options_research.options.chain import expiry_instant, nearest_expiration_at_or_after

CASSETTES = PROJECT_ROOT / "fixtures" / "cassettes" / "tradier"
FIXT = Instrument(symbol="FIXT", venue="XNAS", currency="USD", name="Fixture Semiconductors Inc")
AS_OF = datetime(2026, 10, 15, 20, 0, tzinfo=timezone.utc)
EXP = date(2026, 11, 20)


@pytest.fixture
def source(monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "test-token-not-real")
    cfg = load_config()
    t = cfg.raw["tradier"]
    return TradierChainSource(TradierClient(ReplayHttpClient(CASSETTES), cfg), t["max_quote_age_s"], t["wide_spread_ratio"])


def test_chain_quotes_have_utc_times_and_multiplier(source):
    snap = source.snapshot(FIXT, EXP, AS_OF)
    assert snap.underlying == "FIXT" and snap.expiration == EXP
    q = snap.usable_quotes()[0]
    assert q.contract_size == 100 and q.exercise_style == "american" and q.settlement == "physical"
    assert q.bid_time.tzinfo is not None and q.bid_time == datetime(2026, 10, 15, 19, 59, 30, tzinfo=timezone.utc)
    assert q.expiry_at == datetime(2026, 11, 20, 21, 0, tzinfo=timezone.utc)  # 16:00 EST (DST ended 1 Nov) = 21:00Z
    assert q.vendor_iv is not None and q.vendor_delta is not None
    assert snap.underlying_reference.result > 0 and snap.underlying_reference.receipt_id.startswith("rcpt:")


def test_quality_flags(source):
    snap = source.snapshot(FIXT, EXP, AS_OF)
    flags = snap.quality_summary
    assert flags.get("zero_bid") == 1 and flags.get("crossed") == 1 and flags.get("stale") == 1 and flags.get("wide", 0) >= 1
    for q in snap.quotes:
        if "zero_bid" in q.quality_flags or "crossed" in q.quality_flags or "stale" in q.quality_flags:
            assert not q.usable() and q.mid is None
    assert len(snap.usable_quotes()) == len(snap.quotes) - 3


def test_unlisted_expiration_is_unavailable(source):
    with pytest.raises(ChainUnavailable):
        source.snapshot(FIXT, date(2026, 11, 13), AS_OF)


def test_snapshot_id_reproduces(source):
    a = source.snapshot(FIXT, EXP, AS_OF)
    b = source.snapshot(FIXT, EXP, AS_OF)
    assert a.snapshot_id == b.snapshot_id and a.snapshot_id.startswith("chain:")


def test_missing_token_is_chain_unavailable(monkeypatch):
    monkeypatch.delenv("TRADIER_SANDBOX_TOKEN", raising=False)
    cfg = load_config()
    s = TradierChainSource(TradierClient(ReplayHttpClient(CASSETTES), cfg), 900, 0.25)
    with pytest.raises(ChainUnavailable):
        s.snapshot(FIXT, EXP, AS_OF)


def test_nearest_monthly_expiration():
    exps = [date(2026, 10, 16), date(2026, 10, 23), date(2026, 10, 30), date(2026, 11, 6), date(2026, 11, 20), date(2026, 12, 18)]
    assert nearest_expiration_at_or_after(exps, date(2026, 11, 14)) == date(2026, 11, 20)
    assert nearest_expiration_at_or_after(exps, date(2026, 11, 14), prefer_monthly=False) == date(2026, 11, 20)
    assert nearest_expiration_at_or_after(exps, date(2026, 10, 20), prefer_monthly=False) == date(2026, 10, 23)
    assert nearest_expiration_at_or_after(exps, date(2027, 1, 1)) is None
    assert expiry_instant(date(2026, 7, 17)) == datetime(2026, 7, 17, 20, 0, tzinfo=timezone.utc)  # EDT
