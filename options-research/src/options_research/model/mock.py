"""Fixture-backed model client for offline runs and tests.

Responses are keyed by (role, prompt_version, input_hash). A fixture may
instead declare `match_substring`, matched against the last user message, so
fixture authors need not precompute hashes. Unknown inputs raise NoFixture;
the mock never improvises.

Fixture file format (JSON, under fixtures/model/<role>/<prompt_version>/*.json):
{
  "role": "news_collector", "prompt_version": "news_collector/v1",
  "input_hash": "<sha256>" | null, "match_substring": "..." | null,
  "response": "<model text>", "input_tokens": 1200, "output_tokens": 300
}
"""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from options_research.model.client import ModelClient, ModelRequest, ModelResponse, NoFixture
from options_research.model.usage import UsageLedger


@dataclass(frozen=True)
class MockFixture:
    role: str
    prompt_version: str
    response: str
    input_hash: str | None = None
    match_substring: str | None = None
    input_tokens: int | None = None
    output_tokens: int | None = None


class MockModelClient(ModelClient):
    def __init__(self, usage: UsageLedger, fixtures: list[MockFixture] | None = None,
                 model_id: str = "mock"):
        super().__init__(usage, model_id)
        self._fixtures: list[MockFixture] = list(fixtures or [])

    @classmethod
    def from_dir(cls, usage: UsageLedger, root: Path, model_id: str = "mock") -> "MockModelClient":
        fixtures = []
        for p in sorted(Path(root).rglob("*.json")):
            with open(p, encoding="utf-8") as fh:
                raw = json.load(fh)
            fixtures.append(MockFixture(**raw))
        return cls(usage, fixtures, model_id)

    def add(self, fixture: MockFixture) -> None:
        self._fixtures.append(fixture)

    def _complete(self, request: ModelRequest) -> ModelResponse:
        h = request.input_hash()
        last_user = next((m["content"] for m in reversed(request.messages) if m.get("role") == "user"), "")
        for f in self._fixtures:
            if f.role != request.role or f.prompt_version != request.prompt_version:
                continue
            if f.input_hash is not None and f.input_hash == h:
                return self._resp(f, request)
        for f in self._fixtures:
            if f.role != request.role or f.prompt_version != request.prompt_version:
                continue
            if f.input_hash is None and f.match_substring and f.match_substring in last_user:
                return self._resp(f, request)
        raise NoFixture(f"no mock response for role={request.role} "
                        f"prompt_version={request.prompt_version} input_hash={h[:12]}")

    def _resp(self, f: MockFixture, request: ModelRequest) -> ModelResponse:
        return ModelResponse(
            text=f.response,
            model_id=self.model_id,
            request_id=request.request_id,
            input_tokens=f.input_tokens,
            output_tokens=f.output_tokens,
        )
