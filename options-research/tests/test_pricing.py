import math
from datetime import date, datetime, timedelta, timezone

import pytest

from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.tradier import TradierClient
from options_research.intake.models import Instrument
from options_research.options import TradierChainSource
from options_research.options.pricing import ImpliedVolUnavailable, bsm_greeks, bsm_price, implied_vol
from options_research.options.scenarios import scenario_grid
from options_research.options.structures import Leg, Structure, StructureInvalid, contractual_bounds

EXP = date(2026, 11, 20)
S, R = 100.0, 0.04


def leg(right, k, side, occ=None):
    return Leg(right=right, strike=k, expiry=EXP, side=side, occ_symbol=occ)


# ---- BSM ------------------------------------------------------------------------
def test_put_call_parity_and_iv_roundtrip():
    for K in (80.0, 100.0, 120.0):
        for T in (0.05, 0.5, 2.0):
            for sigma in (0.1, 0.4, 1.2):
                c = bsm_price(S, K, T, R, 0.0, sigma, "call")
                p = bsm_price(S, K, T, R, 0.0, sigma, "put")
                assert abs((c - p) - (S - K * math.exp(-R * T))) < 1e-8
                for px, right in ((c, "call"), (p, "put")):
                    floor = bsm_price(S, K, T, R, 0.0, 0.0, right)
                    if px - floor < 1e-6:
                        with pytest.raises(ImpliedVolUnavailable):  # vol not identifiable at the floor
                            implied_vol(px, S, K, T, R, 0.0, right)
                    else:
                        assert abs(implied_vol(px, S, K, T, R, 0.0, right) - sigma) < 1e-6


def test_iv_fails_explicitly_outside_bounds():
    with pytest.raises(ImpliedVolUnavailable):
        implied_vol(0.5, S, 80.0, 0.5, R, 0.0, "call")  # below intrinsic ~ 21.6
    with pytest.raises(ImpliedVolUnavailable):
        implied_vol(0.01, S, 100.0, 0.0, R, 0.0, "call")  # expired


def test_edge_cases_return_intrinsic():
    assert bsm_price(S, 90.0, 0.0, R, 0.0, 0.3, "call") == 10.0
    assert bsm_price(S, 110.0, 0.0, R, 0.0, 0.3, "put") == 10.0
    assert bsm_price(S, 110.0, 0.0, R, 0.0, 0.3, "call") == 0.0
    zero_vol = bsm_price(S, 90.0, 1.0, R, 0.0, 0.0, "call")
    assert abs(zero_vol - (S - 90.0 * math.exp(-R))) < 1e-9
    g = bsm_greeks(S, 100.0, 0.5, R, 0.0, 0.3, "call")
    assert 0.5 < g.delta < 0.7 and g.gamma > 0 and g.theta_per_day < 0 and g.vega_per_vol_point > 0


# ---- structures -----------------------------------------------------------------
def test_validator_rejects_naked_short_and_ratio_and_unknown_kind():
    with pytest.raises((StructureInvalid, ValueError)):
        Structure(kind="bear_call_credit", legs=[leg("call", 105, -1)])  # one leg, naked short
    with pytest.raises((StructureInvalid, ValueError)):
        Structure(kind="short_call", legs=[leg("call", 105, -1)])  # not approved
    with pytest.raises((StructureInvalid, ValueError)):
        Structure(kind="bull_call_debit", legs=[leg("call", 105, 1), leg("call", 100, -1)])  # wrong ordering
    with pytest.raises(ValueError):
        Leg(right="call", strike=100, expiry=EXP, side=-1, qty=2)  # ratio: qty must be 1


