"""News API connector -> evidence store (OPT-109).

Provider: Finnhub company-news (header auth `X-Finnhub-Token`). Marketaux was
the alternative; it authenticates with an `api_token` QUERY parameter, which
would put the secret into URLs, logs and cassette keys, so it is refused here
until a header-auth path exists. Provider choice remains Ankur's; config
`[news] provider` records it.

Documented shape (verified against one live recording on first use):
  GET https://finnhub.io/api/v1/company-news?symbol=X&from=YYYY-MM-DD&to=YYYY-MM-DD
  -> [{"category","datetime" (epoch s),"headline","id","image","related","source","summary","url"}, ...]

Point-in-time rules:
  * published_at comes from the provider; first_available_at = max(published_at,
    retrieved_at of the FIRST ingestion) because the provider does not say when
    it indexed an item.
  * Anything published before `live_since` (when continuous polling started;
    unset = never) is BACKFILL: stored with backfill=True and excluded from
    as-of queries unless the query explicitly allows backfill (disclosed).
  * Clustering: same canonical URL -> same origin. Same title from a different
    outlet is NOT clustered because Finnhub does not mark syndication.
Storage: headline, summary, URL and hashes only; no full article text.
"""
from __future__ import annotations

import hashlib
import os
from dataclasses import dataclass, field
from datetime import date, datetime, timezone
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

from options_research.config import Config
from options_research.connectors.http import HttpClient
from options_research.evidence import EvidenceRecord, EvidenceStore
from options_research.evidence.records import text_hash
from options_research.timeutil import require_utc

FINNHUB_URL = "https://finnhub.io/api/v1/company-news"
_TRACKING_PREFIXES = ("utm_", "fbclid", "gclid", "mc_cid", "mc_eid", "ref", "igshid")
SUPPORTED_PROVIDERS = ("finnhub",)
PROVIDER_MARKS_SYNDICATION = {"finnhub": False}


class NewsApiUnavailable(RuntimeError):
    pass


class NewsApiError(RuntimeError):
    pass


def canonical_url(url: str) -> str:
    """Lower-case scheme/host, drop fragment and tracking params, strip a trailing slash."""
    parts = urlsplit(url.strip())
    query = [(k, v) for k, v in parse_qsl(parts.query, keep_blank_values=True)
             if not k.lower().startswith(_TRACKING_PREFIXES)]
    path = parts.path.rstrip("/") or "/"
    return urlunsplit((parts.scheme.lower(), parts.netloc.lower(), path, urlencode(sorted(query)), ""))


def syndication_key(url: str) -> str:
    return hashlib.sha256(canonical_url(url).encode("utf-8")).hexdigest()[:24]


@dataclass(frozen=True)
class IngestResult:
    appended: list[str] = field(default_factory=list)
    duplicates: int = 0
    backfilled: int = 0
    unavailable_reason: str | None = None

    @property
    def available(self) -> bool:
        return self.unavailable_reason is None


class NewsApiClient:
    def __init__(self, http: HttpClient, config: Config):
        n = config.raw.get("news", {})
        self.provider = str(n.get("provider", "finnhub")).lower()
        if self.provider not in SUPPORTED_PROVIDERS:
            raise NewsApiUnavailable(
                f"news provider {self.provider!r} is not supported: only {SUPPORTED_PROVIDERS} use header authentication; "
                f"query-parameter tokens (e.g. Marketaux api_token) would leak into URLs and cassettes")
        self._http = http
        self._base = str(n.get("base_url", FINNHUB_URL))
        self._key_env = str(n.get("key_env", "FINNHUB_API_KEY"))
        self.allow_backfill_default = bool(n.get("allow_backfill", False))
        ls = str(n.get("live_since", "") or "").strip()
        self.live_since: datetime | None = require_utc(datetime.fromisoformat(ls.replace("Z", "+00:00"))) if ls else None

    # ---- plumbing ----------------------------------------------------------
    def _headers(self) -> dict[str, str]:
        key = os.environ.get(self._key_env)
        if not key:
            raise NewsApiUnavailable(f"news API key env var {self._key_env} not set")
        return {"X-Finnhub-Token": key, "Accept": "application/json"}

    def company_news(self, symbol: str, start: date, end: date) -> tuple[list[dict], datetime]:
        resp = self._http.get(self._base, params={"symbol": symbol.upper(), "from": start.isoformat(), "to": end.isoformat()},
                              headers=self._headers())
        if resp.status != 200:
            raise NewsApiError(f"{self.provider} company-news returned {resp.status}: {str(resp.json)[:200]}")
        body = resp.json
        if not isinstance(body, list):
            raise NewsApiError(f"{self.provider} company-news body is not a list")
        return body, resp.received_at

    # ---- mapping -----------------------------------------------------------
    def evidence(self, symbol: str, start: date, end: date, *, store: EvidenceStore | None = None,
                 live_since: datetime | None = None) -> tuple[list[EvidenceRecord], datetime]:
        """Map provider items to EvidenceRecords. Items already in `store` (by id) are skipped."""
        items, received_at = self.company_news(symbol, start, end)
        live_since = live_since or self.live_since
        origins: dict[str, str] = {}  # syndication key -> origin record id
        if store is not None:
            for rec in store.all():
                if rec.publisher_origin_key:
                    origins.setdefault(rec.publisher_origin_key, rec.origin_id or rec.id)
        out: list[EvidenceRecord] = []
        for it in sorted(items, key=lambda x: (int(x.get("datetime") or 0), str(x.get("id")))):
            rid = f"{self.provider}:{it.get('id')}"
            if store is not None and rid in {r.id for r in store.all()}:
                continue
            url = str(it.get("url") or "")
            published = datetime.fromtimestamp(int(it.get("datetime") or 0), tz=timezone.utc)
            key = syndication_key(url) if url else None
            origin_id = None
            if key is not None:
                if key in origins and origins[key] != rid:
                    origin_id = origins[key]
                else:
                    origins[key] = rid
            backfill = live_since is None or published < live_since
            out.append(EvidenceRecord(
                id=rid, source_id=url or rid, publisher=str(it.get("source") or self.provider), origin_id=origin_id,
                published_at=published, first_available_at=max(published, received_at), retrieved_at=received_at,
                title=str(it.get("headline") or ""), text=str(it.get("summary") or ""),
                entities=sorted({e.strip().upper() for e in str(it.get("related") or symbol).split(",") if e.strip()} | {symbol.upper()}),
                backfill=backfill, publisher_origin_key=key,
                title_hash=text_hash(str(it.get("headline") or "").strip().lower()),
            ))
        return out, received_at


def ingest(client: NewsApiClient, store: EvidenceStore, symbol: str, start: date, end: date,
           live_since: datetime | None = None) -> IngestResult:
    """Append new provider items to the store. Unavailable connector -> reason, nothing appended."""
    try:
        records, _ = client.evidence(symbol, start, end, store=store, live_since=live_since)
    except NewsApiUnavailable as exc:
        return IngestResult(unavailable_reason=str(exc))
    except NewsApiError as exc:
        return IngestResult(unavailable_reason=f"news API error: {exc}")
    appended: list[str] = []
    dup = 0
    for rec in records:
        try:
            store.append(rec)
            appended.append(rec.id)
        except ValueError:  # DuplicateRecord
            dup += 1
    return IngestResult(appended=appended, duplicates=dup, backfilled=sum(1 for r in records if r.backfill and r.id in appended))
