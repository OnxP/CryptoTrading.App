import math
import random
from datetime import date, datetime, timedelta, timezone

import pytest

from options_research.market.bars import Bar
from options_research.market.sources import ny_session_bounds
from options_research.regime import RegimeConfig, assess, build_features

REGIMES = {"bull_quiet": (0.0012, 0.012, 0.85, 0.8), "bear_heavy": (-0.0030, 0.035, 1.9, 2.0), "flat": (0.0, 0.018, 1.0, 1.0)}


def make_bars(plan, seed=1, start=5.0, base_vol=1e7, last=date(2026, 10, 15)) -> list[Bar]:
    rng = random.Random(seed)
    n = sum(k for _, k in plan)
    days, d = [], last
    while len(days) < n:
        if d.weekday() < 5:
            days.append(d)
        d -= timedelta(days=1)
    days.reverse()
    bars, price, i = [], start, 0
    for regime, k in plan:
        mu, sig, vm, rm = REGIMES[regime]
        for _ in range(k):
            r = rng.gauss(mu, sig)
            op, cl = price * math.exp(rng.gauss(0, sig * 0.3)), price * math.exp(r)
            spread = abs(rng.gauss(0, sig)) * rm + 0.004
            o, c = ny_session_bounds(days[i])
            bars.append(Bar(open_time=o, close_time=c, open=op, high=max(op, cl) * (1 + spread / 2),
                            low=min(op, cl) * (1 - spread / 2), close=cl, volume=base_vol * vm * math.exp(rng.gauss(0, 0.35))))
            price, i = cl, i + 1
    return bars


CFG = RegimeConfig()


def test_features_window_and_warmup():
    bars = make_bars([("flat", 60)])
    fm = build_features(bars, volume_median_days=20, window=5)
    # bars 1..4 lack the 5-bar volume baseline (bar 0 has no return), then 4 lost to the window
    assert fm.n == 60 - 1 - 4 - 4 and fm.rows_skipped == 8
    assert fm.times[-1] == bars[-1].close_time
    assert all(abs(v) <= fm.winsor for row in fm.rows for v in row)
    assert all(abs(m) < 1e-9 for m in [sum(c) / len(c) for c in zip(*fm.rows)])   # z-scored
    assert build_features(bars[:3]).rows == []


def test_zero_volume_and_bad_range_do_not_crash():
    bars = make_bars([("flat", 40)])
    b = bars[20]
    bars[20] = b.model_copy(update={"volume": 0.0, "high": b.low, "low": b.high})   # inverted range, zero volume
    fm = build_features(bars)
    assert fm.n > 0 and all(math.isfinite(v) for row in fm.rows for v in row)


def test_short_history_is_unavailable_never_guessed():
    a = assess("NEWB", make_bars([("flat", 60)]), CFG)
    assert a.unavailable_reason and "need 120" in a.unavailable_reason
    assert a.flag == "UNAVAILABLE" and a.current is None and not a.turning_point


def test_long_stable_regime_is_stable_and_labelled():
    a = assess("ACME", make_bars([("bull_quiet", 350), ("flat", 300), ("bear_heavy", 100)], seed=3), CFG)
    assert a.unavailable_reason is None and a.converged
    assert a.current is not None and a.current.startswith("BEAR")
    assert a.days_in_regime >= 60 and a.p_current > 0.95
    assert a.flag in ("STABLE", "OVERDUE") and not a.turning_point
    assert len({s.label for s in a.states}) == len(a.states)             # labels unique
    assert any(s.label.startswith("BULL") for s in a.states)
    bear = next(s for s in a.states if s.label == a.current)
    assert bear.mean_daily_return_pct < 0 and bear.label.endswith("-V")   # the selloff state is the widest-range one
    assert all(abs(sum(row) - 1.0) < 1e-9 for row in a.transition)
    assert len(a.receipt_ids) == 2


def test_fresh_switch_is_a_turning_point():
    plan = [("flat", 250), ("bull_quiet", 300), ("bear_heavy", 198), ("bull_quiet", 12)]
    a = assess("FIXT", make_bars(plan, seed=7), RegimeConfig(fresh_days=15))
    assert a.unavailable_reason is None
    assert a.current.startswith("BULL") or a.current.startswith("FLAT")
    assert a.days_in_regime <= 15 and a.flag == "FRESH" and a.turning_point
    assert a.last_transition is not None and a.last_transition > date(2026, 9, 1)
    assert any("regime changed" in n for n in a.notes)


def test_assessment_is_deterministic_and_ascii():
    bars = make_bars([("bull_quiet", 200), ("bear_heavy", 100)], seed=5)
    a, b = assess("X", bars, CFG), assess("X", bars, CFG)
    assert a.model_dump_json() == b.model_dump_json()
    a.model_dump_json().encode("ascii")


def test_config_from_raw_ignores_unknown_keys():
    cfg = RegimeConfig.from_raw({"regime": {"states": 2, "seed": 3, "unknown": 1}})
    assert cfg.states == 2 and cfg.seed == 3 and cfg.min_bars == 120