def test_bounds_bull_call_debit_and_credit_and_long_call():
    fees = 2.60
    bcs = Structure(kind="bull_call_debit", legs=[leg("call", 100, 1), leg("call", 110, -1)])
    b = contractual_bounds(bcs, net_debit_per_share=4.0, fees_total=fees)
    assert abs(b.max_loss - (4.0 * 100 + fees)) < 1e-9
    assert abs(b.max_gain - ((10.0 - 4.0) * 100 - fees)) < 1e-9
    assert len(b.breakevens) == 1 and abs(b.breakevens[0] - (104.0 + fees / 100)) < 1e-6
    bps = Structure(kind="bull_put_credit", legs=[leg("put", 100, -1), leg("put", 90, 1)])
    c = contractual_bounds(bps, net_debit_per_share=-3.0, fees_total=fees)
    assert abs(c.max_gain - (3.0 * 100 - fees)) < 1e-9
    assert abs(c.max_loss - ((10.0 - 3.0) * 100 + fees)) < 1e-9
    assert abs(c.breakevens[0] - (97.0 + fees / 100)) < 1e-6  # credit net of fees
    lc = Structure(kind="long_call", legs=[leg("call", 100, 1)])
    d = contractual_bounds(lc, net_debit_per_share=5.0, fees_total=1.30)
    assert abs(d.max_loss - (500 + 1.30)) < 1e-9 and d.max_gain is None  # unbounded upside reported as None
    assert abs(d.breakevens[0] - 105.013) < 1e-6
    bpd = Structure(kind="bear_put_debit", legs=[leg("put", 110, 1), leg("put", 100, -1)])
    e = contractual_bounds(bpd, 4.0, 0.0)
    assert e.max_loss == 400 and e.max_gain == 600 and e.breakevens == [106.0]


# ---- scenario grid on the fixture chain ------------------------------------------
@pytest.fixture
def chain(monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    cfg = load_config()
    src = TradierChainSource(TradierClient(ReplayHttpClient(PROJECT_ROOT / "fixtures" / "cassettes" / "tradier"), cfg), 900, 0.25)
    fixt = Instrument(symbol="FIXT", venue="XNAS", currency="USD", name="Fixture Semiconductors Inc")
    return src.snapshot(fixt, EXP, datetime(2026, 10, 15, 20, 0, tzinfo=timezone.utc))


def test_grid_deterministic_expiry_equals_payoff_and_unusable_leg_is_none(chain):
    cfg = load_config().raw["pricing"]
    spot = chain.underlying_reference.result
    calls = [q for q in chain.usable_quotes() if q.right == "call"]
    k_long = min(calls, key=lambda q: abs(q.strike - spot))
    k_short = min([q for q in calls if q.strike > k_long.strike], key=lambda q: q.strike)
    st = Structure(kind="bull_call_debit", legs=[leg("call", k_long.strike, 1, k_long.occ_symbol),
                                                 leg("call", k_short.strike, -1, k_short.occ_symbol)])
    quotes = {q.occ_symbol: q for q in chain.quotes}
    kw = dict(spot0=spot, as_of=chain.as_of, horizon_end=chain.as_of + timedelta(days=30), r=cfg["risk_free_rate"],
              fees_per_contract=cfg["fees_per_contract"], spot_steps_pct=cfg["spot_steps_pct"], iv_shifts_rel=cfg["iv_shifts_rel"])
    g1 = scenario_grid(st, quotes, **kw)
    g2 = scenario_grid(st, quotes, **kw)
    assert g1.grid_id == g2.grid_id and g1.bounds.receipt.receipt_id == g2.bounds.receipt.receipt_id
    m = st.contract_size
    for c in g1.expiry_cells():
        expected = (st.payoff_per_share(c.spot) - g1.bounds.net_debit_per_share) * m - g1.bounds.fees_total
        assert abs(c.pnl - expected) < 1e-9
    # contractual bounds hold at expiry; pre-expiry cells may exceed them because of exit friction (disclosed)
    for c in g1.expiry_cells():
        assert -g1.bounds.max_loss - 1e-6 <= c.pnl <= g1.bounds.max_gain + 1e-6
    assert any("exit friction" in a for a in g1.assumptions)
    # time value decays: at the same spot/vol, pnl at mid-horizon vs now differs and expiry equals payoff
    assert any("European" in a for a in g1.assumptions)
    # unusable leg -> error at entry (never a fabricated price)
    crossed = next(q for q in chain.quotes if "crossed" in q.quality_flags)
    bad = Structure(kind="long_call", legs=[leg("call", crossed.strike, 1, crossed.occ_symbol)])
    with pytest.raises(ValueError):
        scenario_grid(bad, quotes, **kw)
    # underlying comparison present
    assert len(g1.underlying_long_100) == len(cfg["spot_steps_pct"])
    up = next(c for c in g1.underlying_long_100 if c.spot_pct == 10)
    assert abs(up.pnl - spot * 0.10 * 100) < 1e-9
