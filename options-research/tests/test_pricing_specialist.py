import json
from datetime import date, datetime, timezone

import pytest

from options_research.agents.pricing_specialist import PricingConfig, PricingSpecialist, direction_hint
from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.tradier import TradierClient
from options_research.envelope import Status
from options_research.findings import CORRECT_DIRECTION_CAVEAT, StructureComparison
from options_research.intake import Analysis, FixtureInstrumentLookup, IntakeValidator
from options_research.model import Budget, MockModelClient, UsageLedger
from options_research.model.mock import MockFixture
from options_research.options import TradierChainSource, build_chain_snapshot
from options_research.options.chain import expiry_instant
from options_research.options.pricing import bsm_price, year_fraction

FIXTURES = PROJECT_ROOT / "fixtures"
AS_OF = datetime(2026, 10, 15, 20, 0, tzinfo=timezone.utc)
EXP = date(2026, 11, 20)
Q = "Compare defined-risk options on FIXT for the next month"


@pytest.fixture
def chain(monkeypatch):
    monkeypatch.setenv("TRADIER_SANDBOX_TOKEN", "x")
    cfg = load_config()
    src = TradierChainSource(TradierClient(ReplayHttpClient(FIXTURES / "cassettes" / "tradier"), cfg), 900, 0.25)
    fixt = FixtureInstrumentLookup(FIXTURES / "instruments.json").by_symbol("FIXT")[0]
    return src.snapshot(fixt, EXP, AS_OF)


def request(question=Q, horizon=30):
    iv = IntakeValidator(FixtureInstrumentLookup(FIXTURES / "instruments.json"), 7)
    return iv.build(question=question, requester="t", request_time=AS_OF, as_of=AS_OF,
                    requested_analyses=[Analysis.OPTIONS_STRUCTURES], horizon_days=horizon)


def specialist(tmp_path, fixtures=None):
    usage = UsageLedger(tmp_path / "u.jsonl")
    model = MockModelClient.from_dir(usage, FIXTURES / "model") if fixtures is None else MockModelClient(usage, fixtures)
    return PricingSpecialist(model, PricingConfig(load_config().raw))


def structures(env):
    return [f for f in env.findings if isinstance(f, StructureComparison) and f.alternative == "structure"]


def synthetic_chain(spot: float, sigma: float, as_of: datetime, expiry: date, symbol: str = "SYNT"):
    """Chain rows priced by BSM at one flat vol with a 2% half-spread; quote time = as_of."""
    T = year_fraction(as_of, expiry_instant(expiry))
    ms = int(as_of.timestamp() * 1000)
    rows = []
    for k in (80, 85, 90, 95, 100, 105, 110, 115, 120, 125, 130):
        for right in ("call", "put"):
            mid = bsm_price(spot, k, T, 0.04, 0.0, sigma, right)
            hs = max(0.02, mid * 0.02)
            occ = symbol + expiry.strftime("%y%m%d") + ("C" if right == "call" else "P") + f"{int(k * 1000):08d}"
            rows.append({"symbol": occ, "underlying": symbol, "expiration_date": expiry.isoformat(), "strike": k,
                         "option_type": right, "bid": round(mid - hs, 2), "ask": round(mid + hs, 2), "last": round(mid, 2),
                         "bid_date": ms, "ask_date": ms, "trade_date": ms, "open_interest": 1000, "volume": 100,
                         "contract_size": 100, "greeks": {}})
    return build_chain_snapshot(rows, underlying=symbol, expiration=expiry, expirations_available=[expiry], as_of=as_of,
                                received_at=as_of, underlying_price=spot, underlying_price_time=as_of, max_age_s=900,
                                wide_ratio=0.25, source="SYNTHETIC:test")


