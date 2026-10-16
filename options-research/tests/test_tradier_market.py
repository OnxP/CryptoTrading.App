from datetime import date, datetime, timedelta, timezone

import pytest

from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.tradier import TradierClient
from options_research.intake.models import Instrument
from options_research.market import MarketDataSpecialist, MarketDataUnavailable, TradierBarSource

CASSETTES = PROJECT_ROOT / "fixtures" / "cassettes" / "tradier"
FIXT = Instrument(symbol="FIXT", venue="XNAS", currency="USD", name="Fixture Semiconductors Inc")
AS_OF = datetime(2026, 10, 15, 20, 0, tzinfo=timezone.utc)      # 16:00 ET on 2026-10-15
W0 = datetime(2026, 9, 15, 0, 0, tzinfo=timezone.utc)


@pytest.fixture
def client(monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "test-token-not-real")
    return TradierClient(ReplayHttpClient(CASSETTES), load_config())


def test_missing_token_is_unavailable_not_traceback(monkeypatch):
    monkeypatch.delenv("TRADIER_SANDBOX_TOKEN", raising=False)
    mds = MarketDataSpecialist(TradierBarSource(TradierClient(ReplayHttpClient(CASSETTES), load_config())))
    with pytest.raises(MarketDataUnavailable) as e:
        mds.snapshot(FIXT, AS_OF, "1d", window_start=W0)
    assert "TRADIER_SANDBOX_TOKEN" in str(e.value)


def test_daily_bars_complete_only_and_session_times(client):
    mds = MarketDataSpecialist(TradierBarSource(client))
    snap = mds.snapshot(FIXT, AS_OF, "1d", window_start=W0)
    assert snap.last_complete_bar == AS_OF                      # 10-15 session closes exactly at as_of
    before_close = mds.snapshot(FIXT, AS_OF - timedelta(minutes=1), "1d", window_start=W0)
    assert before_close.last_complete_bar == datetime(2026, 10, 14, 20, 0, tzinfo=timezone.utc)
    assert before_close.bars_used == snap.bars_used - 1
    assert snap.source.startswith("TRADIER-SANDBOX:FIXT:1d")
    assert snap.delay_disclosure and "delayed" in snap.delay_disclosure


def test_daily_gap_detected_via_calendar_not_weekends(client):
    snap = MarketDataSpecialist(TradierBarSource(client)).snapshot(FIXT, AS_OF, "1d", window_start=W0)
    gaps = [f for f in snap.quality_failures if f.kind == "gap"]
    assert [f.at.date() for f in gaps] == [date(2026, 10, 8)]   # weekends are not gaps
    assert snap.quality == "DEGRADED"
    assert snap.metric("largest_single_bar_drop") is not None and snap.metric("largest_single_bar_drop") < -0.05


def test_no_accounts_endpoints_exist():
    import inspect

    src = inspect.getsource(TradierClient)
    assert "accounts" not in src and "orders" not in src


def test_quotes_and_sessions(client):
    q = client.quotes(["FIXT"])
    assert q[0]["symbol"] == "FIXT" and q[0]["bid"] < q[0]["ask"]
    s = client.sessions(date(2026, 10, 5), date(2026, 10, 9))
    assert s == [date(2026, 10, 5), date(2026, 10, 6), date(2026, 10, 7), date(2026, 10, 8), date(2026, 10, 9)]
