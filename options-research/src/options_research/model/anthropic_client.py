"""Live Anthropic client. Constructing it while model.enabled is false raises
ModelDisabled BEFORE the SDK is imported, so an offline environment never
needs the dependency and never reads credentials.

Enabling it is a configuration decision (plan question 4), not a code change.
"""
from __future__ import annotations

from options_research.config import ModelConfig
from options_research.model.client import ModelClient, ModelDisabled, ModelRequest, ModelResponse
from options_research.model.usage import UsageLedger


class AnthropicModelClient(ModelClient):
    def __init__(self, usage: UsageLedger, config: ModelConfig, model_id: str | None = None):
        if not config.enabled:
            raise ModelDisabled(
                "model.enabled is false in config; live model calls are not permitted"
            )
        super().__init__(usage, model_id or config.specialist_model)
        try:
            import anthropic  # noqa: F401  (lazy; only when enabled)
        except ImportError as exc:  # pragma: no cover - depends on environment
            raise ModelDisabled("anthropic SDK not installed; add it to requirements "
                                "once the model budget is approved") from exc
        self._sdk = anthropic.Anthropic()  # reads ANTHROPIC_API_KEY from env; never from repo files

    def _complete(self, request: ModelRequest) -> ModelResponse:  # pragma: no cover - live path
        msg = self._sdk.messages.create(
            model=self.model_id,
            max_tokens=4096,
            system=request.system,
            messages=list(request.messages),
        )
        text = "".join(block.text for block in msg.content if getattr(block, "type", "") == "text")
        return ModelResponse(
            text=text,
            model_id=msg.model,
            request_id=request.request_id,
            input_tokens=getattr(msg.usage, "input_tokens", None),
            output_tokens=getattr(msg.usage, "output_tokens", None),
        )
