import inspect
import json
from datetime import timezone

import pytest

from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient, cassette_key
from options_research.connectors.cassette import NoCassette, stored_params
from options_research.connectors.yahoo import YahooClient, YahooError
from options_research.market import MarketDataUnavailable, YahooBarSource

CASSETTES = PROJECT_ROOT / "fixtures" / "cassettes" / "yahoo"


@pytest.fixture
def client():
    return YahooClient(ReplayHttpClient(CASSETTES), load_config())


def test_chart_rows_parsed_null_rows_dropped(client):
    rows, meta = client.chart("FIXT")
    assert meta["symbol"] == "FIXT" and meta["dropped_rows"] == 1
    assert len(rows) == 750
    assert rows[-1]["time"].tzinfo == timezone.utc and rows[0]["time"] < rows[-1]["time"]
    assert all(r["high"] >= max(r["open"], r["close"]) >= min(r["open"], r["close"]) >= r["low"] for r in rows)


def test_chart_error_is_yahoo_error_not_traceback(client):
    with pytest.raises(YahooError) as e:
        client.chart("NOPE")
    assert "404" in str(e.value) or "delisted" in str(e.value)
    with pytest.raises(MarketDataUnavailable):
        YahooBarSource(client).daily("NOPE")


def test_bar_source_uses_exchange_sessions(client):
    bars = YahooBarSource(client).daily("FIXT")
    assert len(bars) == 750
    b = bars[-1]
    assert b.close_time.hour == 20 and b.close_time.minute == 0        # 16:00 ET in October = 20:00Z
    assert (b.close_time - b.open_time).total_seconds() == 6.5 * 3600
    assert all(x.finite() for x in bars)


def test_screener_uses_crumb_and_returns_quotes_in_order(client):
    quotes, total = client.screen_low_priced_by_volume(10.0, 1.0, 3)
    assert [q["symbol"] for q in quotes] == ["FIXT", "ACME", "NEWB"] and total == 937
    assert client._crumb == "FIXTURECRUMB"
    quotes2, _ = client.screen_low_priced_by_volume(10.0, 1.0, 3)        # crumb cached, no second cookie dance
    assert quotes2 == quotes
    with pytest.raises(ValueError):
        client.screen_low_priced_by_volume(10.0, 1.0, 0)


def test_screener_without_cassette_raises_not_network(client):
    with pytest.raises(NoCassette):
        client.screen_low_priced_by_volume(50.0, 1.0, 3)


def test_crumb_is_volatile_in_cassette_keys_and_redacted_on_disk():
    url = "https://query2.finance.yahoo.com/v1/finance/screener"
    a = cassette_key("POST", url, {"crumb": "abc", "lang": "en-US"}, {"size": 3})
    b = cassette_key("POST", url, {"crumb": "xyz", "lang": "en-US"}, {"size": 3})
    assert a == b
    assert cassette_key("POST", url, {"lang": "en-US"}, {"size": 4}) != a       # body is part of the key
    assert cassette_key("GET", url, {"lang": "en-US"}) != cassette_key("GET", url, {"lang": "en-GB"})
    assert stored_params({"crumb": "abc", "lang": "en-US"}) == {"crumb": "(volatile, redacted)", "lang": "en-US"}
    for p in CASSETTES.glob("*.json"):
        d = json.loads(p.read_text(encoding="utf-8"))
        assert d["source"] == "docs"
        assert d["request"]["params"].get("crumb", "(volatile, redacted)") == "(volatile, redacted)"


def test_m2_get_keys_unchanged():
    # a GET key must be identical to the pre-M4 definition so Tradier/EDGAR cassettes still replay
    import hashlib
    url, params = "https://sandbox.tradier.com/v1/markets/quotes", {"symbols": "FIXT", "greeks": "false"}
    canon = {"method": "GET", "url": url, "params": sorted((k, v) for k, v in params.items())}
    legacy = hashlib.sha256(json.dumps(canon, sort_keys=True).encode("utf-8")).hexdigest()[:20]
    assert cassette_key("GET", url, params) == legacy


def test_no_account_portfolio_or_order_endpoints():
    src = inspect.getsource(YahooClient).lower()
    for word in ("portfolio", "account", "order", "trade", "watchlist"):
        assert word not in src


def test_profile_sector_industry_and_missing_symbol(client):
    prof = client.profile("FIXT")
    assert prof["sector"] == "Technology" and prof["industry"] == "Semiconductors" and prof["unavailable_reason"] is None
    assert client._crumb == "FIXTURECRUMB"
    missing = client.profile("NOPE")
    assert missing["sector"] is None and "not found" in missing["unavailable_reason"].lower()


def test_bar_source_drops_the_session_still_open(client):
    from datetime import datetime, timezone

    during = datetime(2026, 10, 15, 18, 0, tzinfo=timezone.utc)      # 14:00 ET on the last fixture session
    bars = YahooBarSource(client, now=lambda: during).daily("FIXT")
    assert len(bars) == 749 and bars[-1].close_time.date().isoformat() == "2026-10-14"
