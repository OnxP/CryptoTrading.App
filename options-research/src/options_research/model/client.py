"""The single seam through which agents reach a language model.

Agents never pick model ids; the client is constructed from config. Every call
is budgeted per request and recorded in the usage ledger.
"""
from __future__ import annotations

import hashlib
import json
import uuid
from abc import ABC, abstractmethod
from dataclasses import dataclass, field

from options_research.model.usage import UsageLedger, UsageRecord
from options_research.timeutil import utc_now


class ModelDisabled(RuntimeError):
    """Raised when a live client is constructed while model.enabled is false."""


class BudgetExceeded(RuntimeError):
    pass


class NoFixture(LookupError):
    """MockModelClient has no canned response for this input."""


@dataclass
class Budget:
    """Per-request budget. Enforced before each call and after token counts arrive."""

    max_calls: int
    max_tokens: int
    calls_used: int = 0
    tokens_used: int = 0

    def reserve_call(self) -> None:
        if self.calls_used + 1 > self.max_calls:
            raise BudgetExceeded(f"max_calls={self.max_calls} reached")
        self.calls_used += 1

    def charge_tokens(self, n: int) -> None:
        self.tokens_used += n
        if self.tokens_used > self.max_tokens:
            raise BudgetExceeded(f"max_tokens={self.max_tokens} exceeded ({self.tokens_used})")


@dataclass(frozen=True)
class ModelRequest:
    role: str
    prompt_version: str
    system: str
    messages: tuple[dict[str, str], ...]
    request_id: str = field(default_factory=lambda: uuid.uuid4().hex)

    def input_hash(self) -> str:
        payload = {
            "role": self.role,
            "prompt_version": self.prompt_version,
            "system": self.system,
            "messages": list(self.messages),
        }
        return hashlib.sha256(
            json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
        ).hexdigest()


@dataclass(frozen=True)
class ModelResponse:
    text: str
    model_id: str
    request_id: str
    input_tokens: int | None
    output_tokens: int | None


class ModelClient(ABC):
    def __init__(self, usage: UsageLedger, model_id: str):
        self._usage = usage
        self._model_id = model_id

    @property
    def model_id(self) -> str:
        return self._model_id

    def complete(self, request: ModelRequest, budget: Budget) -> ModelResponse:
        budget.reserve_call()
        response = self._complete(request)
        tokens = (response.input_tokens or 0) + (response.output_tokens or 0)
        self._usage.append(
            UsageRecord(
                at=utc_now(),
                request_id=request.request_id,
                role=request.role,
                prompt_version=request.prompt_version,
                model_id=response.model_id,
                input_hash=request.input_hash(),
                input_tokens=response.input_tokens,
                output_tokens=response.output_tokens,
                cost_estimate_usd=None,  # UNKNOWN until a price table is configured
            )
        )
        if tokens:
            budget.charge_tokens(tokens)
        return response

    @abstractmethod
    def _complete(self, request: ModelRequest) -> ModelResponse: ...
