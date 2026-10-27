"""Yahoo Finance connector: daily chart bars and the equity screener.

Unofficial public endpoints, no key. The screener needs a session cookie
(set by `cookie_url`) and a crumb bound to it; both live only in the HTTP
client's memory for the run. No portfolio, account or order endpoint exists
here and tests assert that.
"""
from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

from options_research.config import Config
from options_research.connectors.http import HttpClient, HttpResponse
from options_research.connectors.ratelimit import RateLimiter


class YahooError(RuntimeError):
    pass


class YahooClient:
    CHART_PATH = "/v8/finance/chart/{symbol}"
    CRUMB_PATH = "/v1/test/getcrumb"
    SCREENER_PATH = "/v1/finance/screener"
    PROFILE_PATH = "/v10/finance/quoteSummary/{symbol}"

    def __init__(self, http: HttpClient, config: Config):
        y = config.raw.get("yahoo", {})
        self._http = http
        self._base = str(y.get("base_url", "https://query2.finance.yahoo.com")).rstrip("/")
        self._cookie_url = str(y.get("cookie_url", "https://fc.yahoo.com"))
        self._ua = str(y.get("user_agent", "options-research/0.1"))
        self.chart_range = str(y.get("chart_range", "3y"))
        self.region = str(y.get("region", "us"))
        self.exchanges = [str(e) for e in y.get("exchanges", ["NMS", "NYQ", "ASE"])]
        self._limiter = RateLimiter(float(y.get("max_rps", 4)))
        self._crumb: str | None = None

    # ---- plumbing ---------------------------------------------------------
    def _headers(self) -> dict[str, str]:
        return {"User-Agent": self._ua, "Accept": "application/json,text/plain"}

    @staticmethod
    def _check(r: HttpResponse, what: str) -> Any:
        if r.status != 200:
            raise YahooError(f"{what}: HTTP {r.status}")
        return r.json

    def _get(self, path: str, params: dict[str, str]) -> Any:
        self._limiter.acquire()
        return self._check(self._http.get(self._base + path, params=params, headers=self._headers()), path)

    # ---- chart ------------------------------------------------------------
    def chart(self, symbol: str, range_: str | None = None, interval: str = "1d") -> tuple[list[dict], dict]:
        """Daily OHLCV rows (split-adjusted; `adjclose` also dividend-adjusted) plus the chart meta.

        Rows with any null field are dropped and counted in meta["dropped_rows"].
        """
        j = self._get(self.CHART_PATH.format(symbol=symbol),
                      {"range": range_ or self.chart_range, "interval": interval, "events": "div,splits"})
        chart = j.get("chart", {}) if isinstance(j, dict) else {}
        if chart.get("error"):
            err = chart["error"]
            raise YahooError(f"chart {symbol}: {err.get('description') if isinstance(err, dict) else err}")
        results = chart.get("result") or []
        if not results:
            raise YahooError(f"chart {symbol}: empty result")
        res = results[0]
        ts = res.get("timestamp") or []
        quote = (res.get("indicators", {}).get("quote") or [{}])[0]
        adj = (res.get("indicators", {}).get("adjclose") or [{}])[0].get("adjclose") or [None] * len(ts)
        rows: list[dict] = []
        dropped = 0
        for i, t in enumerate(ts):
            vals = [quote.get(k, [None] * len(ts))[i] for k in ("open", "high", "low", "close", "volume")]
            if any(v is None for v in vals):
                dropped += 1
                continue
            o, h, lo, c, v = vals
            rows.append({"time": datetime.fromtimestamp(int(t), tz=timezone.utc), "open": float(o), "high": float(h),
                         "low": float(lo), "close": float(c),
                         "adjclose": float(adj[i]) if i < len(adj) and adj[i] is not None else float(c),
                         "volume": float(v)})
        meta = dict(res.get("meta", {}))
        meta["dropped_rows"] = dropped
        meta["splits"] = list(((res.get("events") or {}).get("splits") or {}).values())
        return rows, meta

    # ---- profile ----------------------------------------------------------
    def profile(self, symbol: str) -> dict:
        """Sector, industry and country from the assetProfile module (crumb-gated).

        Returns {"sector": None, "industry": None, "country": None, "unavailable_reason": str}
        when Yahoo has no profile; never raises for a missing symbol.
        """
        crumb = self._ensure_crumb()
        self._limiter.acquire()
        r = self._http.get(self._base + self.PROFILE_PATH.format(symbol=symbol),
                           params={"modules": "assetProfile,price", "crumb": crumb}, headers=self._headers())
        j = r.json if isinstance(r.json, dict) else {}
        qs = j.get("quoteSummary", {}) if isinstance(j, dict) else {}
        results = qs.get("result") or []
        if r.status != 200 or not results:
            err = (qs.get("error") or {}).get("description") if isinstance(qs.get("error"), dict) else None
            return {"sector": None, "industry": None, "country": None,
                    "unavailable_reason": err or f"profile: HTTP {r.status}"}
        prof = results[0].get("assetProfile") or {}
        price = results[0].get("price") or {}

        def raw(k):
            v = price.get(k)
            return v.get("raw") if isinstance(v, dict) else v

        return {"sector": prof.get("sector"), "industry": prof.get("industry"), "country": prof.get("country"),
                "unavailable_reason": None if prof.get("sector") else "profile has no sector",
                "quote": {"symbol": price.get("symbol", symbol), "shortName": price.get("shortName"),
                          "longName": price.get("longName"), "exchange": price.get("exchange"),
                          "quoteType": price.get("quoteType", "EQUITY"), "regularMarketPrice": raw("regularMarketPrice"),
                          "regularMarketVolume": raw("regularMarketVolume"),
                          "averageDailyVolume3Month": raw("averageDailyVolume3Month"), "marketCap": raw("marketCap"),
                          "regularMarketTime": raw("regularMarketTime")}}

    # ---- screener ---------------------------------------------------------
    def _ensure_crumb(self) -> str:
        if self._crumb:
            return self._crumb
        self._limiter.acquire()
        self._http.get(self._cookie_url, headers=self._headers())          # 404 body; sets the session cookie
        self._limiter.acquire()
        r = self._http.get(self._base + self.CRUMB_PATH, headers=self._headers())
        body = r.json
        crumb = body.get("_non_json_body") if isinstance(body, dict) else body
        if r.status != 200 or not isinstance(crumb, str) or not crumb.strip() or "<" in crumb:
            raise YahooError(f"crumb unavailable: HTTP {r.status}")
        self._crumb = crumb.strip()
        return self._crumb

    def screen_low_priced_by_volume(self, max_price: float, min_price: float, size: int,
                                    sort_field: str = "dayvolume") -> tuple[list[dict], int]:
        """Equities on `exchanges` in `region` with min_price < price < max_price, sorted by `sort_field` desc.

        Returns (quotes, total_matching). Quote dicts are Yahoo's, untouched.
        """
        if size < 1 or size > 250:
            raise ValueError("size must be 1..250")
        crumb = self._ensure_crumb()
        query = {"operator": "AND", "operands": [
            {"operator": "LT", "operands": ["intradayprice", float(max_price)]},
            {"operator": "GT", "operands": ["intradayprice", float(min_price)]},
            {"operator": "EQ", "operands": ["region", self.region]},
            {"operator": "OR", "operands": [{"operator": "EQ", "operands": ["exchange", e]} for e in self.exchanges]},
        ]}
        body = {"size": int(size), "offset": 0, "sortField": sort_field, "sortType": "DESC", "quoteType": "EQUITY",
                "query": query, "userId": "", "userIdType": "guid"}
        self._limiter.acquire()
        j = self._check(self._http.post(self._base + self.SCREENER_PATH,
                                        params={"crumb": crumb, "formatted": "false", "lang": "en-US", "region": "US"},
                                        headers=self._headers(), json=body), "screener")
        fin = j.get("finance", {}) if isinstance(j, dict) else {}
        if fin.get("error"):
            err = fin["error"]
            raise YahooError(f"screener: {err.get('description') if isinstance(err, dict) else err}")
        results = fin.get("result") or []
        if not results:
            raise YahooError("screener: empty result")
        quotes = [q for q in results[0].get("quotes", []) if q.get("quoteType", "EQUITY") == "EQUITY"]
        return quotes, int(results[0].get("total", len(quotes)))
