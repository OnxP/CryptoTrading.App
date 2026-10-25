"""Load options-research configuration (stdlib tomllib, no env-var secrets)."""
from __future__ import annotations

import tomllib
from dataclasses import dataclass
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CONFIG = PROJECT_ROOT / "config" / "default.toml"


@dataclass(frozen=True)
class ModelConfig:
    enabled: bool
    provider: str
    specialist_model: str
    cheap_model: str
    max_calls_per_request: int
    max_tokens_per_request: int


@dataclass(frozen=True)
class Config:
    project_id: str
    environment: str
    model: ModelConfig
    news_window_days: int | None
    timezone: str
    ledger_dir: Path
    inline_only: bool
    raw: dict  # full parsed TOML for provider sections ([tradier], [edgar], [pricing], ...)


def load_config(path: Path | None = None) -> Config:
    path = path or DEFAULT_CONFIG
    with open(path, "rb") as fh:
        raw = tomllib.load(fh)
    m = raw["model"]
    ledger_dir = Path(raw["ledger"]["dir"])
    if not ledger_dir.is_absolute():
        ledger_dir = PROJECT_ROOT / ledger_dir
    return Config(
        project_id=raw["project"]["id"],
        environment=raw["project"].get("environment", "offline-fixtures"),
        model=ModelConfig(
            enabled=bool(m.get("enabled", False)),
            provider=m.get("provider", "anthropic"),
            specialist_model=m["specialist_model"],
            cheap_model=m["cheap_model"],
            max_calls_per_request=int(m.get("max_calls_per_request", 8)),
            max_tokens_per_request=int(m.get("max_tokens_per_request", 60000)),
        ),
        news_window_days=raw.get("defaults", {}).get("news_window_days"),
        timezone=raw.get("defaults", {}).get("timezone", "UTC"),
        ledger_dir=ledger_dir,
        inline_only=bool(raw.get("delivery", {}).get("inline_only", True)),
        raw=raw,
    )
