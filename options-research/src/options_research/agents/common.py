"""Shared helpers for agents: prompt loading, injection heuristics, JSON parsing."""
from __future__ import annotations

import json
import re
from pathlib import Path

from options_research.config import PROJECT_ROOT

PROMPTS_DIR = PROJECT_ROOT / "prompts"

_INJECTION_PATTERNS = [
    r"ignore (all |any )?(previous|prior|above) instructions",
    r"disregard (the )?(previous|prior|above)",
    r"\b(email|send|forward) (this|the report|it) to\b",
    r"\byou are (now )?(an?|the) (assistant|ai|model)\b",
    r"\bsystem prompt\b",
    r"\brecommend (buying|selling)\b",
    r"\bas an ai\b",
]
_INJECTION_RE = re.compile("|".join(_INJECTION_PATTERNS), re.IGNORECASE)


def load_prompt(role: str, version: str) -> tuple[str, str]:
    """Return (prompt_version_id, system_text) for prompts/<role>/<version>.md."""
    path = PROMPTS_DIR / role / f"{version}.md"
    return f"{role}/{version}", path.read_text(encoding="utf-8")


def injection_suspected(text: str) -> bool:
    return bool(_INJECTION_RE.search(text))


def delimit_untrusted(record_id: str, title: str, text: str) -> str:
    """Wrap article content so the model can see it is data, not instructions."""
    safe_title = title.replace("<<<", "<<").replace(">>>", ">>")
    safe_text = text.replace("<<<", "<<").replace(">>>", ">>")
    return f"<<<ARTICLE id={record_id}>>>\nTITLE: {safe_title}\n{safe_text}\n<<<END ARTICLE>>>"


class ModelOutputInvalid(ValueError):
    pass


def parse_json_array(text: str) -> list[dict]:
    """Parse a JSON array from model text; tolerate a fenced code block; nothing else."""
    s = text.strip()
    m = re.match(r"^```(?:json)?\s*(.*?)\s*```$", s, re.DOTALL)
    if m:
        s = m.group(1)
    try:
        data = json.loads(s)
    except json.JSONDecodeError as exc:
        raise ModelOutputInvalid(f"model output is not JSON: {exc}") from exc
    if not isinstance(data, list) or not all(isinstance(x, dict) for x in data):
        raise ModelOutputInvalid("model output is not a JSON array of objects")
    return data
