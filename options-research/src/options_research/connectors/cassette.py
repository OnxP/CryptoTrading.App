"""Record/replay cassettes for HTTP responses.

A cassette is one JSON file per (method, url, sorted params, body hash for
POST). Header VALUES are never stored; only header names. Session-bound query
params (Yahoo's `crumb`) are excluded from the key and stored redacted.
`source` says whether the body came from a live call or was hand-written from
provider documentation.
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Mapping

from options_research.connectors.http import HttpClient, HttpResponse
from options_research.timeutil import require_utc

_SECRET_HEADER_MARKERS = ("authorization", "key", "token", "secret", "cookie", "password")
_VOLATILE_PARAMS = frozenset({"crumb"})   # session-bound; useless without the cookie, which is never stored


class NoCassette(LookupError):
    pass


def _stable_params(params: Mapping[str, str] | None) -> list[tuple[str, str]]:
    return sorted((str(k), str(v)) for k, v in (params or {}).items() if str(k) not in _VOLATILE_PARAMS)


def stored_params(params: Mapping[str, str] | None) -> dict[str, str]:
    """Params as written to disk: volatile values redacted."""
    return {str(k): ("(volatile, redacted)" if str(k) in _VOLATILE_PARAMS else str(v))
            for k, v in (params or {}).items()}


def cassette_key(method: str, url: str, params: Mapping[str, str] | None, body: Any = None) -> str:
    canon: dict[str, Any] = {"method": method.upper(), "url": url, "params": _stable_params(params)}
    if body is not None:  # GET keys are unchanged from M2; POST keys include the JSON body
        canon["body_sha256"] = hashlib.sha256(
            json.dumps(body, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()
    return hashlib.sha256(json.dumps(canon, sort_keys=True).encode("utf-8")).hexdigest()[:20]


def scrub_header_names(headers: Mapping[str, str] | None) -> list[str]:
    """Return header names only, with secret-bearing ones marked redacted."""
    out = []
    for name in sorted(headers or {}):
        low = name.lower()
        out.append(f"{name} (redacted)" if any(m in low for m in _SECRET_HEADER_MARKERS) else name)
    return out


@dataclass(frozen=True)
class Cassette:
    key: str
    method: str
    url: str
    params: dict[str, str]
    header_names: list[str]
    status: int
    json: Any
    received_at: datetime
    source: str  # "live" | "docs"
    note: str = ""

    def to_dict(self) -> dict:
        return {
            "key": self.key, "source": self.source, "note": self.note,
            "request": {"method": self.method, "url": self.url, "params": dict(sorted(self.params.items())),
                        "header_names": self.header_names},
            "response": {"status": self.status, "json": self.json},
            "received_at": self.received_at.isoformat(),
        }

    @classmethod
    def from_dict(cls, d: dict) -> "Cassette":
        req, resp = d["request"], d["response"]
        return cls(key=d["key"], method=req["method"], url=req["url"], params=dict(req.get("params", {})),
                   header_names=list(req.get("header_names", [])), status=resp["status"], json=resp["json"],
                   received_at=require_utc(datetime.fromisoformat(d["received_at"].replace("Z", "+00:00"))),
                   source=d.get("source", "live"), note=d.get("note", ""))

    def response(self) -> HttpResponse:
        return HttpResponse(status=self.status, json=self.json, received_at=self.received_at, source=self.source)


def write_cassette(directory: Path, c: Cassette) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / f"{c.key}.json"
    path.write_text(json.dumps(c.to_dict(), indent=1, sort_keys=True), encoding="utf-8")
    return path


def load_cassettes(directory: Path) -> dict[str, Cassette]:
    out: dict[str, Cassette] = {}
    if not directory.exists():
        return out
    for p in sorted(directory.glob("*.json")):
        c = Cassette.from_dict(json.loads(p.read_text(encoding="utf-8")))
        out[c.key] = c
    return out


class ReplayHttpClient:
    """Serves cassettes only. A miss is an error, never a live call."""

    def __init__(self, directory: Path):
        self._dir = Path(directory)
        self._cassettes = load_cassettes(self._dir)

    def _serve(self, method: str, url: str, params: Mapping[str, str] | None, body: Any = None) -> HttpResponse:
        key = cassette_key(method, url, params, body)
        c = self._cassettes.get(key)
        if c is None:
            raise NoCassette(f"no cassette {key} for {method} {url} {stored_params(params)} in {self._dir}")
        return c.response()

    def get(self, url: str, params: Mapping[str, str] | None = None,
            headers: Mapping[str, str] | None = None) -> HttpResponse:
        return self._serve("GET", url, params)

    def post(self, url: str, params: Mapping[str, str] | None = None,
             headers: Mapping[str, str] | None = None, json: Any = None) -> HttpResponse:
        return self._serve("POST", url, params, json)


class RecordingHttpClient:
    """Wraps a real client; writes a cassette per response; secrets never stored."""

    def __init__(self, inner: HttpClient, directory: Path, source: str = "live"):
        self._inner = inner
        self._dir = Path(directory)
        self._source = source

    def _store(self, method: str, url: str, params: Mapping[str, str] | None, headers: Mapping[str, str] | None,
               resp: HttpResponse, body: Any = None) -> HttpResponse:
        c = Cassette(key=cassette_key(method, url, params, body), method=method, url=url, params=stored_params(params),
                     header_names=scrub_header_names(headers), status=resp.status, json=resp.json,
                     received_at=resp.received_at, source=self._source)
        write_cassette(self._dir, c)
        return resp

    def get(self, url: str, params: Mapping[str, str] | None = None,
            headers: Mapping[str, str] | None = None) -> HttpResponse:
        return self._store("GET", url, params, headers, self._inner.get(url, params=params, headers=headers))

    def post(self, url: str, params: Mapping[str, str] | None = None,
             headers: Mapping[str, str] | None = None, json: Any = None) -> HttpResponse:
        resp = self._inner.post(url, params=params, headers=headers, json=json)
        return self._store("POST", url, params, headers, resp, body=json)
