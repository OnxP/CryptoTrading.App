"""Record/replay cassettes for HTTP responses.

A cassette is one JSON file per (method, url, sorted params). Header VALUES are
never stored; only header names. `source` says whether the body came from a
live call or was hand-written from provider documentation.
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


class NoCassette(LookupError):
    pass


def cassette_key(method: str, url: str, params: Mapping[str, str] | None) -> str:
    canon = {"method": method.upper(), "url": url,
             "params": sorted((str(k), str(v)) for k, v in (params or {}).items())}
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

    def get(self, url: str, params: Mapping[str, str] | None = None,
            headers: Mapping[str, str] | None = None) -> HttpResponse:
        key = cassette_key("GET", url, params)
        c = self._cassettes.get(key)
        if c is None:
            raise NoCassette(f"no cassette {key} for GET {url} {dict(params or {})} in {self._dir}")
        return c.response()


class RecordingHttpClient:
    """Wraps a real client; writes a cassette per response; secrets never stored."""

    def __init__(self, inner: HttpClient, directory: Path, source: str = "live"):
        self._inner = inner
        self._dir = Path(directory)
        self._source = source

    def get(self, url: str, params: Mapping[str, str] | None = None,
            headers: Mapping[str, str] | None = None) -> HttpResponse:
        resp = self._inner.get(url, params=params, headers=headers)
        c = Cassette(key=cassette_key("GET", url, params), method="GET", url=url,
                     params={str(k): str(v) for k, v in (params or {}).items()},
                     header_names=scrub_header_names(headers), status=resp.status, json=resp.json,
                     received_at=resp.received_at, source=self._source)
        write_cassette(self._dir, c)
        return resp
