"""The one HTTP seam. Everything external goes through an HttpClient.

`HttpxClient` is the only implementation that touches the network and it
imports httpx lazily, so tests (sockets blocked) never need it.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Any, Mapping, Protocol

from options_research.timeutil import utc_now


@dataclass(frozen=True)
class HttpResponse:
    status: int
    json: Any
    received_at: datetime
    source: str = "live"  # "live" | "docs" | "replay"


class HttpClient(Protocol):
    def get(self, url: str, params: Mapping[str, str] | None = None,
            headers: Mapping[str, str] | None = None) -> HttpResponse: ...


class HttpxClient:
    """Real network client. Constructed only outside tests."""

    def __init__(self, timeout_s: float = 20.0):
        import httpx  # lazy: not needed for replay or tests

        self._client = httpx.Client(timeout=timeout_s, follow_redirects=False)

    def get(self, url: str, params: Mapping[str, str] | None = None,
            headers: Mapping[str, str] | None = None) -> HttpResponse:
        r = self._client.get(url, params=dict(params or {}), headers=dict(headers or {}))
        try:
            body = r.json()
        except ValueError:
            body = {"_non_json_body": r.text[:2000]}
        return HttpResponse(status=r.status_code, json=body, received_at=utc_now(), source="live")