def test_usable_chain_compares_approved_set_with_underlying_and_no_trade(chain, tmp_path):
    env = specialist(tmp_path).run(request(), chain, None, None, "r1", Budget(8, 60_000))
    assert env.status is Status.OK and env.missing_inputs == []
    kinds = {s.kind for s in structures(env)}
    assert {"long_call", "long_put", "bull_call_debit", "bull_put_credit"} <= kinds
    assert {f.alternative for f in env.findings} == {"structure", "underlying_long_100", "no_trade"}
    for s in structures(env):
        assert s.receipt_ids and all(r.startswith(("rcpt:", "grid:")) for r in s.receipt_ids)
        assert s.quote_times and all(t.tzinfo is not None for t in s.quote_times)
        assert all(l.qty == 1 for l in s.legs) and len(s.scenario_rows) == 9
        assert CORRECT_DIRECTION_CAVEAT in s.caveats
        assert "FIXT261120" in s.note  # narrative from the mock, naming only supplied legs
        assert s.direction in ("bullish", "bearish")
    lc = next(s for s in structures(env) if s.kind == "long_call")
    assert lc.max_gain_unbounded and lc.max_gain is None and lc.max_loss > 0
    assert lc.correct_direction_loss_cells  # +5% at horizon still negative on this fixture
    # valid_until bounded by the earliest leg quote expiry (quote 19:59:30 + 900s)
    assert env.valid_until == datetime(2026, 10, 15, 20, 14, 30, tzinfo=timezone.utc)
    dumped = env.model_dump_json().lower()
    for k in ("probability", "expected_return", "quantity_to_trade", '"order'):
        assert k not in dumped


def test_direction_input_limits_structures_and_is_restated(chain, tmp_path):
    env = specialist(tmp_path).run(request(), chain, None, None, "r1", Budget(8, 60_000), direction="bullish")
    assert {s.kind for s in structures(env)} == {"long_call", "bull_call_debit", "bull_put_credit"}
    assert env.input_hashes["direction_input"] == "bullish"
    assert "direction input: bullish" in env.rationale
    assert direction_hint("is FIXT bullish into results?") == "bullish"
    assert direction_hint("downside protection with puts") == "bearish"
    assert direction_hint(Q) is None


def test_chain_unavailable_abstains_thesis_only_with_zero_legs(tmp_path):
    env = specialist(tmp_path).run(request(), None, "token env var not set", None, "r1", Budget(8, 60_000))
    assert env.status is Status.ABSTAIN and env.findings == []
    assert any("option chain unavailable" in m for m in env.missing_inputs)
    assert "thesis-only" in env.rationale
    text = env.model_dump_json()
    assert "occ_symbol" not in text and "FIXT261120" not in text


def test_expiry_before_horizon_abstains(chain, tmp_path):
    env = specialist(tmp_path).run(request(horizon=60), chain, None, None, "r1", Budget(8, 60_000))
    assert env.status is Status.ABSTAIN and env.findings == [] and "before the horizon end" in env.missing_inputs[0]


def test_expensive_iv_correct_direction_still_loses(tmp_path):
    chain = synthetic_chain(100.0, 1.20, AS_OF, EXP)  # 120% vol: premium swamps a modest move
    env = specialist(tmp_path, fixtures=[]).run(request(), chain, None, None, "r1", Budget(8, 60_000))
    assert env.status is Status.OK  # narratives missing is not an error
    assert any("narrative unavailable" in m for m in env.missing_inputs)
    lc = next(s for s in structures(env) if s.kind == "long_call")
    up5 = next(r for r in lc.scenario_rows if r.spot_pct == 5)
    assert up5.pnl_expiry is not None and up5.pnl_expiry < 0 and up5.pnl_horizon_iv_down < 0
    assert any("spot +5%" in c for c in lc.correct_direction_loss_cells)
    assert CORRECT_DIRECTION_CAVEAT in lc.caveats
    assert any("correct-direction scenarios with negative P&L" in c for c in lc.caveats)


def test_model_naming_unknown_leg_or_order_wording_is_error(chain, tmp_path):
    bad = MockFixture(role="pricing_specialist", prompt_version="pricing_specialist/v1", match_substring='"kind": "long_call"',
                      response=json.dumps([{"kind": "long_call", "narrative": "Consider FIXT261120C00150000 instead."}]))
    env = specialist(tmp_path, fixtures=[bad]).run(request(), chain, None, None, "r1", Budget(8, 60_000), direction="bullish")
    assert env.status is Status.ERROR and env.findings == []
    assert "not in the input" in env.missing_inputs[0] and "FIXT261120C00150000" in env.missing_inputs[0]
    order = MockFixture(role="pricing_specialist", prompt_version="pricing_specialist/v1", match_substring='"kind": "long_call"',
                        response=json.dumps([{"kind": "long_call", "narrative": "Place a market order for the call."}]))
    env2 = specialist(tmp_path, fixtures=[order]).run(request(), chain, None, None, "r1", Budget(8, 60_000), direction="bullish")
    assert env2.status is Status.ERROR and "order instruction" in env2.missing_inputs[0]
