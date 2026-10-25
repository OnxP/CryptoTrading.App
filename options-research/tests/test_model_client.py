import sys

import pytest

from options_research.config import ModelConfig
from options_research.model import (
    Budget,
    BudgetExceeded,
    MockModelClient,
    ModelDisabled,
    ModelRequest,
    NoFixture,
    UsageLedger,
)
from options_research.model.anthropic_client import AnthropicModelClient
from options_research.model.mock import MockFixture


def req(content="hello", role="news_collector", pv="news_collector/v1"):
    return ModelRequest(role=role, prompt_version=pv, system="sys",
                        messages=({"role": "user", "content": content},))


@pytest.fixture
def ledger(tmp_path):
    return UsageLedger(tmp_path / "usage.jsonl")


def test_mock_returns_fixture_by_hash_and_raises_for_unknown(ledger):
    r = req()
    client = MockModelClient(ledger, [MockFixture(role=r.role, prompt_version=r.prompt_version,
                                                  response="canned", input_hash=r.input_hash(),
                                                  input_tokens=10, output_tokens=5)])
    out = client.complete(r, Budget(max_calls=2, max_tokens=1000))
    assert out.text == "canned" and out.model_id == "mock"
    with pytest.raises(NoFixture):
        client.complete(req("something else"), Budget(max_calls=2, max_tokens=1000))


def test_mock_substring_match_and_prompt_version_isolation(ledger):
    client = MockModelClient(ledger, [MockFixture(role="x", prompt_version="x/v1", response="a",
                                                  match_substring="FIXT")])
    assert client.complete(req("about FIXT", role="x", pv="x/v1"), Budget(5, 1000)).text == "a"
    with pytest.raises(NoFixture):
        client.complete(req("about FIXT", role="x", pv="x/v2"), Budget(5, 1000))


def test_every_call_appends_usage_record(ledger):
    r = req()
    client = MockModelClient(ledger, [MockFixture(r.role, r.prompt_version, "ok", r.input_hash(),
                                                  input_tokens=7, output_tokens=3)])
    client.complete(r, Budget(5, 1000))
    client.complete(r, Budget(5, 1000))
    rows = ledger.read_all()
    assert len(rows) == 2
    row = rows[0]
    assert row["role"] == "news_collector"
    assert row["prompt_version"] == "news_collector/v1"
    assert row["model_id"] == "mock"
    assert row["input_tokens"] == 7 and row["output_tokens"] == 3
    assert row["cost_estimate_usd"] is None  # UNKNOWN, not 0
    assert row["request_id"] == r.request_id
    assert len(row["input_hash"]) == 64


def test_budget_enforced(ledger):
    r = req()
    client = MockModelClient(ledger, [MockFixture(r.role, r.prompt_version, "ok", r.input_hash(),
                                                  input_tokens=600, output_tokens=0)])
    b = Budget(max_calls=1, max_tokens=10_000)
    client.complete(r, b)
    with pytest.raises(BudgetExceeded):
        client.complete(r, b)
    b2 = Budget(max_calls=10, max_tokens=1000)
    client.complete(r, b2)
    with pytest.raises(BudgetExceeded):
        client.complete(r, b2)  # 1200 > 1000
    assert len(ledger.read_all()) == 3  # the over-budget call is still recorded


def test_anthropic_client_refuses_when_disabled_without_importing_sdk(ledger):
    cfg = ModelConfig(enabled=False, provider="anthropic", specialist_model="m", cheap_model="c",
                      max_calls_per_request=1, max_tokens_per_request=1)
    sys.modules.pop("anthropic", None)
    with pytest.raises(ModelDisabled):
        AnthropicModelClient(ledger, cfg)
    assert "anthropic" not in sys.modules
