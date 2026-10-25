import math
import random
import statistics
from datetime import date, datetime, timedelta, timezone

import pytest

from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.tradier import TradierClient
from options_research.intake.models import Instrument
from options_research.market import MarketDataSpecialist
from options_research.market.bars import Bar
from options_research.market.sources import TradierBarSource
from options_research.options import TradierChainSource
from options_research.options.vol import (
    HAR_NOT_FITTED,
    FixedWeightHarForecaster,
    close_to_close,
    implied_summary,
    parkinson,
    realised_summary,
    vol_premium_points,
    yang_zhang,
)
from tests.test_pricing_specialist import synthetic_chain

FIXT = Instrument(symbol="FIXT", venue="XNAS", currency="USD", name="Fixture Semiconductors Inc")
AS_OF = datetime(2026, 10, 15, 20, 0, tzinfo=timezone.utc)
T0 = datetime(2025, 1, 1, 14, 30, tzinfo=timezone.utc)


def gbm_bars(n: int, sigma: float, seed: int = 7, drift: float = 0.0, steps: int = 200) -> list[Bar]:
    """Daily OHLC from an intraday GBM path (no overnight gap), so every estimator targets the same sigma."""
    rng = random.Random(seed)
    dt = 1.0 / TRADING / steps
    price = 100.0
    out = []
    for i in range(n):
        o = price
        path = [o]
        for _ in range(steps):
            path.append(path[-1] * math.exp((drift - 0.5 * sigma * sigma) * dt + sigma * math.sqrt(dt) * rng.gauss(0, 1)))
        price = path[-1]
        t = T0 + timedelta(days=i)
        out.append(Bar(open_time=t, close_time=t + timedelta(hours=6, minutes=30), open=o, high=max(path), low=min(path),
                       close=price, volume=1000))
    return out


TRADING = 252


def test_estimators_recover_constant_vol():
    bars = gbm_bars(250, 0.30)
    for est in (close_to_close, parkinson, yang_zhang):
        v = est(bars)
        assert v is not None and abs(v - 0.30) / 0.30 < 0.15, (est.__name__, v)


def test_yang_zhang_is_more_efficient_than_close_to_close_on_short_windows():
    cc, yz = [], []
    for seed in range(40):
        bars = gbm_bars(60, 0.25, seed=seed, drift=0.5)  # drift: YZ is drift-independent, close-to-close is not
        cc.append(close_to_close(bars))
        yz.append(yang_zhang(bars))
    assert statistics.pvariance(yz) < statistics.pvariance(cc)


def test_too_few_bars_gives_none_with_reason_and_short_window_is_disclosed():
    s1 = realised_summary(gbm_bars(1, 0.3))
    assert s1.unavailable_reason and all(r.result is None and r.unavailable_reason for r in s1.receipts.values())
    s10 = realised_summary(gbm_bars(10, 0.3))
    assert s10.forecast is not None and s10.forecast_low <= s10.forecast <= s10.forecast_high
    f = s10.receipts["vol_forecast"]
    assert HAR_NOT_FITTED in f.assumptions and any("only 10 bars" in a for a in f.assumptions)
    assert all(r.units == "annualised fraction" for k, r in s10.receipts.items() if k != "vol_forecast_range" or True)


def test_summary_is_deterministic_and_forecaster_seam_matches():
    bars = gbm_bars(40, 0.2)
    a, b = realised_summary(bars), realised_summary(bars)
    assert a.receipt_ids == b.receipt_ids and a.forecast == b.forecast
    assert FixedWeightHarForecaster().forecast(bars).forecast == a.forecast


@pytest.fixture
def replay(monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    cfg = load_config()
    client = TradierClient(ReplayHttpClient(PROJECT_ROOT / "fixtures" / "cassettes" / "tradier"), cfg)
    return cfg, client


def test_fixture_daily_bars_and_chain_give_a_full_vol_context(replay):
    cfg, client = replay
    snap = MarketDataSpecialist(TradierBarSource(client)).snapshot(FIXT, AS_OF, "1d", window_start=AS_OF - timedelta(days=30))
    bars = TradierBarSource(client).load(FIXT, "1d", AS_OF - timedelta(days=30), AS_OF).bars
    complete = [b for b in bars if b.close_time <= AS_OF]
    rs = realised_summary(complete)
    assert rs.bars_used == 22 and snap.bars_used == 21  # snapshot also drops the bar opening before window_start
    assert rs.forecast is not None and 0.05 < rs.forecast < 1.5
    chain = TradierChainSource(client, 900, 0.25).snapshot(FIXT, date(2026, 11, 20), AS_OF)
    spot = chain.underlying_reference.result
    im = implied_summary(chain, spot, AS_OF, cfg.raw["pricing"]["risk_free_rate"])
    assert im.atm_legs == ["FIXT261120C00110000", "FIXT261120P00110000"] and im.atm_iv is not None
    assert im.skew_legs == ["FIXT261120P00100000", "FIXT261120C00122500"]  # at/below 104.98 and at/above 116.03
    assert im.skew_proxy is not None and im.iv_rank is None and "no IV history" in im.iv_rank_reason
    assert im.receipts["atm_iv"].receipt_id.startswith("rcpt:") and "legs" not in im.receipts["iv_rank"].assumptions
    prem = vol_premium_points(im, rs)
    assert prem is not None and -100 < prem < 100


def test_synthetic_flat_vol_chain_round_trips_atm_and_zero_skew():
    chain = synthetic_chain(100.0, 0.40, AS_OF, date(2026, 11, 20))
    im = implied_summary(chain, 100.0, AS_OF, 0.04)
    assert abs(im.atm_iv - 0.40) < 2e-3 and abs(im.skew_proxy) < 2e-3
    assert im.skew_legs == ["SYNT261120P00095000", "SYNT261120C00105000"]


def test_unsolvable_leg_is_none_not_substituted():
    chain = synthetic_chain(100.0, 0.0001, AS_OF, date(2026, 11, 20))  # near-zero vol: OTM mids sit at the intrinsic floor
    im = implied_summary(chain, 100.0, AS_OF, 0.04)
    assert im.skew_proxy is None and any("floor" in n or "unusable" in n for n in im.notes)
    assert im.receipts["skew_proxy"].result is None and im.receipts["skew_proxy"].unavailable_reason
